#if TOOLS
using Godot;

namespace Template.Godot.Framework.Editor;

/// <summary>
/// Monitors the shared game DLL for changes and restarts the editor
/// before Godot's reload_assemblies can crash.
/// Configure the DLL name via Project Settings → Framework Tools → Shared Dll Name.
/// </summary>
[Tool]
public partial class ReloadCleaner : Node
{
    private const string DllSetting = "framework_tools/shared_dll_name";
    private const string DefaultDllName = "Template.Shared.dll";
    private const string VmTypeSetting = "framework_tools/entity_viewmodel_type";

    private System.DateTime _sharedDllTimestamp;
    private string _sharedDllPath;
    private bool _restartPending;

    public override void _Ready()
    {
        var dllName = ProjectSettings.HasSetting(DllSetting)
            ? (string)ProjectSettings.GetSetting(DllSetting)
            : DefaultDllName;

        var projectPath = ProjectSettings.GlobalizePath("res://");
        _sharedDllPath = System.IO.Path.Combine(projectPath,
            $".godot/mono/temp/bin/Debug/{dllName}");
        _sharedDllTimestamp = GetDllTimestamp();
    }

    public override void _Process(double delta)
    {
        if (_restartPending || string.IsNullOrEmpty(_sharedDllPath)) return;

        var current = GetDllTimestamp();
        if (current != _sharedDllTimestamp)
        {
            _restartPending = true;
            GD.Print($"{System.IO.Path.GetFileName(_sharedDllPath)} changed. Restarting editor to avoid reload crash...");
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
                var vmTypeStr = ProjectSettings.HasSetting(VmTypeSetting)
                    ? (string)ProjectSettings.GetSetting(VmTypeSetting)
                    : "Template.Godot.Visuals.EntityViewModel, Template.Godot";
                var entityVmType = System.Type.GetType(vmTypeStr);
                var dict = entityVmType?.GetProperty("EntityViewModels")?.GetValue(null);
                if (dict is System.Collections.IDictionary d) d.Clear();
            }
            catch { /* ignore during reload */ }
        }
    }
}
#endif
