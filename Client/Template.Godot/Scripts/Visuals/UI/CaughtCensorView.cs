using Godot;
using R3;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Godot.Core;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

// Single billboard quad spanning both player and catching cow with a screen-space pixelation
// shader. Visible only while the local player has CaughtComponent.
public partial class CaughtCensorView : Node3D
{
    private const float WidthPadding = 3.0f;
    private const float HeightPadding = 5.25f;
    private const float YOffset = 1.0f;
    private const float MinWidth = 6.75f;
    private const float MinHeight = 8.25f;
    private const float PixelSize = 12f;
    private static readonly Color Tint = new(1.0f, 1.0f, 1.0f);
    private const float Brightness = 0f;

    private static readonly Shader _shader =
        GD.Load<Shader>("res://shaders/caught_censor.gdshader");

    private static readonly System.Collections.Generic.List<CaughtCensorView> _active = new();
    private static readonly CompositeDisposable _watchers = new();
    private static bool _installed;

    private MeshInstance3D _quad;
    private Node3D _playerVisual;
    private Node3D _cowVisual;

    public static void InstallWatcher()
    {
        if (_installed) return;
        _installed = true;

        ReactiveSystem.Instance.ObserveAdd<CaughtComponent>()
            .Subscribe(entity =>
            {
                var gm = GameManager.Instance;
                if (gm == null || entity.Id != gm.LocalPlayerId) return;
                Callable.From(() => SpawnFor(entity)).CallDeferred();
            }).AddTo(_watchers);

        ReactiveSystem.Instance.ObserveRemove<CaughtComponent>()
            .Subscribe(entity =>
            {
                var gm = GameManager.Instance;
                if (gm == null || entity.Id != gm.LocalPlayerId) return;
                Callable.From(DespawnAll).CallDeferred();
            }).AddTo(_watchers);
    }

    private static void SpawnFor(Entity playerEntity)
    {
        var gm = GameManager.Instance;
        var tree = gm?.GetTree();
        if (tree?.Root == null)
        {
            GD.Print("[CaughtCensor] no tree/root");
            return;
        }
        if (_shader == null)
        {
            GD.Print("[CaughtCensor] shader failed to load");
            return;
        }

        var state = ReactiveSystem.Instance?.BoundState;
        if (state == null || !state.HasComponent<CaughtComponent>(playerEntity))
        {
            GD.Print("[CaughtCensor] no state/component");
            return;
        }
        var caught = state.GetComponent<CaughtComponent>(playerEntity);
        if (caught.CowEntity == Entity.Null)
        {
            GD.Print("[CaughtCensor] cow entity null");
            return;
        }

        if (!EntityViewModel.EntityVisualNodes.TryGetValue(playerEntity.Id, out var playerNode)
            || !IsInstanceValid(playerNode))
        {
            GD.Print($"[CaughtCensor] player visual missing for {playerEntity.Id}");
            return;
        }
        if (!EntityViewModel.EntityVisualNodes.TryGetValue(caught.CowEntity.Id, out var cowNode)
            || !IsInstanceValid(cowNode))
        {
            GD.Print($"[CaughtCensor] cow visual missing for {caught.CowEntity.Id}");
            return;
        }

        var view = new CaughtCensorView
        {
            _playerVisual = playerNode,
            _cowVisual = cowNode,
        };
        // Parent under the player's visual node so we share whatever scene/viewport the
        // active camera sees, instead of guessing at CurrentScene.
        playerNode.GetParent().AddChild(view);
        view.BuildQuad();
        _active.Add(view);
        GD.Print($"[CaughtCensor] spawned for player={playerEntity.Id} cow={caught.CowEntity.Id}");
    }

    private static void DespawnAll()
    {
        foreach (var v in _active)
            if (IsInstanceValid(v)) v.QueueFree();
        _active.Clear();
    }

    private void BuildQuad()
    {
        var mesh = new QuadMesh { Size = new Vector2(1f, 1f) };
        _quad = new MeshInstance3D
        {
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 10f,
        };

        var mat = new ShaderMaterial { Shader = _shader, RenderPriority = 10 };
        mat.SetShaderParameter("pixel_size", PixelSize);
        mat.SetShaderParameter("tint", new Vector3(Tint.R, Tint.G, Tint.B));
        mat.SetShaderParameter("brightness", Brightness);
        _quad.MaterialOverride = mat;
        AddChild(_quad);
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_playerVisual) || !IsInstanceValid(_cowVisual))
        {
            QueueFree();
            return;
        }

        var camera = GetViewport()?.GetCamera3D();
        if (camera == null) return;

        var pPos = _playerVisual.GlobalPosition;
        var cPos = _cowVisual.GlobalPosition;
        var mid = (pPos + cPos) * 0.5f;
        mid.Y += YOffset;

        float worldDist = pPos.DistanceTo(cPos);
        float width = Mathf.Max(MinWidth, worldDist + WidthPadding * 2f);
        float height = MinHeight + HeightPadding;

        GlobalPosition = mid;
        // Billboard: orient quad's -Z toward camera AND its +Y toward the camera's
        // screen-up so the cloud's top puffs always read as "up" on screen, not just
        // in world space (the game camera is tilted).
        var camPos = camera.GlobalPosition;
        var look = camPos - mid;
        if (look.LengthSquared() > 0.0001f)
            LookAt(camPos, camera.GlobalTransform.Basis.Y);

        _quad.Scale = new Vector3(width, height, 1f);

        if (_quad.MaterialOverride is ShaderMaterial sm)
            sm.SetShaderParameter("quad_aspect", height / Mathf.Max(0.001f, width));
    }
}
