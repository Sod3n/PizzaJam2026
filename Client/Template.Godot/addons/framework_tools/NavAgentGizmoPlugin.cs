#if TOOLS
using Godot;

namespace Template.Godot.Framework.Editor;

[Tool]
public partial class NavAgentGizmoPlugin : EditorNode3DGizmoPlugin
{
    private static readonly Color NavColor = new(1.0f, 0.6f, 0.0f);
    private const int CircleSegments = 32;

    private readonly EditorUndoRedoManager _undoRedo;

    public NavAgentGizmoPlugin(EditorUndoRedoManager undoRedo)
    {
        _undoRedo = undoRedo;
        CreateMaterial("nav_shape", NavColor, false, true);
        CreateHandleMaterial("handles");
    }

    public override string _GetGizmoName() => "FrameworkNavAgent";

    public override bool _HasGizmo(Node3D node) => node is FrameworkNavAgent;

    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
        gizmo.Clear();
        var agent = (FrameworkNavAgent)gizmo.GetNode3D();

        var material = GetMaterial("nav_shape", gizmo);
        var handleMaterial = GetMaterial("handles", gizmo);

        var lines = new System.Collections.Generic.List<Vector3>();

        // Draw radius circle slightly above ground to avoid z-fighting with physics gizmo
        for (int i = 0; i < CircleSegments; i++)
        {
            float a1 = i * Mathf.Tau / CircleSegments;
            float a2 = (i + 1) * Mathf.Tau / CircleSegments;
            lines.Add(new Vector3(Mathf.Cos(a1) * agent.Radius, 0.01f, Mathf.Sin(a1) * agent.Radius));
            lines.Add(new Vector3(Mathf.Cos(a2) * agent.Radius, 0.01f, Mathf.Sin(a2) * agent.Radius));
        }

        gizmo.AddLines(lines.ToArray(), material);

        // Radius handle at +X
        gizmo.AddHandles(
            new[] { new Vector3(agent.Radius, 0.01f, 0) },
            handleMaterial,
            new[] { 0 });
    }

    public override string _GetHandleName(EditorNode3DGizmo gizmo, int handleId, bool secondary)
        => "Nav Radius";

    public override Variant _GetHandleValue(EditorNode3DGizmo gizmo, int handleId, bool secondary)
        => ((FrameworkNavAgent)gizmo.GetNode3D()).Radius;

    public override void _SetHandle(EditorNode3DGizmo gizmo, int handleId, bool secondary, Camera3D camera, Vector2 screenPos)
    {
        var agent = (FrameworkNavAgent)gizmo.GetNode3D();
        var gt = agent.GlobalTransform;

        var from = camera.ProjectRayOrigin(screenPos);
        var dir = camera.ProjectRayNormal(screenPos);
        var plane = new Plane(Vector3.Up, gt.Origin.Y);
        var hit = plane.IntersectsRay(from, dir);
        if (hit == null) return;

        var localPos = gt.AffineInverse() * hit.Value;
        agent.Radius = Mathf.Max(0.01f, new Vector2(localPos.X, localPos.Z).Length());
        agent.UpdateGizmos();
    }

    public override void _CommitHandle(EditorNode3DGizmo gizmo, int handleId, bool secondary, Variant restore, bool cancel)
    {
        var agent = (FrameworkNavAgent)gizmo.GetNode3D();

        if (cancel)
        {
            agent.Radius = (float)restore;
            agent.UpdateGizmos();
            return;
        }

        _undoRedo.CreateAction("Resize Nav Agent Radius");
        _undoRedo.AddDoProperty(agent, "Radius", agent.Radius);
        _undoRedo.AddUndoProperty(agent, "Radius", (float)restore);
        _undoRedo.CommitAction();
    }
}
#endif
