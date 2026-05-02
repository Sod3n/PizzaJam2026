using System;
using Godot;
using Template.Godot.Core;

namespace Template.Godot.Visuals;

/// <summary>
/// Full-screen settings overlay accessible via ESC key.
/// Contains Twitch integration settings and a placeholder Game section.
/// Blocks game input while visible. Loads its layout from SettingsOverlay.tscn.
/// </summary>
public partial class SettingsOverlay : CanvasLayer
{
    private static SettingsOverlay _current;

    private static readonly PackedScene _scene =
        GD.Load<PackedScene>("res://Scenes/SettingsOverlay.tscn");

    /// <summary>True while the settings overlay is on screen. Used to block game input.</summary>
    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    // UI references populated from the scene tree
    private Control _root;
    private Button _connectButton;
    private Label _statusLabel;
    private CheckButton _nameCowsToggle;
    private CheckButton _enableRewardsToggle;
    private HSlider _loveConfessionSlider;
    private Label _loveConfessionValueLabel;
    private HSlider _saySomethingSlider;
    private Label _saySomethingValueLabel;

    // Lobby UI
    private Label _lobbyCodeLabel;
    private Button _lobbyCopyButton;
    private Label _lobbyStatusLabel;
    private Button _lobbyCreateButton;
    private Button _lobbyEndSessionButton;
    private Guid _lastSeenLobbyId;
    private string _lastSeenStatus;
    private Action<string> _statusHandler;
    private Action<Guid> _lobbyCreatedHandler;

    // Colors for connection status
    private static readonly Color ConnectedColor = new(0.4f, 0.9f, 0.4f);
    private static readonly Color DisconnectedColor = new(0.7f, 0.7f, 0.7f);

    // ── Static API ─────────────────────────────────────────────────────

    public static void Toggle(SceneTree tree)
    {
        if (IsActive)
        {
            _current._Dismiss();
            return;
        }
        Show(tree);
    }

    public static void Show(SceneTree tree)
    {
        if (IsActive) return;

        TwitchSettings.Load();

        var overlay = _scene.Instantiate<SettingsOverlay>();
        _current = overlay;
        tree.Root.AddChild(overlay);
    }

    // ── Ready ───────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _root = GetNode<Control>("%Root");

        // Connection row
        _statusLabel = GetNode<Label>("%StatusLabel");
        _connectButton = GetNode<Button>("%ConnectButton");
        _connectButton.Pressed += _OnConnectPressed;

        // Toggles
        _nameCowsToggle = GetNode<CheckButton>("%NameCowsToggle");
        _nameCowsToggle.ButtonPressed = TwitchSettings.NameCowsFromChat;
        _nameCowsToggle.Toggled += on =>
        {
            TwitchSettings.NameCowsFromChat = on;
            TwitchSettings.Save();
        };

        _enableRewardsToggle = GetNode<CheckButton>("%EnableRewardsToggle");
        _enableRewardsToggle.ButtonPressed = TwitchSettings.EnableRewards;
        _enableRewardsToggle.Toggled += on =>
        {
            TwitchSettings.EnableRewards = on;
            TwitchSettings.Save();
        };

        // Love Confession slider
        _loveConfessionValueLabel = GetNode<Label>("%LoveValueLabel");
        _loveConfessionSlider = GetNode<HSlider>("%LoveSlider");
        _loveConfessionSlider.Value = TwitchSettings.LoveConfessionCost;
        _loveConfessionValueLabel.Text = TwitchSettings.LoveConfessionCost.ToString();
        _loveConfessionSlider.ValueChanged += val =>
        {
            int rounded = RoundToStep((int)val, 50);
            _loveConfessionSlider.SetValueNoSignal(rounded);
            TwitchSettings.LoveConfessionCost = rounded;
            _loveConfessionValueLabel.Text = rounded.ToString();
            TwitchSettings.Save();
        };

        // Say Something slider
        _saySomethingValueLabel = GetNode<Label>("%SayValueLabel");
        _saySomethingSlider = GetNode<HSlider>("%SaySlider");
        _saySomethingSlider.Value = TwitchSettings.SaySomethingCost;
        _saySomethingValueLabel.Text = TwitchSettings.SaySomethingCost.ToString();
        _saySomethingSlider.ValueChanged += val =>
        {
            int rounded = RoundToStep((int)val, 25);
            _saySomethingSlider.SetValueNoSignal(rounded);
            TwitchSettings.SaySomethingCost = rounded;
            _saySomethingValueLabel.Text = rounded.ToString();
            TwitchSettings.Save();
        };

        // Lobby section
        _lobbyCodeLabel = GetNode<Label>("%CodeLabel");
        _lobbyCopyButton = GetNode<Button>("%CopyButton");
        _lobbyStatusLabel = GetNode<Label>("%LobbyStatusLabel");
        _lobbyCreateButton = GetNode<Button>("%CreateButton");
        _lobbyEndSessionButton = GetNode<Button>("%EndSessionButton");

        _lobbyCopyButton.Pressed += _OnCopyLobbyPressed;
        _lobbyCreateButton.Pressed += _OnCreateLobbyPressed;
        _lobbyEndSessionButton.Pressed += _OnEndSessionPressed;

        var gm = GameManager.Instance;
        if (gm != null)
        {
            // Callable.From bypasses Godot's name-based dispatch (which silently fails on private methods).
            _statusHandler = (status) => Callable.From(() => _OnLobbyStatusDeferred(status)).CallDeferred();
            _lobbyCreatedHandler = (_) => Callable.From(_RefreshLobbyUI).CallDeferred();
            gm.OnStatusChanged += _statusHandler;
            gm.OnLobbyCreated += _lobbyCreatedHandler;
        }
        _RefreshLobbyUI();

        // Set initial connection UI state
        _UpdateConnectionUI();

        // Fade in
        _root.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(_root, "modulate:a", 1f, 0.2f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static int RoundToStep(int value, int step)
    {
        return ((value + step / 2) / step) * step;
    }

    // ── Connection Logic ───────────────────────────────────────────────

    private void _UpdateConnectionUI()
    {
        if (TwitchSettings.IsConnected)
        {
            _statusLabel.Text = $"Connected as {TwitchSettings.ChannelName}";
            _statusLabel.AddThemeColorOverride("font_color", ConnectedColor);
            _connectButton.Text = "Disconnect";
        }
        else
        {
            _statusLabel.Text = "Not connected";
            _statusLabel.AddThemeColorOverride("font_color", DisconnectedColor);
            _connectButton.Text = "Connect Twitch";
        }
    }

    private void _OnConnectPressed()
    {
        if (TwitchSettings.IsConnected)
        {
            TwitchService.Disconnect();
        }
        else
        {
            TwitchService.Connect();
        }
        _UpdateConnectionUI();
    }

    // ── Lobby Logic ─────────────────────────────────────────────────────

    private void _RefreshLobbyUI()
    {
        var gm = GameManager.Instance;
        if (gm == null)
        {
            _lobbyCodeLabel.Text = "Offline";
            _lobbyCodeLabel.AddThemeColorOverride("font_color", DisconnectedColor);
            _lobbyCopyButton.Disabled = true;
            _lobbyCreateButton.Disabled = true;
            _lobbyCreateButton.Visible = true;
            _lobbyEndSessionButton.Visible = false;
            return;
        }

        _lastSeenLobbyId = gm.CurrentLobbyId;
        bool hasLobby = gm.CurrentLobbyId != Guid.Empty;
        bool offline = gm.OfflineMode;

        if (hasLobby)
        {
            _lobbyCodeLabel.Text = gm.CurrentLobbyId.ToString();
            _lobbyCodeLabel.AddThemeColorOverride("font_color", ConnectedColor);
            _lobbyCopyButton.Disabled = false;
        }
        else
        {
            _lobbyCodeLabel.Text = offline ? "Offline" : "Not connected";
            _lobbyCodeLabel.AddThemeColorOverride("font_color", DisconnectedColor);
            _lobbyCopyButton.Disabled = true;
        }

        bool offlineNoLobby = offline && !hasLobby;
        bool onlineWithLobby = !offline && hasLobby;
        bool sessionRunning = gm.IsGameRunning;
        _lobbyCreateButton.Visible = !onlineWithLobby;
        _lobbyCreateButton.Disabled = false;
        _lobbyCreateButton.Text = offlineNoLobby ? "Publish to New Lobby" : "Create New Lobby";
        // Show End Session whenever a session (offline or online) is running so the user
        // can always escape back to the LobbyMenu.
        _lobbyEndSessionButton.Visible = sessionRunning;
        _lobbyEndSessionButton.Disabled = false;
    }

    private void _OnLobbyStatusDeferred(string status)
    {
        _lastSeenStatus = status;
        if (_lobbyStatusLabel != null && IsInsideTree())
            _lobbyStatusLabel.Text = status;
        _RefreshLobbyUI();
    }

    private void _OnCopyLobbyPressed()
    {
        var gm = GameManager.Instance;
        var lobbyId = gm?.CurrentLobbyId ?? Guid.Empty;
        var labelText = _lobbyCodeLabel?.Text ?? "";
        // Prefer the label text — works in offline mode too, and matches what the user sees.
        string toCopy = lobbyId != Guid.Empty ? lobbyId.ToString() : labelText;
        GD.Print($"[SettingsOverlay] Copy pressed. lobbyId={lobbyId} label='{labelText}' toCopy='{toCopy}'");
        if (string.IsNullOrEmpty(toCopy) || toCopy == "Offline" || toCopy == "Not connected") return;

        DisplayServer.ClipboardSet(toCopy);
        _lobbyCopyButton.Text = "Copied!";
        GetTree().CreateTimer(1.5).Timeout += () =>
        {
            if (Node.IsInstanceValid(_lobbyCopyButton))
                _lobbyCopyButton.Text = "Copy";
        };
    }

    private void _OnCreateLobbyPressed()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        _lobbyCreateButton.Disabled = true;
        if (gm.OfflineMode)
        {
            _lobbyStatusLabel.Text = "Publishing offline world to new lobby...";
            _ = gm.PublishOfflineToLobby("Game Lobby");
        }
        else
        {
            _lobbyStatusLabel.Text = "Creating lobby...";
            _ = gm.CreateLobby("Game Lobby");
        }
    }

    private void _OnEndSessionPressed()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        _lobbyEndSessionButton.Disabled = true;
        _lobbyStatusLabel.Text = "Ending session...";
        gm.EndSessionAndReturnToLobbyMenu();
        _Dismiss();
    }

    // ── Input ──────────────────────────────────────────────────────────

    // _UnhandledInput runs AFTER _gui_input, so button clicks reach their handlers
    // first; we only swallow leftovers (gameplay keys like WASD) so they don't fire
    // while the overlay is open.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode == Key.Escape)
        {
            _Dismiss();
            GetViewport().SetInputAsHandled();
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    // ── Dismiss ────────────────────────────────────────────────────────

    private void _Dismiss()
    {
        if (!IsInsideTree()) return;

        var gm = GameManager.Instance;
        if (gm != null)
        {
            if (_statusHandler != null) gm.OnStatusChanged -= _statusHandler;
            if (_lobbyCreatedHandler != null) gm.OnLobbyCreated -= _lobbyCreatedHandler;
        }

        var tween = CreateTween();
        tween.TweenProperty(_root, "modulate:a", 0f, 0.15f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            _current = null;
            QueueFree();
        }));
    }
}
