using System;
using System.Text.Json;
using Godot;

namespace Template.Godot.Core;

/// <summary>
/// Persistent local player identity. Generated once on first launch, cached forever in
/// <c>user://player_identity.json</c>, and used as the player's UserId in both offline
/// sessions and as the auth token sent to the server during online JoinMatch.
/// </summary>
public static class PlayerIdentity
{
    private const string SavePath = "user://player_identity.json";

    private static Guid? _cached;

    public static Guid LocalId
    {
        get
        {
            if (_cached.HasValue) return _cached.Value;
            _cached = ReadCmdlineOverride() ?? LoadOrCreate();
            return _cached.Value;
        }
    }

    /// <summary>
    /// Reads --player-id=&lt;guid&gt; or --player-id &lt;guid&gt; from the command line so two
    /// clients launched on the same machine (sharing the same user:// dir) can be given
    /// distinct identities for local multiplayer testing. Checks both engine args and
    /// "Run Instances" user args (passed after `--`).
    /// </summary>
    private static Guid? ReadCmdlineOverride()
    {
        const string Flag = "--player-id";
        Guid? Find(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.StartsWith(Flag + "="))
                {
                    if (Guid.TryParse(a.Substring(Flag.Length + 1), out var g))
                        return g;
                }
                else if (a == Flag && i + 1 < args.Length && Guid.TryParse(args[i + 1], out var g))
                {
                    return g;
                }
            }
            return null;
        }

        var hit = Find(OS.GetCmdlineArgs()) ?? Find(OS.GetCmdlineUserArgs());
        if (hit.HasValue)
            GD.Print($"[PlayerIdentity] Using CLI-override ID: {hit.Value}");
        return hit;
    }

    private static Guid LoadOrCreate()
    {
        if (FileAccess.FileExists(SavePath))
        {
            using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                try
                {
                    var data = JsonSerializer.Deserialize<IdentityData>(f.GetAsText());
                    if (data != null && Guid.TryParse(data.PlayerId, out var existing))
                        return existing;
                }
                catch (JsonException ex)
                {
                    GD.PrintErr($"[PlayerIdentity] Failed to parse: {ex.Message}");
                }
            }
        }

        var fresh = Guid.NewGuid();
        Save(fresh);
        GD.Print($"[PlayerIdentity] Generated new persistent ID: {fresh}");
        return fresh;
    }

    private static void Save(Guid id)
    {
        var json = JsonSerializer.Serialize(new IdentityData { PlayerId = id.ToString() });
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (f != null) f.StoreString(json);
        else GD.PrintErr($"[PlayerIdentity] Failed to save: {FileAccess.GetOpenError()}");
    }

    private class IdentityData
    {
        public string PlayerId { get; set; } = "";
    }
}
