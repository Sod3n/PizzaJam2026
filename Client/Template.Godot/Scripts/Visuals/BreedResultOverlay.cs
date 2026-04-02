using Godot;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public struct BreedResultData
{
    public string Profession;
    public string FoodPreference;
    public string Stamina;
    public string OwnerInfo; // null if not a pet
}

public partial class BreedResultOverlay : CanvasLayer
{
    private bool _dismissed;
    private static readonly PackedScene _scene =
        GD.Load<PackedScene>("res://Scenes/BreedResultOverlay.tscn");

    private static readonly string[] _foodNames = { "Grass", "Carrot", "Apple", "Mushroom" };

    // ── Static entry points ───────────────────────────────────────────────

    public static void ShowForCow(SceneTree tree, CowViewModel vm, Node3D visualNode)
    {
        int food = vm.Cow.Cow.PreferredFood.CurrentValue;
        int exhaust = vm.Cow.Cow.Exhaust.CurrentValue;
        int maxExhaust = vm.Cow.Cow.MaxExhaust.CurrentValue;
        Spawn(tree, new BreedResultData
        {
            Profession = "Regular Cow",
            FoodPreference = food >= 0 && food < _foodNames.Length ? _foodNames[food] : "Unknown",
            Stamina = $"{maxExhaust - exhaust}/{maxExhaust}",
        }, visualNode);
    }

    public static void ShowForHelper(SceneTree tree, HelperViewModel vm, Node3D visualNode)
    {
        int type = vm.Helper.Helper.Type.CurrentValue;
        Spawn(tree, new BreedResultData
        {
            Profession = type switch
            {
                HelperType.Gatherer  => "Gatherer",
                HelperType.Seller    => "Seller",
                HelperType.Builder   => "Builder",
                HelperType.Assistant => "Assistant",
                _ => "Helper",
            },
            FoodPreference = "N/A",
            Stamina = "N/A",
        }, visualNode);
    }

    public static void ShowForPet(SceneTree tree, HelperPetViewModel vm, Node3D visualNode)
    {
        var state = ReactiveSystem.Instance.BoundState;
        string ownerInfo = "Unknown";
        if (state != null)
        {
            var comp = state.GetComponent<HelperPetComponent>(vm.Entity);
            ownerInfo = comp.FollowTarget.Id != 0 ? $"Entity #{comp.FollowTarget.Id}" : "Wandering";
        }
        Spawn(tree, new BreedResultData
        {
            Profession = "Pet",
            FoodPreference = "N/A",
            Stamina = "N/A",
            OwnerInfo = ownerInfo,
        }, visualNode);
    }

    // ── Instance setup (called after AddChild) ────────────────────────────

    public void Setup(BreedResultData data, Node3D entityVisualNode)
    {
        // Populate info labels
        GetNode<Label>("%ProfValue").Text = data.Profession;
        GetNode<Label>("%FoodValue").Text = data.FoodPreference;
        GetNode<Label>("%StamValue").Text = data.Stamina;

        if (!string.IsNullOrEmpty(data.OwnerInfo))
        {
            GetNode<Label>("%FollowsLabel").Visible = true;
            var followsVal = GetNode<Label>("%FollowsValue");
            followsVal.Visible = true;
            followsVal.Text = data.OwnerInfo;
        }

        // Add camera and character copy into SubViewport
        var viewport = GetNode<SubViewport>("%Viewport");

        var camera = new Camera3D
        {
            Position = new Vector3(0f, 1.5f, 5f),
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 5f,
        };
        camera.LookAt(new Vector3(0f, 1.5f, 0f));
        viewport.AddChild(camera);

        var dropRoot = new Node3D { Name = "DropRoot", Position = new Vector3(0f, 5f, 0f) };
        viewport.AddChild(dropRoot);

        // Duplicate the already-skinned Character node from the spawned entity visual.
        // SetupFlipPivot reparents Character under FlipPivot, so try both paths.
        var charSource = entityVisualNode?.GetNodeOrNull<Node3D>("Character")
            ?? entityVisualNode?.GetNodeOrNull<Node3D>("FlipPivot/Character");
        if (charSource != null)
        {
            var charCopy = (Node3D)charSource.Duplicate();
            charCopy.Position = Vector3.Zero;
            charCopy.Set("enable_bounce", false);
            dropRoot.AddChild(charCopy);
        }

        // Animate entrance (deferred so layout is finalized first)
        Callable.From(() => _Animate(dropRoot)).CallDeferred();
    }

    public override void _Input(InputEvent @event)
    {
        if (_dismissed) return;
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            GetViewport().SetInputAsHandled();
            _dismissed = true;
            _Dismiss();
        }
    }

    // ── Animation ─────────────────────────────────────────────────────────

    private void _Animate(Node3D dropRoot)
    {
        if (!IsInsideTree()) return;

        var backdrop = GetNode<ColorRect>("%Backdrop");
        var card = GetNode<Control>("%Card");

        // Backdrop: fade in
        var bgTween = CreateTween();
        bgTween.TweenProperty(backdrop, "color:a", 0.65f, 0.3f)
               .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

        // Card: slide up from below (shift both offsets to keep size constant)
        float vpHeight = GetViewport()?.GetVisibleRect().Size.Y ?? 1080f;
        float slide = vpHeight * 0.3f;
        card.OffsetTop = slide;
        card.OffsetBottom = slide;
        var cardTween = CreateTween().SetParallel();
        cardTween.TweenProperty(card, "offset_top", 0f, 0.45f)
                 .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        cardTween.TweenProperty(card, "offset_bottom", 0f, 0.45f)
                 .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        // Character: drop from above with bounce
        if (IsInstanceValid(dropRoot))
        {
            var dropTween = CreateTween();
            dropTween.TweenProperty(dropRoot, "position:y", 0f, 0.7f)
                     .SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out)
                     .SetDelay(0.15f);
        }
    }

    private void _Dismiss()
    {
        if (!IsInsideTree()) return;
        var backdrop = GetNode<ColorRect>("%Backdrop");
        var card = GetNode<Control>("%Card");

        var tween = CreateTween().SetParallel();
        tween.TweenProperty(backdrop, "color:a", 0f, 0.25f);
        tween.TweenProperty(card, "modulate:a", 0f, 0.2f);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void Spawn(SceneTree tree, BreedResultData data, Node3D visualNode)
    {
        if (_scene == null) return;
        var overlay = _scene.Instantiate<BreedResultOverlay>();
        tree.Root.AddChild(overlay);
        overlay.Setup(data, visualNode);
    }
}
