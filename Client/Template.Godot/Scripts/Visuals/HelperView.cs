using Godot;
using R3;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class HelperView
{
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
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        int helperType = vm.Helper.Helper.Type.CurrentValue;

        // Setup want icon (shown when helper wants resources from player: seller wants milk, builder wants coins, milker wants food)
        var wantIcon = visualNode.GetNodeOrNull<AnimatedSprite3D>("WantIcon");
        if (wantIcon != null)
        {
            // For seller and builder, set a static icon
            if (WantIcons.TryGetValue(helperType, out var iconPath))
            {
                SetWantIconTexture(wantIcon, iconPath);
            }

            // For milker, dynamically update the icon based on WantedFoodType
            if (helperType == HelperType.Milker)
            {
                vm.Helper.Helper.WantedFoodType.Subscribe(wantedFood =>
                {
                    Callable.From(() =>
                    {
                        if (!IsInstanceValid(wantIcon)) return;
                        if (wantedFood >= 0 && FoodIcons.TryGetValue(wantedFood, out var foodIconPath))
                        {
                            SetWantIconTexture(wantIcon, foodIconPath);
                        }
                    }).CallDeferred();
                }).AddTo(vm.Disposables);
            }

            // Show/hide based on helper state — visible when idle (wanting resources)
            vm.Helper.Helper.State.Subscribe(helperState =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(wantIcon)) return;
                    bool wantsResource;
                    if (helperType == HelperType.Milker)
                    {
                        // Milker shows icon only when idle AND has identified a food target
                        wantsResource = helperState == HelperState.Idle
                            && vm.Helper.Helper.WantedFoodType.CurrentValue >= 0;
                    }
                    else
                    {
                        // Seller wants milk, builder wants coins
                        wantsResource = helperState == HelperState.Idle
                            && (helperType == HelperType.Seller || helperType == HelperType.Builder);
                    }
                    wantIcon.Visible = wantsResource;
                }).CallDeferred();
            }).AddTo(vm.Disposables);

            // For milker, also update visibility when WantedFoodType changes
            if (helperType == HelperType.Milker)
            {
                vm.Helper.Helper.WantedFoodType.Subscribe(wantedFood =>
                {
                    Callable.From(() =>
                    {
                        if (!IsInstanceValid(wantIcon)) return;
                        bool wantsResource = vm.Helper.Helper.State.CurrentValue == HelperState.Idle
                            && wantedFood >= 0;
                        wantIcon.Visible = wantsResource;
                    }).CallDeferred();
                }).AddTo(vm.Disposables);
            }
        }

        // Setup ready icon (exclamation mark) — shown when helper has resources ready for pickup
        var readyIcon = visualNode.GetNodeOrNull<Sprite3D>("ReadyIcon");
        if (readyIcon != null)
        {
            vm.Helper.Helper.State.Subscribe(helperState =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(readyIcon)) return;
                    readyIcon.Visible = helperState == HelperState.WaitingForPickup;
                }).CallDeferred();
            }).AddTo(vm.Disposables);
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
