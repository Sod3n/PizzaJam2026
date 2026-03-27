#if TOOLS
using Godot;

namespace Template.Godot.Framework.Editor;

/// <summary>
/// Monitors Template.Shared.dll for changes and restarts the editor
/// before Godot's reload_assemblies can crash.
/// </summary>
[Tool]
public partial class ReloadCleaner : Node
{
    private System.DateTime _sharedDllTimestamp;
    private string _sharedDllPath;
    private bool _restartPending;

    public override void _Ready()
    {
        var projectPath = ProjectSettings.GlobalizePath("res://");
        _sharedDllPath = System.IO.Path.Combine(projectPath,
            ".godot/mono/temp/bin/Debug/Template.Shared.dll");
        _sharedDllTimestamp = GetDllTimestamp();
    }

    public override void _Process(double delta)
    {
        if (_restartPending || string.IsNullOrEmpty(_sharedDllPath)) return;

        var current = GetDllTimestamp();
        if (current != _sharedDllTimestamp)
        {
            _restartPending = true;
            GD.Print("Template.Shared.dll changed. Restarting editor to avoid reload crash...");
            // Restart before reload_assemblies gets called from CallQueue
            EditorInterface.Singleton.RestartEditor(true);
        }
    }

    private System.DateTime GetDllTimestamp()
    {
        return System.IO.File.Exists(_sharedDllPath)
            ? System.IO.File.GetLastWriteTimeUtc(_sharedDllPath)
            : System.DateTime.MinValue;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete || what == NotificationExitTree)
        {
            try
            {
                var entityVmType = System.Type.GetType("Template.Godot.Visuals.EntityViewModel, Template.Godot");
                var dict = entityVmType?.GetProperty("EntityViewModels")?.GetValue(null);
                if (dict is System.Collections.IDictionary d) d.Clear();
            }
            catch { /* ignore during reload */ }
        }
    }
}
#endif
