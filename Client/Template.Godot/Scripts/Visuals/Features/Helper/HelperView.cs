using Godot;
using R3;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class HelperView
{
    private static readonly Texture2D _sleepTexture =
        GD.Load<Texture2D>("res://sprites/sleep.png");

    private static readonly System.Collections.Generic.Dictionary<int, string> WantIcons = new()
    {
        { HelperType.Seller, "res://sprites/export/icons/Milky_/1.png" },
        { HelperType.Builder, "res://sprites/export/icons/Money_/1.png" },
    };

    private static readonly System.Collections.Generic.Dictionary<int, string> FoodIcons = new()
    {
        { FoodType.Grass, "res://sprites/export/icons/Grass_/1.png" },
        { FoodType.Carrot, "res://sprites/export/icons/Carrot_/1.png" },
        { FoodType.Apple, "res://sprites/export/icons/Apply_/3.png" },
        { FoodType.Mushroom, "res://sprites/export/icons/Mashroom/1.png" },
    };

    partial void OnSpawned(HelperViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);

        // Show breed result overlay for helpers unlocked at breed milestones
        var state = ReactiveSystem.Instance.BoundState;
        if (state != null && state.HasComponent<BreedBornComponent>(vm.Entity))
            Callable.From(() => BreedResultOverlay.ShowForHelper(GetTree(), vm, visualNode)).CallDeferred();
        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.Helper.CharacterBody2D.Velocity, flipPivot, characterNode);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        // Squish the inner character node, not the root — the root is driven by ViewSmoother
        // every frame, so animating its position fights with the smoother and yanks the helper
        // back to a stale cached "orig_pos" each interact.
        ViewHelpers.SetupInteractAnimation(vm, visualNode, animateNode: characterNode);

        // Want icon — server flags `Helper.IsAsking` whenever the helper is idle AND the
        // player has the requested resource. View just subscribes and toggles visibility.
        // Texture is keyed off Type / WantedFoodType (also reactive).
        var wantIcon = visualNode.GetNodeOrNull<AnimatedSprite3D>("WantIcon");
        if (wantIcon != null)
        {
            void RefreshTexture()
            {
                int t = vm.Helper.Helper.Type.CurrentValue;
                int wf = vm.Helper.Helper.WantedFoodType.CurrentValue;
                string iconPath = null;
                if (t == HelperType.Milker && wf >= 0) FoodIcons.TryGetValue(wf, out iconPath);
                else if (t == HelperType.Seller || t == HelperType.Builder) WantIcons.TryGetValue(t, out iconPath);
                if (iconPath != null) SetWantIconTexture(wantIcon, iconPath);
            }

            vm.Helper.Helper.Type.Subscribe(_ =>
                Callable.From(() => { if (IsInstanceValid(wantIcon)) RefreshTexture(); }).CallDeferred()
            ).AddTo(vm.Disposables);
            vm.Helper.Helper.WantedFoodType.Subscribe(_ =>
                Callable.From(() => { if (IsInstanceValid(wantIcon)) RefreshTexture(); }).CallDeferred()
            ).AddTo(vm.Disposables);
            vm.Helper.Helper.IsAsking.Subscribe(asking =>
                Callable.From(() => { if (IsInstanceValid(wantIcon)) wantIcon.Visible = asking; }).CallDeferred()
            ).AddTo(vm.Disposables);
        }

        // Sleep icon — server flags `Helper.IsSleeping` whenever there's no work the helper
        // can do. View just subscribes.
        var sleepIcon = new Sprite3D
        {
            Texture = _sleepTexture,
            PixelSize = 0.0015f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            Shaded = false,
            Position = new Vector3(0, 2.27f, 0),
            NoDepthTest = true,
            RenderPriority = 4,
            Visible = false,
        };
        visualNode.AddChild(sleepIcon);

        vm.Helper.Helper.IsSleeping.Subscribe(sleeping =>
            Callable.From(() => { if (IsInstanceValid(sleepIcon)) sleepIcon.Visible = sleeping; }).CallDeferred()
        ).AddTo(vm.Disposables);

        // Ready icon (exclamation) — shown when helper has resources ready for pickup
        var readyIcon = visualNode.GetNodeOrNull<Sprite3D>("ReadyIcon");
        if (readyIcon != null)
        {
            vm.Helper.Helper.State.Subscribe(helperState =>
                Callable.From(() =>
                {
                    if (IsInstanceValid(readyIcon))
                        readyIcon.Visible = helperState == HelperState.WaitingForPickup;
                }).CallDeferred()
            ).AddTo(vm.Disposables);
        }
    }

    private static void SetWantIconTexture(AnimatedSprite3D wantIcon, string iconPath)
    {
        var texture = GD.Load<Texture2D>(iconPath);
        if (texture != null)
        {
            var frames = new SpriteFrames();
            frames.AddAnimation("default");
            frames.AddFrame("default", texture);
            wantIcon.SpriteFrames = frames;
            wantIcon.Animation = "default";
            wantIcon.Frame = 0;
        }
    }

    partial void OnDespawned(HelperViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
