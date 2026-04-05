using Godot;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Template.Godot.Twitch;

/// <summary>
/// Singleton service that manages Twitch integration: OAuth login, IRC chat reading,
/// and EventSub channel point redemptions. All public API is static for convenience.
/// Add this node to your scene tree (e.g. as an autoload) so _Process runs each frame.
/// </summary>
public partial class TwitchService : Node
{
    // ───────────────────────────── Singleton ─────────────────────────────
    public static TwitchService Instance { get; private set; }

    // ───────────────────────────── Configuration ─────────────────────────
    /// <summary>Replace with your registered Twitch application client ID.</summary>
    private const string ClientId = "PUT_YOUR_TWITCH_CLIENT_ID_HERE";
    private const string RedirectUri = "http://localhost:8910";
    private const string OAuthScopes = "chat:read channel:manage:redemptions channel:read:redemptions";

    private const string IrcWssUrl = "wss://irc-ws.chat.twitch.tv:443";
    private const string EventSubWssUrl = "wss://eventsub.wss.twitch.tv/ws";
    private const string TwitchApiBase = "https://api.twitch.tv/helix";

    private const int MaxChatterQueue = 100;
    private const double ReconnectDelaySec = 5.0;

    // ───────────────────────────── Settings persistence ──────────────────
    private const string SettingsPath = "user://twitch_settings.cfg";
    private const string SettingsSection = "twitch";

    // ───────────────────────────── Public state ──────────────────────────
    public static bool IsConnected => Instance != null && Instance._ircConnected && Instance._eventSubConnected;
    public static string ChannelName => Instance?._channelName;

    // ───────────────────────────── Events ────────────────────────────────
    public static event Action<string> OnLoveConfession;
    public static event Action<string, string> OnSayMessage;

    // ───────────────────────────── Internal state ────────────────────────
    private string _accessToken;
    private string _channelName;
    private string _broadcasterId;

    // IRC
    private WebSocketPeer _ircSocket;
    private bool _ircConnected;
    private bool _ircAuthenticated;
    private double _ircReconnectTimer;
    private StringBuilder _ircBuffer = new();

    // EventSub
    private WebSocketPeer _eventSubSocket;
    private bool _eventSubConnected;
    private string _eventSubSessionId;
    private double _eventSubReconnectTimer;
    private StringBuilder _eventSubBuffer = new();

    // Channel point reward IDs we created (so we can delete on disconnect)
    private string _loveConfessionRewardId;
    private string _saySomethingRewardId;

    // Chatter name queue
    private readonly Queue<string> _chatterQueue = new();
    private readonly HashSet<string> _chatterSet = new();
    private readonly object _chatterLock = new();

    // OAuth listener
    private HttpListener _httpListener;
    private CancellationTokenSource _oauthCts;

    // Pending API calls (run on main thread via _Process)
    private readonly Queue<Action> _mainThreadQueue = new();
    private readonly object _mainThreadLock = new();

    // Settings: channel point costs
    private int _loveConfessionCost = 500;
    private int _saySomethingCost = 200;

    // ═══════════════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═══════════════════════════════════════════════════════════════════════
    public override void _Ready()
    {
        Instance = this;
        LoadSettings();
    }

    public override void _Process(double delta)
    {
        // Drain main-thread queue
        lock (_mainThreadLock)
        {
            while (_mainThreadQueue.Count > 0)
            {
                try { _mainThreadQueue.Dequeue().Invoke(); }
                catch (Exception ex) { GD.PrintErr($"[TwitchService] Main-thread action error: {ex.Message}"); }
            }
        }

        // Poll IRC
        if (_ircSocket != null)
        {
            _ircSocket.Poll();
            var ircState = _ircSocket.GetReadyState();

            if (ircState == WebSocketPeer.State.Open)
            {
                while (_ircSocket.GetAvailablePacketCount() > 0)
                {
                    var data = _ircSocket.GetPacket();
                    var text = Encoding.UTF8.GetString(data);
                    HandleIrcData(text);
                }
            }
            else if (ircState == WebSocketPeer.State.Closed)
            {
                if (_ircConnected)
                {
                    GD.PrintErr("[TwitchService] IRC connection closed.");
                    _ircConnected = false;
                    _ircAuthenticated = false;
                }
                _ircReconnectTimer -= delta;
                if (_ircReconnectTimer <= 0 && !string.IsNullOrEmpty(_accessToken))
                {
                    _ircReconnectTimer = ReconnectDelaySec;
                    ConnectIrc();
                }
            }
        }

        // Poll EventSub
        if (_eventSubSocket != null)
        {
            _eventSubSocket.Poll();
            var esState = _eventSubSocket.GetReadyState();

            if (esState == WebSocketPeer.State.Open)
            {
                while (_eventSubSocket.GetAvailablePacketCount() > 0)
                {
                    var data = _eventSubSocket.GetPacket();
                    var text = Encoding.UTF8.GetString(data);
                    HandleEventSubData(text);
                }
            }
            else if (esState == WebSocketPeer.State.Closed)
            {
                if (_eventSubConnected)
                {
                    GD.PrintErr("[TwitchService] EventSub connection closed.");
                    _eventSubConnected = false;
                    _eventSubSessionId = null;
                }
                _eventSubReconnectTimer -= delta;
                if (_eventSubReconnectTimer <= 0 && !string.IsNullOrEmpty(_accessToken))
                {
                    _eventSubReconnectTimer = ReconnectDelaySec;
                    ConnectEventSub();
                }
            }
        }
    }

    public override void _ExitTree()
    {
        Disconnect();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Public Static API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts the OAuth flow: opens the browser and waits for the redirect token.
    /// After obtaining the token, connects IRC and EventSub automatically.
    /// </summary>
    public static void Connect()
    {
        if (Instance == null)
        {
            GD.PrintErr("[TwitchService] Instance not found. Add TwitchService node to the scene tree.");
            return;
        }
        Instance.StartOAuthFlow();
    }

    /// <summary>
    /// Disconnects IRC, EventSub, deletes auto-created rewards, and clears the token.
    /// </summary>
    public static void Disconnect()
    {
        Instance?.DoDisconnect();
    }

    /// <summary>
    /// Returns and removes the oldest chatter name from the queue, or null if empty.
    /// </summary>
    public static string GetNextChatterName()
    {
        if (Instance == null) return null;
        lock (Instance._chatterLock)
        {
            if (Instance._chatterQueue.Count == 0) return null;
            var name = Instance._chatterQueue.Dequeue();
            Instance._chatterSet.Remove(name);
            return name;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OAuth Flow
    // ═══════════════════════════════════════════════════════════════════════
    private void StartOAuthFlow()
    {
        _oauthCts?.Cancel();
        _oauthCts = new CancellationTokenSource();

        // Build the Twitch authorization URL (implicit grant)
        var authUrl = $"https://id.twitch.tv/oauth2/authorize" +
                      $"?client_id={ClientId}" +
                      $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                      $"&response_type=token" +
                      $"&scope={Uri.EscapeDataString(OAuthScopes)}";

        // Start local HTTP listener in background
        Task.Run(() => RunOAuthListener(_oauthCts.Token));

        // Open browser
        OS.ShellOpen(authUrl);
        GD.Print("[TwitchService] Opened browser for Twitch OAuth login.");
    }

    private async Task RunOAuthListener(CancellationToken ct)
    {
        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://localhost:8910/");
            _httpListener.Start();

            while (!ct.IsCancellationRequested)
            {
                var contextTask = _httpListener.GetContextAsync();
                // Wait for either the context or cancellation
                var completedTask = await Task.WhenAny(contextTask, Task.Delay(-1, ct)).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;

                var context = await contextTask.ConfigureAwait(false);
                var request = context.Request;
                var response = context.Response;

                // Check if this is the token callback (from our JS redirect)
                var token = request.QueryString["access_token"];
                if (!string.IsNullOrEmpty(token))
                {
                    // Token received — send success page and finish
                    var successHtml = "<html><body><h2>Twitch connected! You can close this tab.</h2><script>window.close();</script></body></html>";
                    var buffer = Encoding.UTF8.GetBytes(successHtml);
                    response.ContentType = "text/html";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    response.Close();

                    // Process the token on the main thread
                    EnqueueMainThread(() => OnTokenReceived(token));
                    break;
                }
                else
                {
                    // First request — serve the HTML page that extracts the hash fragment
                    // Twitch implicit grant puts the token in the URL hash (#access_token=...)
                    // which the browser doesn't send to the server, so we use JS to forward it.
                    var extractorHtml = @"<html><body>
<h2>Connecting to Twitch...</h2>
<script>
    var hash = window.location.hash.substring(1);
    var params = new URLSearchParams(hash);
    var token = params.get('access_token');
    if (token) {
        window.location.href = '/?access_token=' + encodeURIComponent(token);
    } else {
        document.body.innerHTML = '<h2>Error: No access token received.</h2>';
    }
</script>
</body></html>";
                    var buffer = Encoding.UTF8.GetBytes(extractorHtml);
                    response.ContentType = "text/html";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    response.Close();
                }
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        catch (ObjectDisposedException) { /* listener stopped */ }
        catch (Exception ex)
        {
            GD.PrintErr($"[TwitchService] OAuth listener error: {ex.Message}");
        }
        finally
        {
            try { _httpListener?.Stop(); } catch { /* ignore */ }
            _httpListener = null;
        }
    }

    private void OnTokenReceived(string token)
    {
        _accessToken = token;
        GD.Print("[TwitchService] OAuth token received.");
        SaveSettings();

        // Fetch user info, then connect IRC + EventSub
        Task.Run(async () =>
        {
            try
            {
                await FetchUserInfo().ConfigureAwait(false);
                EnqueueMainThread(() =>
                {
                    ConnectIrc();
                    ConnectEventSub();
                });
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TwitchService] Failed to fetch user info: {ex.Message}");
            }
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Twitch API helpers (using System.Net.HttpWebRequest for thread safety)
    // ═══════════════════════════════════════════════════════════════════════
    private async Task FetchUserInfo()
    {
        var json = await TwitchApiGet("/users").ConfigureAwait(false);
        // Minimal JSON parsing — extract login and id from {"data":[{"id":"...","login":"..."}]}
        _broadcasterId = ExtractJsonField(json, "id");
        _channelName = ExtractJsonField(json, "login");
        GD.Print($"[TwitchService] Logged in as: {_channelName} (id: {_broadcasterId})");
    }

    private async Task<string> TwitchApiGet(string endpoint)
    {
        var url = TwitchApiBase + endpoint;
        var request = WebRequest.CreateHttp(url);
        request.Method = "GET";
        request.Headers["Authorization"] = $"Bearer {_accessToken}";
        request.Headers["Client-Id"] = ClientId;

        using var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);
        using var stream = response.GetResponseStream();
        using var reader = new System.IO.StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private async Task<string> TwitchApiPost(string endpoint, string body)
    {
        var url = TwitchApiBase + endpoint;
        var request = WebRequest.CreateHttp(url);
        request.Method = "POST";
        request.ContentType = "application/json";
        request.Headers["Authorization"] = $"Bearer {_accessToken}";
        request.Headers["Client-Id"] = ClientId;

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        request.ContentLength = bodyBytes.Length;
        using (var reqStream = await request.GetRequestStreamAsync().ConfigureAwait(false))
        {
            await reqStream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
        }

        using var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);
        using var stream = response.GetResponseStream();
        using var reader = new System.IO.StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private async Task TwitchApiDelete(string endpoint)
    {
        var url = TwitchApiBase + endpoint;
        var request = WebRequest.CreateHttp(url);
        request.Method = "DELETE";
        request.Headers["Authorization"] = $"Bearer {_accessToken}";
        request.Headers["Client-Id"] = ClientId;

        using var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);
        // 204 No Content expected
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  IRC Chat Connection
    // ═══════════════════════════════════════════════════════════════════════
    private void ConnectIrc()
    {
        if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_channelName)) return;

        _ircSocket?.Close();
        _ircSocket = new WebSocketPeer();
        var err = _ircSocket.ConnectToUrl(IrcWssUrl);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[TwitchService] IRC WebSocket connect error: {err}");
            _ircReconnectTimer = ReconnectDelaySec;
            return;
        }

        _ircConnected = false;
        _ircAuthenticated = false;
        _ircBuffer.Clear();
        GD.Print("[TwitchService] Connecting to IRC...");

        // We'll detect the open state in _Process and send auth there,
        // but we can also watch for it via a one-shot timer:
        WaitForIrcOpen();
    }

    private async void WaitForIrcOpen()
    {
        // Poll until connected or timeout
        for (int i = 0; i < 100; i++) // ~5 seconds
        {
            await Task.Delay(50);
            if (_ircSocket == null) return;
            _ircSocket.Poll();
            if (_ircSocket.GetReadyState() == WebSocketPeer.State.Open)
            {
                _ircConnected = true;
                SendIrc($"PASS oauth:{_accessToken}");
                SendIrc($"NICK {_channelName}");
                SendIrc($"JOIN #{_channelName}");
                _ircAuthenticated = true;
                GD.Print($"[TwitchService] IRC connected and joined #{_channelName}");
                return;
            }
            if (_ircSocket.GetReadyState() == WebSocketPeer.State.Closed)
            {
                GD.PrintErr("[TwitchService] IRC connection failed during handshake.");
                _ircReconnectTimer = ReconnectDelaySec;
                return;
            }
        }
        GD.PrintErr("[TwitchService] IRC connection timed out.");
        _ircReconnectTimer = ReconnectDelaySec;
    }

    private void SendIrc(string message)
    {
        _ircSocket?.SendText(message + "\r\n");
    }

    private void HandleIrcData(string data)
    {
        _ircBuffer.Append(data);
        var full = _ircBuffer.ToString();
        var lines = full.Split(new[] { "\r\n" }, StringSplitOptions.None);

        // Last element might be partial — keep it in the buffer
        _ircBuffer.Clear();
        if (!full.EndsWith("\r\n"))
        {
            _ircBuffer.Append(lines[^1]);
        }

        for (int i = 0; i < lines.Length - (full.EndsWith("\r\n") ? 0 : 1); i++)
        {
            var line = lines[i];
            if (string.IsNullOrEmpty(line)) continue;
            ProcessIrcLine(line);
        }
    }

    private void ProcessIrcLine(string line)
    {
        // PING/PONG keepalive
        if (line.StartsWith("PING"))
        {
            SendIrc("PONG" + line.Substring(4));
            return;
        }

        // PRIVMSG — extract username
        // Format: :username!username@username.tmi.twitch.tv PRIVMSG #channel :message
        if (line.Contains(" PRIVMSG "))
        {
            var username = ExtractIrcUsername(line);
            if (!string.IsNullOrEmpty(username))
            {
                EnqueueChatter(username);
            }
        }
    }

    private string ExtractIrcUsername(string line)
    {
        // :nick!user@host PRIVMSG #channel :message
        if (!line.StartsWith(":")) return null;
        var excl = line.IndexOf('!');
        if (excl < 2) return null;
        return line.Substring(1, excl - 1);
    }

    private void EnqueueChatter(string username)
    {
        lock (_chatterLock)
        {
            // Dedup: if already in queue, skip
            if (_chatterSet.Contains(username)) return;

            // Evict oldest if at capacity
            if (_chatterQueue.Count >= MaxChatterQueue)
            {
                var evicted = _chatterQueue.Dequeue();
                _chatterSet.Remove(evicted);
            }

            _chatterQueue.Enqueue(username);
            _chatterSet.Add(username);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EventSub WebSocket Connection
    // ═══════════════════════════════════════════════════════════════════════
    private void ConnectEventSub()
    {
        if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_broadcasterId)) return;

        _eventSubSocket?.Close();
        _eventSubSocket = new WebSocketPeer();
        var err = _eventSubSocket.ConnectToUrl(EventSubWssUrl);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[TwitchService] EventSub WebSocket connect error: {err}");
            _eventSubReconnectTimer = ReconnectDelaySec;
            return;
        }

        _eventSubConnected = false;
        _eventSubSessionId = null;
        _eventSubBuffer.Clear();
        GD.Print("[TwitchService] Connecting to EventSub...");
    }

    private void HandleEventSubData(string data)
    {
        _eventSubBuffer.Append(data);
        var full = _eventSubBuffer.ToString();

        // EventSub sends complete JSON messages; attempt to parse each one.
        // Messages are delimited by the WebSocket frame boundary, so each packet is one message.
        _eventSubBuffer.Clear();
        ProcessEventSubMessage(full);
    }

    private void ProcessEventSubMessage(string json)
    {
        try
        {
            var messageType = ExtractJsonField(json, "message_type");
            if (string.IsNullOrEmpty(messageType))
            {
                // Might be nested under "metadata"
                messageType = ExtractNestedJsonField(json, "metadata", "message_type");
            }

            switch (messageType)
            {
                case "session_welcome":
                    HandleEventSubWelcome(json);
                    break;
                case "session_keepalive":
                    // No action needed — just confirms connection is alive
                    break;
                case "notification":
                    HandleEventSubNotification(json);
                    break;
                case "session_reconnect":
                    HandleEventSubReconnect(json);
                    break;
                default:
                    // Unknown message type — log for debugging
                    GD.Print($"[TwitchService] EventSub unknown message_type: {messageType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TwitchService] EventSub message parse error: {ex.Message}");
        }
    }

    private void HandleEventSubWelcome(string json)
    {
        // Extract session id from: {"metadata":{...},"payload":{"session":{"id":"..."}}}
        _eventSubSessionId = ExtractNestedJsonField(json, "session", "id");
        _eventSubConnected = true;
        GD.Print($"[TwitchService] EventSub connected. Session: {_eventSubSessionId}");

        // Subscribe to channel point redemptions and create rewards
        Task.Run(async () =>
        {
            try
            {
                await CreateChannelPointRewards().ConfigureAwait(false);
                await SubscribeToRedemptions().ConfigureAwait(false);
                GD.Print("[TwitchService] EventSub subscriptions active.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TwitchService] EventSub setup error: {ex.Message}");
            }
        });
    }

    private void HandleEventSubNotification(string json)
    {
        // Extract subscription type
        var subType = ExtractNestedJsonField(json, "subscription", "type");
        if (subType != "channel.channel_points_custom_reward_redemption.add") return;

        // Extract event data
        // The event payload is under "payload" -> "event"
        var eventBlock = ExtractJsonObject(json, "event");
        if (string.IsNullOrEmpty(eventBlock)) return;

        var username = ExtractJsonField(eventBlock, "user_name");
        var rewardBlock = ExtractJsonObject(eventBlock, "reward");
        var rewardId = rewardBlock != null ? ExtractJsonField(rewardBlock, "id") : null;
        var userInput = ExtractJsonField(eventBlock, "user_input");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(rewardId)) return;

        if (rewardId == _loveConfessionRewardId)
        {
            GD.Print($"[TwitchService] Love Confession redeemed by: {username}");
            EnqueueMainThread(() => OnLoveConfession?.Invoke(username));
        }
        else if (rewardId == _saySomethingRewardId)
        {
            GD.Print($"[TwitchService] Say Something redeemed by: {username} — \"{userInput}\"");
            EnqueueMainThread(() => OnSayMessage?.Invoke(username, userInput ?? ""));
        }
    }

    private void HandleEventSubReconnect(string json)
    {
        var reconnectUrl = ExtractNestedJsonField(json, "session", "reconnect_url");
        GD.Print($"[TwitchService] EventSub reconnect requested: {reconnectUrl}");

        _eventSubSocket?.Close();
        _eventSubSocket = new WebSocketPeer();
        var err = _eventSubSocket.ConnectToUrl(reconnectUrl ?? EventSubWssUrl);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[TwitchService] EventSub reconnect failed: {err}");
            _eventSubReconnectTimer = ReconnectDelaySec;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Channel Point Rewards
    // ═══════════════════════════════════════════════════════════════════════
    private async Task CreateChannelPointRewards()
    {
        // Create "Love Confession" reward
        if (string.IsNullOrEmpty(_loveConfessionRewardId))
        {
            try
            {
                var body = $"{{\"title\":\"Love Confession\",\"cost\":{_loveConfessionCost},\"is_enabled\":true,\"is_user_input_required\":false}}";
                var response = await TwitchApiPost($"/channel_points/custom_rewards?broadcaster_id={_broadcasterId}", body).ConfigureAwait(false);
                _loveConfessionRewardId = ExtractNestedJsonField(response, "data", "id");
                if (string.IsNullOrEmpty(_loveConfessionRewardId))
                {
                    // data is an array — try extracting from first element
                    _loveConfessionRewardId = ExtractFirstArrayItemField(response, "data", "id");
                }
                GD.Print($"[TwitchService] Created 'Love Confession' reward (id: {_loveConfessionRewardId})");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TwitchService] Failed to create 'Love Confession' reward: {ex.Message}");
            }
        }

        // Create "Say Something" reward
        if (string.IsNullOrEmpty(_saySomethingRewardId))
        {
            try
            {
                var body = $"{{\"title\":\"Say Something\",\"cost\":{_saySomethingCost},\"is_enabled\":true,\"is_user_input_required\":true}}";
                var response = await TwitchApiPost($"/channel_points/custom_rewards?broadcaster_id={_broadcasterId}", body).ConfigureAwait(false);
                _saySomethingRewardId = ExtractNestedJsonField(response, "data", "id");
                if (string.IsNullOrEmpty(_saySomethingRewardId))
                {
                    _saySomethingRewardId = ExtractFirstArrayItemField(response, "data", "id");
                }
                GD.Print($"[TwitchService] Created 'Say Something' reward (id: {_saySomethingRewardId})");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TwitchService] Failed to create 'Say Something' reward: {ex.Message}");
            }
        }
    }

    private async Task SubscribeToRedemptions()
    {
        var body = $"{{" +
                   $"\"type\":\"channel.channel_points_custom_reward_redemption.add\"," +
                   $"\"version\":\"1\"," +
                   $"\"condition\":{{\"broadcaster_user_id\":\"{_broadcasterId}\"}}," +
                   $"\"transport\":{{\"method\":\"websocket\",\"session_id\":\"{_eventSubSessionId}\"}}" +
                   $"}}";

        try
        {
            var response = await TwitchApiPost("/eventsub/subscriptions", body).ConfigureAwait(false);
            GD.Print($"[TwitchService] Subscribed to channel point redemptions.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TwitchService] Failed to subscribe to redemptions: {ex.Message}");
        }
    }

    private async Task DeleteChannelPointRewards()
    {
        if (!string.IsNullOrEmpty(_loveConfessionRewardId))
        {
            try
            {
                await TwitchApiDelete($"/channel_points/custom_rewards?broadcaster_id={_broadcasterId}&id={_loveConfessionRewardId}").ConfigureAwait(false);
                GD.Print("[TwitchService] Deleted 'Love Confession' reward.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TwitchService] Failed to delete 'Love Confession' reward: {ex.Message}");
            }
            _loveConfessionRewardId = null;
        }

        if (!string.IsNullOrEmpty(_saySomethingRewardId))
        {
            try
            {
                await TwitchApiDelete($"/channel_points/custom_rewards?broadcaster_id={_broadcasterId}&id={_saySomethingRewardId}").ConfigureAwait(false);
                GD.Print("[TwitchService] Deleted 'Say Something' reward.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TwitchService] Failed to delete 'Say Something' reward: {ex.Message}");
            }
            _saySomethingRewardId = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Disconnect
    // ═══════════════════════════════════════════════════════════════════════
    private void DoDisconnect()
    {
        // Cancel OAuth listener if running
        _oauthCts?.Cancel();

        // Delete auto-created rewards (fire-and-forget on background thread)
        if (!string.IsNullOrEmpty(_accessToken) && (!string.IsNullOrEmpty(_loveConfessionRewardId) || !string.IsNullOrEmpty(_saySomethingRewardId)))
        {
            // Synchronous-ish cleanup: best-effort
            try { DeleteChannelPointRewards().GetAwaiter().GetResult(); }
            catch (Exception ex) { GD.PrintErr($"[TwitchService] Reward cleanup error: {ex.Message}"); }
        }

        // Close IRC
        if (_ircSocket != null)
        {
            try { _ircSocket.Close(); } catch { /* ignore */ }
            _ircSocket = null;
        }
        _ircConnected = false;
        _ircAuthenticated = false;

        // Close EventSub
        if (_eventSubSocket != null)
        {
            try { _eventSubSocket.Close(); } catch { /* ignore */ }
            _eventSubSocket = null;
        }
        _eventSubConnected = false;
        _eventSubSessionId = null;

        // Clear chatter queue
        lock (_chatterLock)
        {
            _chatterQueue.Clear();
            _chatterSet.Clear();
        }

        // Clear token and save
        _accessToken = null;
        _channelName = null;
        _broadcasterId = null;
        SaveSettings();

        GD.Print("[TwitchService] Disconnected.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Settings Persistence (Godot ConfigFile)
    // ═══════════════════════════════════════════════════════════════════════
    private void LoadSettings()
    {
        var config = new ConfigFile();
        var err = config.Load(SettingsPath);
        if (err != Error.Ok) return;

        _accessToken = config.GetValue(SettingsSection, "access_token", "").AsString();
        _channelName = config.GetValue(SettingsSection, "channel_name", "").AsString();
        _broadcasterId = config.GetValue(SettingsSection, "broadcaster_id", "").AsString();
        _loveConfessionCost = config.GetValue(SettingsSection, "love_confession_cost", 500).AsInt32();
        _saySomethingCost = config.GetValue(SettingsSection, "say_something_cost", 200).AsInt32();
        _loveConfessionRewardId = config.GetValue(SettingsSection, "love_confession_reward_id", "").AsString();
        _saySomethingRewardId = config.GetValue(SettingsSection, "say_something_reward_id", "").AsString();

        if (string.IsNullOrEmpty(_accessToken)) _accessToken = null;
        if (string.IsNullOrEmpty(_channelName)) _channelName = null;
        if (string.IsNullOrEmpty(_broadcasterId)) _broadcasterId = null;
        if (string.IsNullOrEmpty(_loveConfessionRewardId)) _loveConfessionRewardId = null;
        if (string.IsNullOrEmpty(_saySomethingRewardId)) _saySomethingRewardId = null;
    }

    private void SaveSettings()
    {
        var config = new ConfigFile();
        config.SetValue(SettingsSection, "access_token", _accessToken ?? "");
        config.SetValue(SettingsSection, "channel_name", _channelName ?? "");
        config.SetValue(SettingsSection, "broadcaster_id", _broadcasterId ?? "");
        config.SetValue(SettingsSection, "love_confession_cost", _loveConfessionCost);
        config.SetValue(SettingsSection, "say_something_cost", _saySomethingCost);
        config.SetValue(SettingsSection, "love_confession_reward_id", _loveConfessionRewardId ?? "");
        config.SetValue(SettingsSection, "say_something_reward_id", _saySomethingRewardId ?? "");
        config.Save(SettingsPath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Thread helpers
    // ═══════════════════════════════════════════════════════════════════════
    private void EnqueueMainThread(Action action)
    {
        lock (_mainThreadLock)
        {
            _mainThreadQueue.Enqueue(action);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Minimal JSON helpers (avoids external dependency)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extracts the first occurrence of "key":"value" from a JSON string.
    /// Only works for simple string values.
    /// </summary>
    private static string ExtractJsonField(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var searchKey = $"\"{key}\":\"";
        var idx = json.IndexOf(searchKey, StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + searchKey.Length;
        var end = json.IndexOf('"', start);
        if (end < 0) return null;
        return json.Substring(start, end - start);
    }

    /// <summary>
    /// Extracts a field from within a named JSON object: "outerKey":{..."innerKey":"value"...}
    /// </summary>
    private static string ExtractNestedJsonField(string json, string outerKey, string innerKey)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var outerSearch = $"\"{outerKey}\"";
        var outerIdx = json.IndexOf(outerSearch, StringComparison.Ordinal);
        if (outerIdx < 0) return null;
        // Find the opening brace of the object
        var braceIdx = json.IndexOf('{', outerIdx + outerSearch.Length);
        if (braceIdx < 0) return null;
        // Find the matching closing brace
        int depth = 1;
        int pos = braceIdx + 1;
        while (pos < json.Length && depth > 0)
        {
            if (json[pos] == '{') depth++;
            else if (json[pos] == '}') depth--;
            pos++;
        }
        var innerJson = json.Substring(braceIdx, pos - braceIdx);
        return ExtractJsonField(innerJson, innerKey);
    }

    /// <summary>
    /// Extracts a JSON object block by key: "key":{...}
    /// Returns the inner JSON including braces.
    /// </summary>
    private static string ExtractJsonObject(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var search = $"\"{key}\"";
        var idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return null;
        var braceIdx = json.IndexOf('{', idx + search.Length);
        if (braceIdx < 0) return null;
        // Ensure there's no unexpected content between the key and the brace (just colon and whitespace)
        var between = json.Substring(idx + search.Length, braceIdx - (idx + search.Length)).Trim();
        if (between != ":") return null;
        int depth = 1;
        int pos = braceIdx + 1;
        while (pos < json.Length && depth > 0)
        {
            if (json[pos] == '{') depth++;
            else if (json[pos] == '}') depth--;
            pos++;
        }
        return json.Substring(braceIdx, pos - braceIdx);
    }

    /// <summary>
    /// For responses like {"data":[{"id":"..."}]} — extracts a field from the first array item.
    /// </summary>
    private static string ExtractFirstArrayItemField(string json, string arrayKey, string fieldKey)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var search = $"\"{arrayKey}\"";
        var idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return null;
        var bracketIdx = json.IndexOf('[', idx + search.Length);
        if (bracketIdx < 0) return null;
        var braceIdx = json.IndexOf('{', bracketIdx + 1);
        if (braceIdx < 0) return null;
        int depth = 1;
        int pos = braceIdx + 1;
        while (pos < json.Length && depth > 0)
        {
            if (json[pos] == '{') depth++;
            else if (json[pos] == '}') depth--;
            pos++;
        }
        var itemJson = json.Substring(braceIdx, pos - braceIdx);
        return ExtractJsonField(itemJson, fieldKey);
    }
}
