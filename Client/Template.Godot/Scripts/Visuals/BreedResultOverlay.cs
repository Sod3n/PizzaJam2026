using Godot;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Template.Godot.Twitch;

using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

public struct BreedResultData
{
    public int Stars;          // 1 = cow, 2 = pet, 3 = helper
    public string Name;
    public string Profession;
    public int FoodPreference; // FoodType constant, -1 for non-cows
    public int MaxExhaust;
    public bool ShowBottom;    // true only for cows
    public Deterministic.GameFramework.ECS.Entity Entity;
}

public partial class BreedResultOverlay : CanvasLayer
{
    private enum Phase { Reveal, Preview, Dismissed }
    private Phase _phase;
    private Tween _activeTween;

    // Cached nodes
    private Node3D _charNode;
    private SubViewportContainer _charContainer;
    private Control _top;
    private Control _bottom;
    private HBoxContainer _starsBox;
    private Label _nameLabel;
    private Label _profLabel;
    private ColorRect _background;
    private BreedResultData _data;

    // Static resources
    private static readonly PackedScene _scene =
        GD.Load<PackedScene>("res://Scenes/BreedResultOverlay.tscn");

    private static readonly string[] _foodIcons =
    {
        "res://sprites/export/icons/Grass_/1.png",
        "res://sprites/export/icons/Carrot_/1.png",
        "res://sprites/export/icons/Apply_/1.png",
        "res://sprites/export/icons/Mashroom/1.png",
    };

    private static readonly Shader _smoothShader =
        GD.Load<Shader>("res://shaders/smooth_character.gdshader");

    private static readonly Texture2D _heartTexture =
        GD.Load<Texture2D>("res://sprites/heart.png");

    // ── Static entry points ───────────────────────────────────────────────

    public static void ShowForCow(SceneTree tree, CowViewModel vm, Node3D visualNode)
    {
        int food = vm.Cow.Cow.PreferredFood.CurrentValue;
        int maxExhaust = vm.Cow.Cow.MaxExhaust.CurrentValue;
        Spawn(tree, new BreedResultData
        {
            Stars = 1,
            Name = ReadName(vm.Entity),
            Profession = "Cow",
            FoodPreference = food,
            MaxExhaust = maxExhaust,
            ShowBottom = true,
            Entity = vm.Entity,
        }, visualNode);
    }

    public static void ShowForHelper(SceneTree tree, HelperViewModel vm, Node3D visualNode)
    {
        int type = vm.Helper.Helper.Type.CurrentValue;
        string profession = type switch
        {
            HelperType.Gatherer  => "Gatherer",
            HelperType.Seller    => "Seller",
            HelperType.Builder   => "Builder",
            HelperType.Milker    => "Milker",
            HelperType.Assistant => "Assistant",
            _ => "Helper",
        };
        Spawn(tree, new BreedResultData
        {
            Stars = 3,
            Name = ReadName(vm.Entity),
            Profession = profession,
            FoodPreference = -1,
            ShowBottom = false,
            Entity = vm.Entity,
        }, visualNode);
    }

    public static void ShowForPet(SceneTree tree, HelperPetViewModel vm, Node3D visualNode)
    {
        var state = ReactiveSystem.Instance.BoundState;
        string ownerName = "Unknown";
        if (state != null)
        {
            var comp = state.GetComponent<HelperPetComponent>(vm.Entity);
            if (comp.FollowTarget.Id != 0)
                ownerName = ReadName(comp.FollowTarget);
        }
        Spawn(tree, new BreedResultData
        {
            Stars = 2,
            Name = ReadName(vm.Entity),
            Profession = $"Pet of {ownerName}",
            FoodPreference = -1,
            ShowBottom = false,
            Entity = vm.Entity,
        }, visualNode);
    }

    // ── Instance setup ────────────────────────────────────────────────────

    public void Setup(BreedResultData data, Node3D entityVisualNode)
    {
        _data = data;

        // Cache nodes
        _top = GetNode<Control>("GachaScreen/Top");
        _bottom = GetNode<Control>("GachaScreen/Bottom");
        _starsBox = GetNode<HBoxContainer>("GachaScreen/Top/HBoxContainer");
        _nameLabel = GetNode<Label>("GachaScreen/Top/Name");
        _profLabel = GetNode<Label>("GachaScreen/Top/Proffession");
        _charContainer = GetNode<SubViewportContainer>("GachaScreen/Control/CharViewport");

        // Configure star visibility
        _starsBox.GetNode<Control>("Control").Visible  = data.Stars >= 1;
        _starsBox.GetNode<Control>("Control2").Visible = data.Stars >= 2;
        _starsBox.GetNode<Control>("Control3").Visible = data.Stars >= 3;

        // Set label text
        _nameLabel.Text = data.Name;
        _profLabel.Text = data.Profession;

        // Background — legendary for helpers (3★)
        var generalBack = GetNode<ColorRect>("GachaScreen/GeneralBack");
        var legendaryBack = GetNode<ColorRect>("GachaScreen/LegendaryBack");
        generalBack.Visible  = data.Stars < 3;
        legendaryBack.Visible = data.Stars >= 3;
        _background = data.Stars >= 3 ? legendaryBack : generalBack;

        // Bottom stats (cow only)
        _bottom.Visible = data.ShowBottom;
        if (data.ShowBottom)
        {
            if (data.FoodPreference >= 0 && data.FoodPreference < _foodIcons.Length)
                GetNode<TextureRect>("GachaScreen/Bottom/StatsBack/FoodPreferenceIcon").Texture =
                    GD.Load<Texture2D>(_foodIcons[data.FoodPreference]);
            GetNode<Label>("GachaScreen/Bottom/StatsBack/MaxExhaust").Text = $"{data.MaxExhaust}";
        }

        // ── Hide UI for reveal phase ──
        _top.Visible = false;
        if (data.ShowBottom)
        {
            _bottom.OffsetTop = 200;
            _bottom.OffsetBottom = 200;
        }

        // ── Character ──
        var viewport = GetNode<SubViewport>("%Viewport");
        var charSource = entityVisualNode?.GetNodeOrNull<Node3D>("Character")
            ?? entityVisualNode?.GetNodeOrNull<Node3D>("FlipPivot/Character");
        if (charSource != null)
        {
            _charNode = (Node3D)charSource.Duplicate();
            _charNode.Transform = Transform3D.Identity;
            _charNode.Scale = GVector3.Zero;
            viewport.AddChild(_charNode);
            Callable.From(() => _charNode.Call("stop_idle")).CallDeferred();
            StripPixelShaders(_charNode);

            var reactiveState = ReactiveSystem.Instance.BoundState;
            if (reactiveState != null && reactiveState.HasComponent<SkinComponent>(data.Entity))
            {
                var skin = reactiveState.GetComponent<SkinComponent>(data.Entity);
                SkinVisualizer.UpdateSkins(_charNode, skin.Skins);
                SkinVisualizer.UpdateColors(_charNode, skin.Colors);
            }
        }

        // Silhouette tint
        _charContainer.Modulate = Colors.Black;

        Callable.From(_StartReveal).CallDeferred();
    }

    // ── Input ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the event represents an "advance/skip" press.
    /// Accepts the same inputs used for interaction in InputManager:
    ///   mouse left-click, screen tap, Space (interact), Enter/ui_accept,
    ///   and gamepad A button.
    /// </summary>
    private static bool IsAdvancePress(InputEvent @event)
    {
        // Mouse left-click
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            return true;

        // Touch screen tap (release after short drag = tap)
        if (@event is InputEventScreenTouch { Pressed: true })
            return true;

        // Keyboard / gamepad actions (Space, Enter, Gamepad A)
        if (@event.IsPressed() && !@event.IsEcho())
        {
            if (@event.IsAction("interact") ||
                @event.IsAction("ui_accept") ||
                @event.IsAction("gamepad_interact"))
                return true;
        }

        return false;
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsAdvancePress(@event))
            return;
        GetViewport().SetInputAsHandled();

        switch (_phase)
        {
            case Phase.Reveal:
                // Do not allow skipping during the reveal animation.
                // The player must wait for the reveal to finish.
                return;
            case Phase.Preview:
                _phase = Phase.Dismissed;
                _Dismiss();
                break;
        }
    }

    // ── Reveal state ──────────────────────────────────────────────────────

    private void _StartReveal()
    {
        if (!IsInsideTree()) return;
        _phase = Phase.Reveal;

        var screen = GetNode<Control>("GachaScreen");
        screen.Modulate = new Color(1, 1, 1, 0);

        _activeTween = CreateTween();

        // Fade in background
        _activeTween.TweenProperty(screen, "modulate:a", 1f, 0.3f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

        // Bake the shader background — hide other elements so the screenshot is clean
        _activeTween.TweenCallback(Callable.From(() => _charContainer.Visible = false));
        _activeTween.TweenCallback(Callable.From(() => _background.Call("bake_background")));
        _activeTween.TweenInterval(0.15f); // wait for async bake (~2-3 frames)
        _activeTween.TweenCallback(Callable.From(() => _charContainer.Visible = true));

        if (_charNode != null)
        {
            // Scale 0→1 + spin Y (3 full rotations) — parallel
            _activeTween.SetParallel();
            _activeTween.TweenProperty(_charNode, "scale", GVector3.One, 2f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            _activeTween.TweenProperty(_charNode, "rotation_degrees:y", 1080f, 2f)
                .From(0f)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            _activeTween.SetParallel(false);

            // Squish + reveal color simultaneously
            _activeTween.TweenProperty(_charNode, "scale",
                new GVector3(1.4f, 0.7f, 1f), 0.1f)
                .SetTrans(Tween.TransitionType.Sine);
            _activeTween.Parallel().TweenProperty(_charContainer, "modulate",
                Colors.White, 0.2f);
            _activeTween.TweenProperty(_charNode, "scale", GVector3.One, 0.15f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        else
        {
            _charContainer.Modulate = Colors.White;
        }

        // Heart burst + brief pause
        _activeTween.TweenCallback(Callable.From(_SpawnHearts));
        _activeTween.TweenInterval(0.4f);

        // → Preview
        _activeTween.TweenCallback(Callable.From(_StartPreview));
    }

    private void _SkipToPreview()
    {
        _activeTween?.Kill();

        // Snap to final reveal state
        GetNode<Control>("GachaScreen").Modulate = Colors.White;
        _charContainer.Modulate = Colors.White;
        if (_charNode != null)
        {
            _charNode.Scale = GVector3.One;
            _charNode.RotationDegrees = GVector3.Zero;
        }

        _StartPreview();
    }

    // ── Preview state ─────────────────────────────────────────────────────

    private void _StartPreview()
    {
        if (!IsInsideTree()) return;
        _phase = Phase.Preview;

        // Make Top visible — children start at scale 0 so nothing pops yet
        _top.Visible = true;

        _activeTween = CreateTween();

        // Bottom slides up from below
        if (_data.ShowBottom)
        {
            _activeTween.TweenProperty(_bottom, "offset_top", 0f, 0.4f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            _activeTween.Parallel().TweenProperty(_bottom, "offset_bottom", 0f, 0.4f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }

        // Stars pop in sequentially
        string[] starNames = { "Control", "Control2", "Control3" };
        for (int i = 0; i < _data.Stars; i++)
        {
            var star = _starsBox.GetNode<Control>(starNames[i])
                .GetNode<TextureRect>("Star");
            // pivot_offset already set to (35,35) in scene
            star.Scale = Vector2.Zero;
            _activeTween.TweenProperty(star, "scale", Vector2.One * 1.5f, 0.15f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            _activeTween.TweenProperty(star, "scale", Vector2.One, 0.1f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        // Name pops in
        _nameLabel.PivotOffset = _nameLabel.Size / 2;
        _nameLabel.Scale = Vector2.Zero;
        _activeTween.TweenProperty(_nameLabel, "scale", Vector2.One * 1.5f, 0.15f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _activeTween.TweenProperty(_nameLabel, "scale", Vector2.One, 0.1f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        // Profession pops in
        _profLabel.PivotOffset = _profLabel.Size / 2;
        _profLabel.Scale = Vector2.Zero;
        _activeTween.TweenProperty(_profLabel, "scale", Vector2.One * 1.5f, 0.15f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _activeTween.TweenProperty(_profLabel, "scale", Vector2.One, 0.1f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    // ── Dismiss ───────────────────────────────────────────────────────────

    private void _Dismiss()
    {
        _activeTween?.Kill();
        if (!IsInsideTree()) return;

        _background.Call("unbake");
        var screen = GetNode<Control>("GachaScreen");

        var tween = CreateTween();
        tween.TweenProperty(screen, "modulate:a", 0f, 0.25f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            var tree = GetTree();
            QueueFree();
            _OnDismissed(tree);
        }));
    }

    // ── Heart burst ───────────────────────────────────────────────────────

    private void _SpawnHearts()
    {
        var viewport = GetNode<SubViewport>("%Viewport");
        for (int i = 0; i < 5; i++)
        {
            float t = i / 4f;
            float angle = Mathf.Lerp(-1.2f, 1.2f, t);
            _SpawnHeart(viewport, angle);
        }
    }

    private void _SpawnHeart(Node parent, float angle)
    {
        if (_heartTexture == null) return;
        var sprite = new Sprite3D
        {
            Texture = _heartTexture,
            PixelSize = 0.003f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            Shaded = false,
            Position = new GVector3(0f, 10f, 0f),
            Scale = GVector3.Zero,
        };
        parent.AddChild(sprite);

        float dist = 1.5f;
        float endX = Mathf.Sin(angle) * dist;
        float endY = 10f + Mathf.Cos(angle) * dist;

        var tween = sprite.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(sprite, "scale", GVector3.One, 0.1f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(sprite, "position", new GVector3(endX, endY, 0f), 0.4f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(sprite, "modulate:a", 0f, 0.2f)
            .SetDelay(0.25f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(sprite.QueueFree));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string ReadName(Deterministic.GameFramework.ECS.Entity entity)
    {
        return TwitchIntegration.GetDisplayName(entity);
    }

    private static void StripPixelShaders(Node node)
    {
        if (node is GeometryInstance3D geo && geo.MaterialOverride is ShaderMaterial mat)
        {
            var smooth = (ShaderMaterial)mat.Duplicate();
            smooth.Shader = _smoothShader;
            geo.MaterialOverride = smooth;
        }
        if (node is SpriteBase3D sprite)
            sprite.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
        foreach (var child in node.GetChildren())
            StripPixelShaders(child);
    }

    // ── Queue (only one overlay at a time) ──────────────────────────────

    private static readonly System.Collections.Generic.Queue<(BreedResultData data, Node3D visual)> _queue = new();
    private static BreedResultOverlay _current;

    /// <summary>True while any overlay is on screen. Use to block game input.</summary>
    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    private static void Spawn(SceneTree tree, BreedResultData data, Node3D visualNode)
    {
        if (_scene == null) return;

        if (_current != null && Node.IsInstanceValid(_current))
        {
            _queue.Enqueue((data, visualNode));
            return;
        }

        _SpawnImmediate(tree, data, visualNode);
    }

    private static void _SpawnImmediate(SceneTree tree, BreedResultData data, Node3D visualNode)
    {
        var overlay = _scene.Instantiate<BreedResultOverlay>();
        _current = overlay;
        tree.Root.AddChild(overlay);
        overlay.Setup(data, visualNode);
    }

    private static void _OnDismissed(SceneTree tree)
    {
        _current = null;
        if (_queue.Count > 0)
        {
            var (data, visual) = _queue.Dequeue();
            _SpawnImmediate(tree, data, visual);
        }
    }
}
