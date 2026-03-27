#if TOOLS
using Godot;

namespace Template.Godot.Framework.Editor;

/// <summary>
/// Tool script that clears static references before assembly reload
/// to prevent ALC unload failures.
/// </summary>
[Tool]
public partial class ReloadCleaner : Node
{
    public override void _Notification(int what)
    {
        if (what == NotificationPredelete || what == NotificationExitTree)
        {
            try
            {
                // Clear static singletons that hold cross-assembly references
                var entityVmType = System.Type.GetType("Template.Godot.Visuals.EntityViewModel, Template.Godot");
                var dict = entityVmType?.GetProperty("EntityViewModels")?.GetValue(null);
                if (dict is System.Collections.IDictionary d) d.Clear();
            }
            catch { /* ignore during reload */ }
        }
    }
}
#endif
