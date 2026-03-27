using Godot;
using System;
using System.Collections.Generic;
using Template.Godot.Core;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Navigation2D.Systems;
using Deterministic.GameFramework.ECS;

using DTransform2D = Deterministic.GameFramework.TwoD.Transform2D;
using DCollisionShape2D = Deterministic.GameFramework.Physics2D.Components.CollisionShape2D;
using DCollisionShapeType = Deterministic.GameFramework.Physics2D.Components.CollisionShapeType;
using DArea2D = Deterministic.GameFramework.Physics2D.Components.Area2D;
using DNavAgent = Deterministic.GameFramework.Navigation2D.Components.NavigationAgent2D;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Debug;

/// <summary>
/// Debug overlay that can visualize collisions, navigation mesh, and agent paths.
/// Toggle each layer independently with F1/F2/F3 keys.
/// </summary>
public partial class DebugVisualizer : Node3D
{
    private bool _showCollisions;
    private bool _showNavMesh;
    private bool _showAgentPaths;

    private MeshInstance3D _collisionMesh;
    private MeshInstance3D _navMeshInstance;
    private MeshInstance3D _agentPathsMesh;

    private const float DrawHeight = 0.05f;

    public override void _Ready()
    {
        RegisterAction("debug_collisions", Key.F1);
        RegisterAction("debug_navmesh", Key.F2);
        RegisterAction("debug_agent_paths", Key.F3);

        _collisionMesh = new MeshInstance3D { Name = "DebugCollisions" };
        _navMeshInstance = new MeshInstance3D { Name = "DebugNavMesh" };
        _agentPathsMesh = new MeshInstance3D { Name = "DebugAgentPaths" };

        AddChild(_collisionMesh);
        AddChild(_navMeshInstance);
        AddChild(_agentPathsMesh);
    }

    private static void RegisterAction(string name, Key key)
    {
        if (InputMap.HasAction(name)) return;
        InputMap.AddAction(name);
        var ev = new InputEventKey { Keycode = key };
        InputMap.ActionAddEvent(name, ev);
    }

    public override void _Process(double delta)
    {
        if (global::Godot.Input.IsActionJustPressed("debug_collisions"))
        {
            _showCollisions = !_showCollisions;
            GD.Print($"[Debug] Collisions: {(_showCollisions ? "ON" : "OFF")}");
        }
        if (global::Godot.Input.IsActionJustPressed("debug_navmesh"))
        {
            _showNavMesh = !_showNavMesh;
            GD.Print($"[Debug] NavMesh: {(_showNavMesh ? "ON" : "OFF")}");
        }
        if (global::Godot.Input.IsActionJustPressed("debug_agent_paths"))
        {
            _showAgentPaths = !_showAgentPaths;
            GD.Print($"[Debug] Agent Paths: {(_showAgentPaths ? "ON" : "OFF")}");
        }

        _collisionMesh.Visible = _showCollisions;
        _navMeshInstance.Visible = _showNavMesh;
        _agentPathsMesh.Visible = _showAgentPaths;

        if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;

        if (_showCollisions) DrawCollisions();
        if (_showNavMesh) DrawNavMesh();
        if (_showAgentPaths) DrawAgentPaths();
    }

    private void DrawCollisions()
    {
        var state = GameManager.Instance.Game.State;
        var im = new ImmediateMesh();

        // Filled shapes
        im.SurfaceBegin(Mesh.PrimitiveType.Triangles);

        foreach (var entity in state.Filter<DCollisionShape2D, DTransform2D>())
        {
            ref var shape = ref state.GetComponent<DCollisionShape2D>(entity);
            ref var transform = ref state.GetComponent<DTransform2D>(entity);
            if (shape.Disabled) continue;

            var pos = transform.GlobalPosition;
            var cx = (float)pos.X + (float)shape.Position.X;
            var cz = (float)pos.Y + (float)shape.Position.Y;

            bool isArea = state.HasComponent<DArea2D>(entity);
            if (!isArea && transform.Parent.Id > 0 && state.HasComponent<DArea2D>(transform.Parent))
                isArea = true;

            var fill = isArea ? new Color(0f, 1f, 1f, 0.12f) : new Color(0.2f, 1f, 0.2f, 0.12f);

            if (shape.Type == DCollisionShapeType.Circle)
            {
                FillCircle(im, cx, cz, (float)shape.Circle.Radius, fill);
            }
            else if (shape.Type == DCollisionShapeType.Rectangle)
            {
                var hw = (float)shape.Rectangle.Size.X * 0.5f;
                var hh = (float)shape.Rectangle.Size.Y * 0.5f;
                FillRect(im, cx, cz, hw, hh, fill);
            }
            else if (shape.Type == DCollisionShapeType.Capsule)
            {
                FillCircle(im, cx, cz, (float)shape.Capsule.Radius, fill);
            }
        }

        im.SurfaceEnd();
        im.SurfaceSetMaterial(0, CreateCollisionFillMaterial());

        // Wireframe outlines
        im.SurfaceBegin(Mesh.PrimitiveType.Lines);

        foreach (var entity in state.Filter<DCollisionShape2D, DTransform2D>())
        {
            ref var shape = ref state.GetComponent<DCollisionShape2D>(entity);
            ref var transform = ref state.GetComponent<DTransform2D>(entity);
            if (shape.Disabled) continue;

            var pos = transform.GlobalPosition;
            var cx = (float)pos.X + (float)shape.Position.X;
            var cz = (float)pos.Y + (float)shape.Position.Y;

            bool isArea = state.HasComponent<DArea2D>(entity);
            if (!isArea && transform.Parent.Id > 0 && state.HasComponent<DArea2D>(transform.Parent))
                isArea = true;

            var color = isArea ? Colors.Cyan : Colors.LimeGreen;

            if (shape.Type == DCollisionShapeType.Circle)
            {
                DrawCircle(im, cx, cz, (float)shape.Circle.Radius, color, 16, DrawHeight + 0.01f);
            }
            else if (shape.Type == DCollisionShapeType.Rectangle)
            {
                var hw = (float)shape.Rectangle.Size.X * 0.5f;
                var hh = (float)shape.Rectangle.Size.Y * 0.5f;
                DrawRect(im, cx, cz, hw, hh, color);
            }
            else if (shape.Type == DCollisionShapeType.Capsule)
            {
                DrawCircle(im, cx, cz, (float)shape.Capsule.Radius, color, 16, DrawHeight + 0.01f);
            }
        }

        im.SurfaceEnd();
        im.SurfaceSetMaterial(1, CreateLineMaterial());
        _collisionMesh.Mesh = im;
    }

    private void DrawNavMesh()
    {
        var state = GameManager.Instance.Game.State;
        var navState = state.GetCustomData<NavigationState>();
        if (navState == null) return;

        var map = navState.Map;
        if (map.Triangles.Count == 0) return;

        var im = new ImmediateMesh();

        // Draw filled triangles
        im.SurfaceBegin(Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < map.Triangles.Count; i++)
        {
            var tri = map.Triangles[i];
            if (tri.V0 < 0) continue;

            var v0 = map.Vertices[tri.V0].Position;
            var v1 = map.Vertices[tri.V1].Position;
            var v2 = map.Vertices[tri.V2].Position;

            im.SurfaceSetColor(new Color(0.2f, 0.5f, 1.0f, 0.15f));
            im.SurfaceAddVertex(new GVector3((float)v0.X, DrawHeight, (float)v0.Y));
            im.SurfaceAddVertex(new GVector3((float)v1.X, DrawHeight, (float)v1.Y));
            im.SurfaceAddVertex(new GVector3((float)v2.X, DrawHeight, (float)v2.Y));
        }

        im.SurfaceEnd();
        im.SurfaceSetMaterial(0, CreateNavMeshMaterial());

        // Draw wireframe edges
        im.SurfaceBegin(Mesh.PrimitiveType.Lines);

        for (int i = 0; i < map.Triangles.Count; i++)
        {
            var tri = map.Triangles[i];
            if (tri.V0 < 0) continue;

            var v0 = new GVector3((float)map.Vertices[tri.V0].Position.X, DrawHeight + 0.01f, (float)map.Vertices[tri.V0].Position.Y);
            var v1 = new GVector3((float)map.Vertices[tri.V1].Position.X, DrawHeight + 0.01f, (float)map.Vertices[tri.V1].Position.Y);
            var v2 = new GVector3((float)map.Vertices[tri.V2].Position.X, DrawHeight + 0.01f, (float)map.Vertices[tri.V2].Position.Y);

            im.SurfaceSetColor(new Color(0.3f, 0.6f, 1.0f, 0.6f));
            im.SurfaceAddVertex(v0); im.SurfaceAddVertex(v1);
            im.SurfaceAddVertex(v1); im.SurfaceAddVertex(v2);
            im.SurfaceAddVertex(v2); im.SurfaceAddVertex(v0);
        }

        im.SurfaceEnd();
        im.SurfaceSetMaterial(1, CreateLineMaterial());
        _navMeshInstance.Mesh = im;
    }

    private void DrawAgentPaths()
    {
        var state = GameManager.Instance.Game.State;
        var navState = state.GetCustomData<NavigationState>();
        if (navState == null) return;

        var im = new ImmediateMesh();
        im.SurfaceBegin(Mesh.PrimitiveType.Lines);

        foreach (var entity in state.Filter<DNavAgent, DTransform2D>())
        {
            ref var agent = ref state.GetComponent<DNavAgent>(entity);
            ref var transform = ref state.GetComponent<DTransform2D>(entity);

            var agentPos = new GVector3((float)transform.GlobalPosition.X, DrawHeight + 0.02f, (float)transform.GlobalPosition.Y);

            // Draw full path if available
            if (navState.AgentPaths.TryGetValue(entity.Id, out var pathData) && pathData.PathPoints.Count > 0)
            {
                var prev = agentPos;

                for (int i = pathData.CurrentPathIndex; i < pathData.PathPoints.Count; i++)
                {
                    var wp = pathData.PathPoints[i];
                    var next = new GVector3((float)wp.X, DrawHeight + 0.02f, (float)wp.Y);

                    im.SurfaceSetColor(Colors.Yellow);
                    im.SurfaceAddVertex(prev);
                    im.SurfaceAddVertex(next);
                    prev = next;
                }

                // Draw waypoint markers
                for (int i = pathData.CurrentPathIndex; i < pathData.PathPoints.Count; i++)
                {
                    var wp = pathData.PathPoints[i];
                    DrawCircle(im, (float)wp.X, (float)wp.Y, 0.15f, Colors.Orange, 6, DrawHeight + 0.02f);
                }
            }

            // Draw target position marker
            if (!agent.IsNavigationFinished)
            {
                DrawCircle(im, (float)agent.TargetPosition.X, (float)agent.TargetPosition.Y, 0.3f, Colors.Red, 8, DrawHeight + 0.02f);
            }

            // Draw agent radius
            DrawCircle(im, (float)transform.GlobalPosition.X, (float)transform.GlobalPosition.Y, (float)agent.Radius, Colors.Green, 8, DrawHeight + 0.02f);
        }

        im.SurfaceEnd();
        im.SurfaceSetMaterial(0, CreateLineMaterial());
        _agentPathsMesh.Mesh = im;
    }

    private static void DrawCircle(ImmediateMesh im, float cx, float cz, float radius, Color color, int segments = 16, float height = DrawHeight)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.Tau / segments;
            float a1 = (i + 1) * Mathf.Tau / segments;

            im.SurfaceSetColor(color);
            im.SurfaceAddVertex(new GVector3(cx + Mathf.Cos(a0) * radius, height, cz + Mathf.Sin(a0) * radius));
            im.SurfaceAddVertex(new GVector3(cx + Mathf.Cos(a1) * radius, height, cz + Mathf.Sin(a1) * radius));
        }
    }

    private static void DrawRect(ImmediateMesh im, float cx, float cz, float hw, float hh, Color color)
    {
        var tl = new GVector3(cx - hw, DrawHeight + 0.01f, cz - hh);
        var tr = new GVector3(cx + hw, DrawHeight + 0.01f, cz - hh);
        var br = new GVector3(cx + hw, DrawHeight + 0.01f, cz + hh);
        var bl = new GVector3(cx - hw, DrawHeight + 0.01f, cz + hh);

        im.SurfaceSetColor(color);
        im.SurfaceAddVertex(tl); im.SurfaceAddVertex(tr);
        im.SurfaceAddVertex(tr); im.SurfaceAddVertex(br);
        im.SurfaceAddVertex(br); im.SurfaceAddVertex(bl);
        im.SurfaceAddVertex(bl); im.SurfaceAddVertex(tl);
    }

    private static void FillCircle(ImmediateMesh im, float cx, float cz, float radius, Color color, int segments = 16)
    {
        var center = new GVector3(cx, DrawHeight, cz);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.Tau / segments;
            float a1 = (i + 1) * Mathf.Tau / segments;

            im.SurfaceSetColor(color);
            im.SurfaceAddVertex(center);
            im.SurfaceAddVertex(new GVector3(cx + Mathf.Cos(a0) * radius, DrawHeight, cz + Mathf.Sin(a0) * radius));
            im.SurfaceAddVertex(new GVector3(cx + Mathf.Cos(a1) * radius, DrawHeight, cz + Mathf.Sin(a1) * radius));
        }
    }

    private static void FillRect(ImmediateMesh im, float cx, float cz, float hw, float hh, Color color)
    {
        var tl = new GVector3(cx - hw, DrawHeight, cz - hh);
        var tr = new GVector3(cx + hw, DrawHeight, cz - hh);
        var br = new GVector3(cx + hw, DrawHeight, cz + hh);
        var bl = new GVector3(cx - hw, DrawHeight, cz + hh);

        im.SurfaceSetColor(color);
        im.SurfaceAddVertex(tl); im.SurfaceAddVertex(tr); im.SurfaceAddVertex(br);
        im.SurfaceAddVertex(tl); im.SurfaceAddVertex(br); im.SurfaceAddVertex(bl);
    }

    private static StandardMaterial3D CreateCollisionFillMaterial()
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = true,
        };
    }

    private static StandardMaterial3D CreateLineMaterial()
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
        };
    }

    private static StandardMaterial3D CreateNavMeshMaterial()
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.2f, 0.5f, 1.0f, 0.15f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = true,
        };
    }
}
