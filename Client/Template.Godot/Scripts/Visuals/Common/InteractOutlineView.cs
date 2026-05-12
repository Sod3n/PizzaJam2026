using Godot;
using R3;
using System.Collections.Generic;
using Template.Godot.Core;
using Template.Shared.Components;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;

namespace Template.Godot.Visuals;

public partial class InteractOutlineView : Node3D
{
    [Export] public Color OutlineColor = new(1f, 1f, 1f, 0.9f);
    [Export] public float OutlineWidth = 2f;
    [Export] public float GroundPlaneY = -0.2f;

    // Layer 2 (bit 1) is reserved for outline rendering
    private const uint OutlineLayer = 1u << 1;
    // Layer 3 (bit 2) is a depth-only ground occluder, hidden from the main camera
    private const uint DepthLayer = 1u << 2;

    private static readonly Shader OutlineShader = GD.Load<Shader>("res://shaders/outline.gdshader");
    private static readonly Shader DepthOnlyShader = GD.Load<Shader>("res://shaders/depth_only.gdshader");

    private SubViewport _viewport;
    private Camera3D _outlineCamera;
    private CanvasLayer _canvasLayer;
    private TextureRect _textureRect;
    private MeshInstance3D _groundDepthPlane;
    private ShaderMaterial _outlineMat;

    private Camera3D _mainCamera;
    private uint _mainCameraOriginalCullMask;

    private Node3D _sourceVisual;
    private readonly List<VisualInstance3D> _layeredNodes = new();
    private int _currentEntityId = -1;
    private readonly CompositeDisposable _disposables = new();
    private bool _registered;

    public override void _Ready()
    {
        SetupOutlineComposite();
        if (GameManager.Instance != null && GameManager.Instance.IsGameRunning)
            Register();
        // Priority 200 > ViewSmoother (100), so by the time we sync the outline
        // camera, the player visual (which the main camera is parented to) has
        // already been updated this frame.
        ProcessPriority = 200;
        GetViewport().SizeChanged += OnViewportSizeChanged;
        OnViewportSizeChanged();
    }

    private void OnViewportSizeChanged()
    {
        var size = GetViewport().GetVisibleRect().Size;
        var sizeI = new Vector2I(Mathf.Max(1, (int)size.X), Mathf.Max(1, (int)size.Y));
        if (_viewport != null && _viewport.Size != sizeI)
            _viewport.Size = sizeI;
    }

    private void SetupOutlineComposite()
    {
        var screenSize = GetViewport().GetVisibleRect().Size;

        _viewport = new SubViewport
        {
            Size = new Vector2I((int)screenSize.X, (int)screenSize.Y),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };

        // Outline camera renders entity layer + depth occluder layer.
        // Interpolation Off — its transform is driven by a RemoteTransform3D
        // parented to the main camera (see Register), so each write must snap.
        _outlineCamera = new Camera3D
        {
            CullMask = OutlineLayer | DepthLayer,
            PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off,
        };
        _viewport.AddChild(_outlineCamera);
        AddChild(_viewport);

        // Ground plane rendered into the SubViewport as black with alpha.
        // Uses StandardMaterial3D with Alpha transparency so Godot's
        // transparent pass writes alpha to the SubViewport framebuffer.
        // Transparent ground plane — renders as black with alpha in the
        // SubViewport. The entity's depth_prepass_alpha writes depth first;
        // underground entity fragments are further from the camera, so the
        // ground plane (closer) passes the depth test and alpha-blends on
        // top, masking the underground portion.
        var groundMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0f, 0f, 0f, 1f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _groundDepthPlane = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(2000f, 2000f) },
            MaterialOverride = groundMat,
            Layers = OutlineLayer,
            PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off,
        };
        AddChild(_groundDepthPlane);

        // Hide OutlineLayer from main camera immediately so the ground plane
        // doesn't flash in the main scene before Register() runs.
        var cam = GetViewport().GetCamera3D();
        if (cam != null && IsInstanceValid(cam))
            cam.CullMask &= ~OutlineLayer;

        _outlineMat = new ShaderMaterial { Shader = OutlineShader };
        _outlineMat.SetShaderParameter("outline_color", OutlineColor);
        _outlineMat.SetShaderParameter("outline_width", OutlineWidth);
        var mat = _outlineMat;

        _textureRect = new TextureRect
        {
            Texture = _viewport.GetTexture(),
            Material = mat,
            Visible = false,
        };
        _textureRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        _canvasLayer = new CanvasLayer { Layer = -1 };
        _canvasLayer.AddChild(_textureRect);
        AddChild(_canvasLayer);
    }

    public override void _Process(double delta)
    {
        if (!_registered)
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;
            Register();
        }

        if (_currentEntityId >= 0 && _sourceVisual != null && !IsInstanceValid(_sourceVisual))
            ClearOutline();

        SyncOutlineCamera();
    }

    // Syncs the outline camera right before rendering. Reads the main camera's
    // plain GlobalTransform — the ViewSmoother writes positions every render
    // frame at the engine-provided interpolation fraction, so GlobalTransform
    // already matches the rendered pose. Using the *interpolated* getter here
    // applies a second, independent lerp and shifts the outline off-target.
    private void SyncOutlineCamera()
    {
        if (!IsInsideTree()) return;

        var mainCamera = GetViewport().GetCamera3D();
        if (mainCamera == null || !IsInstanceValid(mainCamera)) return;

        mainCamera.CullMask &= ~OutlineLayer;

        var transform = mainCamera.GlobalTransform;
        _outlineCamera.GlobalTransform = transform;
        _outlineCamera.Projection = mainCamera.Projection;
        _outlineCamera.Size = mainCamera.Size;
        _outlineCamera.Fov = mainCamera.Fov;
        _outlineCamera.Near = mainCamera.Near;
        _outlineCamera.Far = mainCamera.Far;

        var camPos = transform.Origin;
        _groundDepthPlane.GlobalPosition = new Vector3(camPos.X, GroundPlaneY, camPos.Z);
    }

    private void Register()
    {
        _registered = true;

        // Hide DepthLayer from the main camera so the invisible plane
        // doesn't touch the main scene's depth buffer.
        _mainCamera = GetViewport().GetCamera3D();
        if (_mainCamera != null && IsInstanceValid(_mainCamera))
        {
            _mainCameraOriginalCullMask = _mainCamera.CullMask;
            _mainCamera.CullMask &= ~(DepthLayer | OutlineLayer);
        }

        ReactiveSystem.Instance.ObserveAdd<InteractHighlightComponent>()
            .Subscribe(entity => Callable.From(() => ShowOutline(entity.Id)).CallDeferred())
            .AddTo(_disposables);

        ReactiveSystem.Instance.ObserveRemove<InteractHighlightComponent>()
            .Subscribe(entity => Callable.From(() => HideOutline(entity.Id)).CallDeferred())
            .AddTo(_disposables);
    }

    private void ShowOutline(int entityId)
    {
        ClearOutline();

        if (!EntityViewModel.EntityVisualNodes.TryGetValue(entityId, out var visualNode))
            return;
        if (!IsInstanceValid(visualNode))
            return;

        _currentEntityId = entityId;
        _sourceVisual = visualNode;

        AddOutlineLayer(visualNode);
        _textureRect.Visible = true;
    }

    private void HideOutline(int entityId)
    {
        if (entityId != _currentEntityId) return;
        ClearOutline();
    }

    private void ClearOutline()
    {
        foreach (var vi in _layeredNodes)
            if (IsInstanceValid(vi))
                vi.Layers &= ~OutlineLayer;

        _layeredNodes.Clear();
        _textureRect.Visible = false;
        _sourceVisual = null;
        _currentEntityId = -1;
    }

    private void AddOutlineLayer(Node node)
    {
        if (node is VisualInstance3D vi)
        {
            vi.Layers |= OutlineLayer;
            _layeredNodes.Add(vi);
        }
        foreach (var child in node.GetChildren())
            AddOutlineLayer(child);
    }

    public override void _ExitTree()
    {
        if (IsInsideTree())
            GetViewport().SizeChanged -= OnViewportSizeChanged;
        _disposables.Dispose();
        ClearOutline();

        // Restore main camera cull mask
        if (_mainCamera != null && IsInstanceValid(_mainCamera))
            _mainCamera.CullMask = _mainCameraOriginalCullMask;
    }
}
