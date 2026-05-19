using Godot;
using Template.Godot.Visuals;

namespace Template.Godot.Audio;

// Thin C# bridge to the FmodServer GDExtension singleton. Every call is gated by a
// get_event(path) lookup — the plugin's play_one_shot / create_event_instance hard-crash
// the editor when the path isn't authored in the loaded banks (observed on macOS).
public static class FmodAudio
{
    private static GodotObject _server;
    private static bool _lookedUp;

    private static GodotObject Server
    {
        get
        {
            if (_lookedUp) return _server;
            _lookedUp = true;
            if (Engine.HasSingleton("FmodServer"))
                _server = Engine.GetSingleton("FmodServer");
            return _server;
        }
    }

    private static bool EventExists(GodotObject server, string eventPath)
    {
        try
        {
            var desc = server.Call("get_event", eventPath).As<GodotObject>();
            return desc != null;
        }
        catch { return false; }
    }

    // Standard FMOD bus paths. "bus:/" is the master bus, present in every project.
    // Music/SFX buses must be authored in the FMOD project for those sliders to do anything.
    public const string MasterBus = "bus:/";
    public const string MusicBus = "bus:/Music";
    public const string SfxBus = "bus:/SFX";

    public static void SetBusVolume(string busPath, float volume)
    {
        var s = Server;
        if (s == null) { GD.Print($"[FmodAudio] SetBusVolume('{busPath}'): no server"); return; }
        if (string.IsNullOrEmpty(busPath)) return;
        try
        {
            var bus = s.Call("get_bus", busPath).As<GodotObject>();
            if (bus == null)
            {
                GD.PushWarning($"[FmodAudio] bus not found: '{busPath}'");
                return;
            }
            float v = Mathf.Clamp(volume, 0f, 1f);
            bus.Call("set_volume", v);
            GD.Print($"[FmodAudio] set '{busPath}' volume = {v:F2}");
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[FmodAudio] set_bus_volume '{busPath}' failed: {e.Message}");
        }
    }

    public static void ApplyVolumesFromSettings()
    {
        SetBusVolume(MasterBus, AudioSettings.MasterVolume);
        SetBusVolume(MusicBus, AudioSettings.MusicVolume);
        SetBusVolume(SfxBus, AudioSettings.SfxVolume);
    }

    public static void PlayOneShot(string eventPath)
    {
        var s = Server;
        if (s == null || string.IsNullOrEmpty(eventPath)) return;
        if (!EventExists(s, eventPath)) return;
        try { s.Call("play_one_shot", eventPath); }
        catch (System.Exception e) { GD.PushWarning($"[FmodAudio] play_one_shot '{eventPath}' failed: {e.Message}"); }
    }

    // Fire-and-forget with labeled (string) parameters. Skips empty labels so FMOD
    // doesn't reject the call when the upstream param was unset.
    public static void PlayOneShotWithLabels(string eventPath, params (string name, string label)[] labels)
    {
        var s = Server;
        if (s == null || string.IsNullOrEmpty(eventPath)) return;
        if (!EventExists(s, eventPath)) return;
        try
        {
            var inst = s.Call("create_event_instance", eventPath).As<GodotObject>();
            if (inst == null) return;
            if (labels != null)
            {
                foreach (var (name, label) in labels)
                {
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(label)) continue;
                    inst.Call("set_parameter_by_name_with_label", name, label, false);
                }
            }
            inst.Call("start");
            inst.Call("release");
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[FmodAudio] event '{eventPath}' failed: {e.Message}");
        }
    }
}
