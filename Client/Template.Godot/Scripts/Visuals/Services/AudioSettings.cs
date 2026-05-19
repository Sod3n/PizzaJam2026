using Godot;
using System.Text.Json;

namespace Template.Godot.Visuals;

public static class AudioSettings
{
    private const string SavePath = "user://audio_settings.json";

    public static float MasterVolume { get; set; } = 1f;
    public static float MusicVolume { get; set; } = 1f;
    public static float SfxVolume { get; set; } = 1f;

    public static void Save()
    {
        var data = new SettingsData
        {
            MasterVolume = MasterVolume,
            MusicVolume = MusicVolume,
            SfxVolume = SfxVolume,
        };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file != null) file.StoreString(json);
        else GD.PrintErr($"[AudioSettings] Failed to save: {FileAccess.GetOpenError()}");
    }

    public static void Load()
    {
        if (!FileAccess.FileExists(SavePath)) return;
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (file == null) return;
        try
        {
            var data = JsonSerializer.Deserialize<SettingsData>(file.GetAsText());
            if (data == null) return;
            MasterVolume = Mathf.Clamp(data.MasterVolume, 0f, 1f);
            MusicVolume = Mathf.Clamp(data.MusicVolume, 0f, 1f);
            SfxVolume = Mathf.Clamp(data.SfxVolume, 0f, 1f);
        }
        catch (JsonException ex)
        {
            GD.PrintErr($"[AudioSettings] Failed to parse JSON: {ex.Message}");
        }
    }

    private class SettingsData
    {
        public float MasterVolume { get; set; } = 1f;
        public float MusicVolume { get; set; } = 1f;
        public float SfxVolume { get; set; } = 1f;
    }
}
