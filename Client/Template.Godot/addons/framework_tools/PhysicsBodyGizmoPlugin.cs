#if TOOLS
using Godot;

namespace Template.Godot.Framework.Editor;

[Tool]
public partial class PhysicsBodyGizmoPlugin : EditorNode3DGizmoPlugin
{
    private static readonly Color CharacterColor = new(0.2f, 0.8f, 0.2f);
    private static readonly Color StaticColor = new(0.3f, 0.5f, 1.0f);
    private static readonly Color Area2DColor = new(0.8f, 0.2f, 0.8f);
    private const int CircleSegments = 32;
    private const float FillAlpha = 0.15f;

    private readonly EditorUndoRedoManager _undoRedo;

    public PhysicsBodyGizmoPlugin(EditorUndoRedoManager undoRedo)
    {
        _undoRedo = undoRedo;
        // Wireframe outlines
        CreateMaterial("shape_character", CharacterColor, false, true);
        CreateMaterial("shape_static", StaticColor, false, true);
        CreateMaterial("shape_area2d", Area2DColor, false, true);
        CreateHandleMaterial("handles");
    }

    public override string _GetGizmoName() => "FrameworkPhysicsBody";

    public override bool _HasGizmo(Node3D node) => node is FrameworkPhysicsBody;

    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
        gizmo.Clear();
        var body = (FrameworkPhysicsBody)gizmo.GetNode3D();

        var materialName = body.BodyType switch
        {
            FrameworkPhysicsBodyType.CharacterBody2D => "shape_character",
            FrameworkPhysicsBodyType.Area2D => "shape_area2d",
            _ => "shape_static"
        };
        var color = body.BodyType switch
        {
            FrameworkPhysicsBodyType.CharacterBody2D => CharacterColor,
            FrameworkPhysicsBodyType.Area2D => Area2DColor,
            _ => StaticColor
        };
        var material = GetMaterial(materialName, gizmo);
        var handleMaterial = GetMaterial("handles", gizmo);

        var lines = new System.Collections.Generic.List<Vector3>();

        if (body.ShapeType == FrameworkCollisionShapeType.Circle)
        {
            for (int i = 0; i < CircleSegments; i++)
            {
                float a1 = i * Mathf.Tau / CircleSegments;
                float a2 = (i + 1) * Mathf.Tau / CircleSegments;
                lines.Add(new Vector3(Mathf.Cos(a1) * body.ShapeRadius, 0, Mathf.Sin(a1) * body.ShapeRadius));
                lines.Add(new Vector3(Mathf.Cos(a2) * body.ShapeRadius, 0, Mathf.Sin(a2) * body.ShapeRadius));
            }

            gizmo.AddHandles(
                new[] { new Vector3(body.ShapeRadius, 0, 0) },
                handleMaterial,
                new[] { 0 });

            // Filled circle
            gizmo.AddMesh(CreateCircleMesh(body.ShapeRadius, color));
        }
        else
        {
            float hw = body.ShapeWidth / 2f;
            float hh = body.ShapeHeight / 2f;

            lines.Add(new Vector3(-hw, 0, -hh)); lines.Add(new Vector3(hw, 0, -hh));
            lines.Add(new Vector3(hw, 0, -hh));  lines.Add(new Vector3(hw, 0, hh));
            lines.Add(new Vector3(hw, 0, hh));   lines.Add(new Vector3(-hw, 0, hh));
            lines.Add(new Vector3(-hw, 0, hh));  lines.Add(new Vector3(-hw, 0, -hh));

            gizmo.AddHandles(
                new[] { new Vector3(hw, 0, 0), new Vector3(0, 0, hh) },
                handleMaterial,
                new[] { 0, 1 });

            // Filled rectangle
            gizmo.AddMesh(CreateRectMesh(hw, hh, color));
        }

        gizmo.AddLines(lines.ToArray(), material);
    }

    private static ArrayMesh CreateCircleMesh(float radius, Color color)
    {
        var mesh = new ArrayMesh();
        var verts = new Vector3[CircleSegments * 3];
        var colors = new Color[CircleSegments * 3];
        var fillColor = new Color(color, FillAlpha);

        for (int i = 0; i < CircleSegments; i++)
        {
            float a1 = i * Mathf.Tau / CircleSegments;
            float a2 = (i + 1) * Mathf.Tau / CircleSegments;
            int idx = i * 3;
            verts[idx] = Vector3.Zero;
            verts[idx + 1] = new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
            verts[idx + 2] = new Vector3(Mathf.Cos(a2) * radius, 0, Mathf.Sin(a2) * radius);
            colors[idx] = fillColor;
            colors[idx + 1] = fillColor;
            colors[idx + 2] = fillColor;
        }

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var mat = new StandardMaterial3D();
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.AlbedoColor = fillColor;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.NoDepthTest = true;
        mesh.SurfaceSetMaterial(0, mat);

        return mesh;
    }

    private static ArrayMesh CreateRectMesh(float hw, float hh, Color color)
    {
        var mesh = new ArrayMesh();
        var fillColor = new Color(color, FillAlpha);
        var verts = new Vector3[]
        {
            new(-hw, 0, -hh), new(hw, 0, -hh), new(hw, 0, hh),
            new(-hw, 0, -hh), new(hw, 0, hh), new(-hw, 0, hh),
        };
        var colors = new Color[] { fillColor, fillColor, fillColor, fillColor, fillColor, fillColor };

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var mat = new StandardMaterial3D();
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.AlbedoColor = fillColor;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.NoDepthTest = true;
        mesh.SurfaceSetMaterial(0, mat);

        return mesh;
    }

    public override string _GetHandleName(EditorNode3DGizmo gizmo, int handleId, bool secondary)
    {
        var body = (FrameworkPhysicsBody)gizmo.GetNode3D();
        if (body.ShapeType == FrameworkCollisionShapeType.Circle)
            return "Radius";
        return handleId == 0 ? "Width" : "Height";
    }

    public override Variant _GetHandleValue(EditorNode3DGizmo gizmo, int handleId, bool secondary)
    {
        var body = (FrameworkPhysicsBody)gizmo.GetNode3D();
        if (body.ShapeType == FrameworkCollisionShapeType.Circle)
            return body.ShapeRadius;
        return handleId == 0 ? body.ShapeWidth : body.ShapeHeight;
    }

    public override void _SetHandle(EditorNode3DGizmo gizmo, int handleId, bool secondary, Camera3D camera, Vector2 screenPos)
    {
        var body = (FrameworkPhysicsBody)gizmo.GetNode3D();
        var gt = body.GlobalTransform;

        var from = camera.ProjectRayOrigin(screenPos);
        var dir = camera.ProjectRayNormal(screenPos);
        var plane = new Plane(Vector3.Up, gt.Origin.Y);
        var hit = plane.IntersectsRay(from, dir);
        if (hit == null) return;

        var localPos = gt.AffineInverse() * hit.Value;

        if (body.ShapeType == FrameworkCollisionShapeType.Circle)
        {
            body.ShapeRadius = Mathf.Max(0.01f, new Vector2(localPos.X, localPos.Z).Length());
        }
        else
        {
            if (handleId == 0)
                body.ShapeWidth = Mathf.Max(0.01f, Mathf.Abs(localPos.X) * 2f);
            else
                body.ShapeHeight = Mathf.Max(0.01f, Mathf.Abs(localPos.Z) * 2f);
        }

        body.UpdateGizmos();
    }

    public override void _CommitHandle(EditorNode3DGizmo gizmo, int handleId, bool secondary, Variant restore, bool cancel)
    {
        var body = (FrameworkPhysicsBody)gizmo.GetNode3D();

        if (body.ShapeType == FrameworkCollisionShapeType.Circle)
        {
            if (cancel)
            {
                body.ShapeRadius = (float)restore;
                body.UpdateGizmos();
                return;
            }

            _undoRedo.CreateAction("Resize Physics Radius");
            _undoRedo.AddDoProperty(body, "ShapeRadius", body.ShapeRadius);
            _undoRedo.AddUndoProperty(body, "ShapeRadius", (float)restore);
            _undoRedo.CommitAction();
        }
        else
        {
            var propName = handleId == 0 ? "ShapeWidth" : "ShapeHeight";
            var currentValue = handleId == 0 ? body.ShapeWidth : body.ShapeHeight;

            if (cancel)
            {
                if (handleId == 0) body.ShapeWidth = (float)restore;
                else body.ShapeHeight = (float)restore;
                body.UpdateGizmos();
                return;
            }

            _undoRedo.CreateAction($"Resize Physics {(handleId == 0 ? "Width" : "Height")}");
            _undoRedo.AddDoProperty(body, propName, currentValue);
            _undoRedo.AddUndoProperty(body, propName, (float)restore);
            _undoRedo.CommitAction();
        }
    }
}
#endif
