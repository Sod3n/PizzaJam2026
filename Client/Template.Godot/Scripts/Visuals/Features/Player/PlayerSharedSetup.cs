using System;
using Godot;
using R3;
using Template.Godot.Core;
using Template.Shared.Components;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

/// <summary>
/// Shared view setup for player-like entities (regular players and helper players).
/// Both visuals are functionally identical on the client — only skin/UI differ — so
/// flip/movement/position/interact/camera bindings live here, not duplicated per view.
/// </summary>
public static class PlayerSharedSetup
{
    private const float NormalZoom = 20f;
    private const float CloseZoom = 10f;
    private const float ZoomInDuration = 0.5f;
    private const float ZoomOutDelay = 1.0f;
    private const float ZoomOutDuration = 0.8f;

    public static void Setup(
        EntityViewModel vm,
        Node3D visualNode,
        ReactiveProperty<bool> isHidden,
        ReadOnlyReactiveProperty<GVector2> velocity)
    {
        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, velocity, flipPivot, characterNode);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var camera = visualNode.GetNodeOrNull<Camera3D>("Camera");
        if (camera == null) return;

        // LocalPlayerId is set by SetupLocalPlayerDiscovery on a separate reactive
        // tick — racing OnSpawned. Subscribe so the camera ends up correct whenever
        // discovery resolves, instead of locking in a stale answer at spawn time.
        GameManager.Instance.LocalPlayerIdReactive.Subscribe(localId =>
        {
            Callable.From(() =>
            {
                if (!Node.IsInstanceValid(camera)) return;
                camera.Current = (localId == vm.Entity.Id);
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        Tween zoomTween = null;
        bool zoomingOut = false;
        bool waitingToZoomOut = false;

        isHidden.Subscribe(hidden =>
        {
            // InteractionTarget is set 30 ticks before HiddenComponent (Milking.Enter
            // duration), so it's stable by now. Capture the target entity sync; resolve
            // its world position inside the deferred callable so Godot transforms are settled.
            Entity targetEntity = Entity.Null;
            if (hidden)
            {
                var state = ReactiveSystem.Instance?.BoundState;
                if (state != null && state.HasComponent<PlayerStateComponent>(vm.Entity))
                    targetEntity = state.GetComponent<PlayerStateComponent>(vm.Entity).InteractionTarget;
                // Player frames "the building they're inside", not the cow — when milking,
                // resolve the cow's house so the camera centers on the house, not the cow.
                if (targetEntity != Entity.Null
                    && state != null
                    && state.HasComponent<CowComponent>(targetEntity))
                {
                    var houseId = state.GetComponent<CowComponent>(targetEntity).HouseId;
                    if (houseId != Entity.Null) targetEntity = houseId;
                }
            }

            Callable.From(() =>
            {
                if (!Node.IsInstanceValid(camera)) return;
                zoomTween?.Kill();
                zoomingOut = false;
                waitingToZoomOut = false;
                zoomTween = camera.CreateTween();
                if (hidden)
                {
                    GVector3 panTarget = visualNode.GlobalPosition;
                    if (targetEntity != Entity.Null
                        && EntityViewModel.EntityVisualNodes.TryGetValue(targetEntity.Id, out var targetNode)
                        && Node.IsInstanceValid(targetNode))
                    {
                        panTarget = targetNode.GlobalPosition;
                    }
                    camera.Call("pan_to", panTarget, ZoomInDuration);
                    zoomTween.TweenProperty(camera, "size", CloseZoom, ZoomInDuration)
                        .SetTrans(Tween.TransitionType.Sine)
                        .SetEase(Tween.EaseType.InOut);
                }
                else
                {
                    camera.Call("release_override", ZoomOutDuration);
                    waitingToZoomOut = true;
                    zoomTween.TweenInterval(ZoomOutDelay);
                    zoomTween.Chain().TweenCallback(Callable.From(() => { waitingToZoomOut = false; zoomingOut = true; }));
                    zoomTween.Chain().TweenProperty(camera, "size", NormalZoom, ZoomOutDuration)
                        .SetTrans(Tween.TransitionType.Sine)
                        .SetEase(Tween.EaseType.InOut);
                    zoomTween.Chain().TweenCallback(Callable.From(() => { zoomingOut = false; }));
                }
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        // Skip the post-hide delay when the player starts moving, but don't interrupt
        // an active zoom-out.
        velocity.Subscribe(v =>
        {
            Callable.From(() =>
            {
                if (!Node.IsInstanceValid(camera)) return;
                if ((float)v.X == 0f && (float)v.Y == 0f) return;
                if (!waitingToZoomOut) return;

                zoomTween?.Kill();
                waitingToZoomOut = false;
                zoomingOut = true;
                zoomTween = camera.CreateTween();
                zoomTween.TweenProperty(camera, "size", NormalZoom, ZoomOutDuration)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.InOut);
                zoomTween.Chain().TweenCallback(Callable.From(() => { zoomingOut = false; }));
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }
}
