using System;
using System.Collections.Generic;
using Godot;

namespace Template.Godot.Visuals;

/// <summary>
/// Twitch integration service. Manages chatter name queue and channel point reward events.
/// Connect/Disconnect networking will be implemented by another agent.
/// Game integration is handled by TwitchIntegration.
/// </summary>
public static class TwitchService
{
    // ---- Chatter name queue ----
    private static readonly Queue<string> _chatterNameQueue = new();
    private static readonly HashSet<string> _usedChatterNames = new();

    /// <summary>
    /// Enqueue a chatter name (e.g., from chat messages). Duplicates are ignored.
    /// </summary>
    public static void EnqueueChatterName(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        // Truncate to 32 chars (FixedString32 limit) and normalize
        if (username.Length > 32) username = username[..32];
        if (_usedChatterNames.Contains(username)) return;
        _chatterNameQueue.Enqueue(username);
        _usedChatterNames.Add(username);
    }

    /// <summary>
    /// Dequeue the next chatter name for cow naming. Returns null if none available.
    /// </summary>
    public static string GetNextChatterName()
    {
        if (!TwitchSettings.NameCowsFromChat) return null;
        return _chatterNameQueue.Count > 0 ? _chatterNameQueue.Dequeue() : null;
    }

    /// <summary>Number of chatter names waiting to be assigned to cows.</summary>
    public static int ChatterNameCount => _chatterNameQueue.Count;

    // ---- Channel point reward events ----

    /// <summary>
    /// Fired when a viewer redeems the "Love Confession" channel point reward.
    /// Parameter is the viewer's username.
    /// </summary>
    public static event Action<string> OnLoveConfession;

    /// <summary>
    /// Fired when a viewer redeems the "Say Something" channel point reward.
    /// Parameters: (username, message).
    /// </summary>
    public static event Action<string, string> OnSayMessage;

    /// <summary>
    /// Call from the Twitch IRC/API handler when a love confession reward is redeemed.
    /// </summary>
    public static void TriggerLoveConfession(string username)
    {
        if (!TwitchSettings.EnableRewards) return;
        GD.Print($"[TwitchService] Love confession from: {username}");
        OnLoveConfession?.Invoke(username);
    }

    /// <summary>
    /// Call from the Twitch IRC/API handler when a say-something reward is redeemed.
    /// </summary>
    public static void TriggerSayMessage(string username, string message)
    {
        if (!TwitchSettings.EnableRewards) return;
        GD.Print($"[TwitchService] Say message from {username}: {message}");
        OnSayMessage?.Invoke(username, message);
    }

    // ---- Connection ----

    public static void Connect()
    {
        GD.Print("[TwitchService] Connect() called -- not yet implemented");
    }

    public static void Disconnect()
    {
        GD.Print("[TwitchService] Disconnect() called");
        TwitchSettings.ChannelName = "";
        TwitchSettings.AccessToken = "";
        TwitchSettings.Save();
        ClearState();
    }

    /// <summary>Reset all queues and used-name tracking (e.g., on disconnect or new game).</summary>
    public static void ClearState()
    {
        _chatterNameQueue.Clear();
        _usedChatterNames.Clear();
    }
}
