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

        // Setup want icon
        var wantIcon = visualNode.GetNodeOrNull<AnimatedSprite3D>("WantIcon");
        if (wantIcon != null)
        {
            int helperType = vm.Helper.Helper.Type.CurrentValue;

            if (WantIcons.TryGetValue(helperType, out var iconPath))
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

            // Show/hide based on helper state — visible when idle (wanting resources)
            vm.Helper.Helper.State.Subscribe(state =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(wantIcon)) return;
                    // Show icon only when idle (seller wants milk, builder wants coins)
                    bool wantsResource = state == HelperState.Idle
                        && (helperType == HelperType.Seller || helperType == HelperType.Builder);
                    wantIcon.Visible = wantsResource;
                }).CallDeferred();
            }).AddTo(vm.Disposables);
        }
    }

    partial void OnDespawned(HelperViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
