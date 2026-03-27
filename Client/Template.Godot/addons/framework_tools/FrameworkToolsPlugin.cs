#if TOOLS
using Godot;

namespace Template.Godot.Framework.Editor;

[Tool]
public partial class FrameworkToolsPlugin : EditorPlugin
{
    private PhysicsBodyGizmoPlugin _physicsGizmo;
    private NavAgentGizmoPlugin _navGizmo;
    private ReloadCleaner _reloadCleaner;

    public override void _EnterTree()
    {
        _physicsGizmo = new PhysicsBodyGizmoPlugin(GetUndoRedo());
        AddNode3DGizmoPlugin(_physicsGizmo);

        _navGizmo = new NavAgentGizmoPlugin(GetUndoRedo());
        AddNode3DGizmoPlugin(_navGizmo);

        _reloadCleaner = new ReloadCleaner();
        AddChild(_reloadCleaner);
    }

    public override void _ExitTree()
    {
        RemoveNode3DGizmoPlugin(_physicsGizmo);
        RemoveNode3DGizmoPlugin(_navGizmo);
    }
}
#endif
