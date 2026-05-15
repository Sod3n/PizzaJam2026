using Godot;
using R3;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class HelperView
{
    private static readonly System.Collections.Generic.Dictionary<int, Texture2D> WantTextures = new()
    {
        { HelperType.Seller, GD.Load<Texture2D>("res://sprites/export/icons/Milky_/1.png") },
        { HelperType.Builder, GD.Load<Texture2D>("res://sprites/export/icons/Money_/1.png") },
    };

    private static readonly System.Collections.Generic.Dictionary<int, Texture2D> FoodTextures = new()
    {
        { FoodType.Grass, GD.Load<Texture2D>("res://sprites/export/icons/Grass_/1.png") },
        { FoodType.Carrot, GD.Load<Texture2D>("res://sprites/export/icons/Carrot_/1.png") },
        { FoodType.Apple, GD.Load<Texture2D>("res://sprites/export/icons/Apply_/1.png") },
        { FoodType.Mushroom, GD.Load<Texture2D>("res://sprites/export/icons/Mashroom/1.png") },
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
        // player has the requested resource. View swaps texture on the single WantIcon node
        // based on Type / WantedFoodType, and scales the anchor in/out for show/hide.
        var wantIcon = visualNode.GetNodeOrNull<Sprite3D>("%WantIcon");
        var wantAnchor = visualNode.GetNodeOrNull<Node3D>("%WantIconScaleAnchor");
        if (wantAnchor != null) wantAnchor.Scale = Vector3.Zero;
        if (wantIcon != null)
        {
            void RefreshTexture()
            {
                int t = vm.Helper.Helper.Type.CurrentValue;
                int wf = vm.Helper.Helper.WantedFoodType.CurrentValue;
                Texture2D tex = null;
                if (t == HelperType.Milker && wf >= 0) FoodTextures.TryGetValue(wf, out tex);
                else if (t == HelperType.Seller || t == HelperType.Builder) WantTextures.TryGetValue(t, out tex);
                if (tex != null) wantIcon.Texture = tex;
            }

            vm.Helper.Helper.Type.Subscribe(_ =>
                Callable.From(() => { if (IsInstanceValid(wantIcon)) RefreshTexture(); }).CallDeferred()
            ).AddTo(vm.Disposables);
            vm.Helper.Helper.WantedFoodType.Subscribe(_ =>
                Callable.From(() => { if (IsInstanceValid(wantIcon)) RefreshTexture(); }).CallDeferred()
            ).AddTo(vm.Disposables);
            vm.Helper.Helper.IsAsking.Subscribe(asking =>
                Callable.From(() => TweenIconScale(wantAnchor, asking)).CallDeferred()
            ).AddTo(vm.Disposables);
        }

        // Sleep icon — node lives in Helper.tscn so it's tweakable in the editor.
        var sleepAnchor = visualNode.GetNodeOrNull<Node3D>("%SleepIconScaleAnchor");
        if (sleepAnchor != null) sleepAnchor.Scale = Vector3.Zero;
        if (sleepAnchor != null)
        {
            vm.Helper.Helper.IsSleeping.Subscribe(sleeping =>
                Callable.From(() => TweenIconScale(sleepAnchor, sleeping)).CallDeferred()
            ).AddTo(vm.Disposables);
        }

        var readyAnchor = visualNode.GetNodeOrNull<Node3D>("%ReadyIconScaleAnchor");
        if (readyAnchor != null) readyAnchor.Scale = Vector3.Zero;
        if (readyAnchor != null)
        {
            vm.Helper.Helper.IsReadyForPickup.Subscribe(ready =>
                Callable.From(() => TweenIconScale(readyAnchor, ready)).CallDeferred()
            ).AddTo(vm.Disposables);
        }
    }

    private static void TweenIconScale(Node3D anchor, bool show)
    {
        if (!IsInstanceValid(anchor)) return;
        if (anchor.HasMeta("scale_tween") && anchor.GetMeta("scale_tween").As<Tween>() is { } prev && IsInstanceValid(prev))
            prev.Kill();
        var tween = anchor.CreateTween();
        anchor.SetMeta("scale_tween", tween);
        var target = show ? Vector3.One : Vector3.Zero;
        var trans = show ? Tween.TransitionType.Back : Tween.TransitionType.Quad;
        var ease = show ? Tween.EaseType.Out : Tween.EaseType.In;
        tween.TweenProperty(anchor, "scale", target, 0.2f).SetTrans(trans).SetEase(ease);
    }

    partial void OnDespawned(HelperViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
