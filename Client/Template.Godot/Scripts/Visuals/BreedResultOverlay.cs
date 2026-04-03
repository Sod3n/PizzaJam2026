using Godot;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;

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
    private bool _dismissed;
    private static readonly PackedScene _scene =
        GD.Load<PackedScene>("res://Scenes/BreedResultOverlay.tscn");

    private static readonly string[] _foodIcons =
    {
        "res://sprites/export/icons/Grass_/1.png",
        "res://sprites/export/icons/Carrot_/1.png",
        "res://sprites/export/icons/Apply_/1.png",
        "res://sprites/export/icons/Mashroom/1.png",
    };

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

    // ── Instance setup (called after AddChild) ────────────────────────────

    public void Setup(BreedResultData data, Node3D entityVisualNode)
    {
        // Stars — show 1/2/3 based on entity type
        var stars = GetNode<HBoxContainer>("GachaScreen/Top/HBoxContainer");
        stars.GetNode<Control>("Control").Visible  = data.Stars >= 1;
        stars.GetNode<Control>("Control2").Visible = data.Stars >= 2;
        stars.GetNode<Control>("Control3").Visible = data.Stars >= 3;

        // Name & Profession
        GetNode<Label>("GachaScreen/Top/Name").Text = data.Name;
        GetNode<Label>("GachaScreen/Top/Proffession").Text = data.Profession;

        // Background — legendary shader for helpers (3 stars)
        GetNode<ColorRect>("GachaScreen/GeneralBack").Visible  = data.Stars < 3;
        GetNode<ColorRect>("GachaScreen/LegendaryBack").Visible = data.Stars >= 3;

        // Bottom stats — only shown for cows
        var bottom = GetNode<Control>("GachaScreen/Bottom");
        bottom.Visible = data.ShowBottom;
        if (data.ShowBottom)
        {
            if (data.FoodPreference >= 0 && data.FoodPreference < _foodIcons.Length)
            {
                var foodIcon = GetNode<TextureRect>("GachaScreen/Bottom/FoodPreferenceIcon");
                foodIcon.Texture = GD.Load<Texture2D>(_foodIcons[data.FoodPreference]);
            }
            GetNode<Label>("GachaScreen/Bottom/MaxExhaust").Text = $"{data.MaxExhaust}";
        }

        // Character — duplicate into the viewport (camera/env/light already in scene)
        var viewport = GetNode<SubViewport>("%Viewport");

        var charSource = entityVisualNode?.GetNodeOrNull<Node3D>("Character")
            ?? entityVisualNode?.GetNodeOrNull<Node3D>("FlipPivot/Character");
        if (charSource != null)
        {
            var charCopy = (Node3D)charSource.Duplicate();
            charCopy.Transform = Transform3D.Identity;
            viewport.AddChild(charCopy);
            // Kill idle tweens after _ready fires
            Callable.From(() => charCopy.Call("stop_idle")).CallDeferred();

            // Re-apply skins so the duplicate matches the entity's actual appearance
            var state = ReactiveSystem.Instance.BoundState;
            if (state != null && state.HasComponent<SkinComponent>(data.Entity))
            {
                var skin = state.GetComponent<SkinComponent>(data.Entity);
                SkinVisualizer.UpdateSkins(charCopy, skin.Skins);
                SkinVisualizer.UpdateColors(charCopy, skin.Colors);
            }
        }

        // Animate entrance (deferred so layout is finalized first)
        Callable.From(_AnimateEntrance).CallDeferred();
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

    private void _AnimateEntrance()
    {
        if (!IsInsideTree()) return;

        var screen = GetNode<Control>("GachaScreen");

        // Fade in the whole screen
        screen.Modulate = new Color(1, 1, 1, 0);
        var fadeTween = CreateTween();
        fadeTween.TweenProperty(screen, "modulate:a", 1f, 0.3f)
                 .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    private void _Dismiss()
    {
        if (!IsInsideTree()) return;
        var screen = GetNode<Control>("GachaScreen");

        var tween = CreateTween();
        tween.TweenProperty(screen, "modulate:a", 0f, 0.25f);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string ReadName(Deterministic.GameFramework.ECS.Entity entity)
    {
        var state = ReactiveSystem.Instance.BoundState;
        if (state != null && state.HasComponent<NameComponent>(entity))
            return state.GetComponent<NameComponent>(entity).Name.ToString();
        return $"Entity #{entity.Id}";
    }

    private static void Spawn(SceneTree tree, BreedResultData data, Node3D visualNode)
    {
        if (_scene == null) return;
        var overlay = _scene.Instantiate<BreedResultOverlay>();
        tree.Root.AddChild(overlay);
        overlay.Setup(data, visualNode);
    }
}
