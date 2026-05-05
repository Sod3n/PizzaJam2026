using Godot;
using System.Text.Json;

namespace Template.Godot.Visuals;

/// <summary>
/// Static class that holds all Twitch-related settings and handles
/// persistence to user://twitch_settings.json via Godot FileAccess.
/// </summary>
public static class TwitchSettings
{
    private const string SavePath = "user://twitch_settings.json";

    // Connection
    public static string ChannelName { get; set; } = "";
    public static string AccessToken { get; set; } = "";

    // Toggles
    public static bool NameCowsFromChat { get; set; } = true;
    public static bool EnableRewards { get; set; } = true;

    // Reward costs
    public static int LoveConfessionCost { get; set; } = 500;
    public static int SaySomethingCost { get; set; } = 100;

    // Derived state
    public static bool IsConnected => !string.IsNullOrEmpty(ChannelName) && !string.IsNullOrEmpty(AccessToken);

    public static void Save()
    {
        var data = new SettingsData
        {
            ChannelName = ChannelName,
            AccessToken = AccessToken,
            NameCowsFromChat = NameCowsFromChat,
            EnableRewards = EnableRewards,
            LoveConfessionCost = LoveConfessionCost,
            SaySomethingCost = SaySomethingCost,
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file != null)
        {
            file.StoreString(json);
        }
        else
        {
            GD.PrintErr($"[TwitchSettings] Failed to save: {FileAccess.GetOpenError()}");
        }
    }

    public static void Load()
    {
        if (!FileAccess.FileExists(SavePath))
            return;

        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"[TwitchSettings] Failed to load: {FileAccess.GetOpenError()}");
            return;
        }

        var json = file.GetAsText();
        try
        {
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data == null) return;

            ChannelName = data.ChannelName ?? "";
            AccessToken = data.AccessToken ?? "";
            NameCowsFromChat = data.NameCowsFromChat;
            EnableRewards = data.EnableRewards;
            LoveConfessionCost = data.LoveConfessionCost;
            SaySomethingCost = data.SaySomethingCost;
        }
        catch (JsonException ex)
        {
            GD.PrintErr($"[TwitchSettings] Failed to parse JSON: {ex.Message}");
        }
    }

    private class SettingsData
    {
        public string ChannelName { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public bool NameCowsFromChat { get; set; } = true;
        public bool EnableRewards { get; set; } = true;
        public int LoveConfessionCost { get; set; } = 500;
        public int SaySomethingCost { get; set; } = 100;
    }
}
