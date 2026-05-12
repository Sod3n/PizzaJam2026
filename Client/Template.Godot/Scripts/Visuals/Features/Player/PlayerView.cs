using System;
using Godot;
using R3;
using Template.Godot.Core;
using Template.Shared.Components;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

public partial class PlayerView
{
    private const float NormalZoom = 20f;
    private const float CloseZoom = 10f;
    private const float ZoomInDuration = 0.5f;
    private const float ZoomOutDelay = 1.0f;
    private const float ZoomOutDuration = 0.8f;

    partial void GetEntityFilter(ref Func<Entity, bool> filter)
    {
        var state = GameManager.Instance?.Game?.State;
        if (state == null) return;
        filter = entity => !state.HasComponent<HelperPlayerComponent>(entity);
    }

    partial void OnSpawned(PlayerViewModel vm, Node3D visualNode)
    {
        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.Player.CharacterBody2D.Velocity, flipPivot, characterNode);

        // ViewSmoother does inter-tick linear interpolation for all entities,
        // so the tau parameter is a no-op now. The local-player and remote-player
        // paths follow the same code; the second Track call is harmless re-attachment
        // when local player resolution races spawn — kept for ordering reasons.
        IDisposable currentPositionTracker = ViewSmoothingManager.Smoother.TrackPosition3D(vm.Entity, visualNode);
        vm.Disposables.Add(Disposable.Create(() => currentPositionTracker?.Dispose()));
        GameManager.Instance.LocalPlayerIdReactive.Subscribe(localId =>
        {
            if (localId != vm.Entity.Id) return;
            Callable.From(() =>
            {
                currentPositionTracker?.Dispose();
                currentPositionTracker = ViewSmoothingManager.Smoother.TrackPosition3D(vm.Entity, visualNode);
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        // Zoom camera when player is hidden (milking, breeding)
        var camera = visualNode.GetNodeOrNull<Camera3D>("Camera");
        if (camera != null)
        {
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
            vm.IsHidden.Subscribe(hidden =>
            {
                Callable.From(() =>
                {
                    if (!Node.IsInstanceValid(camera)) return;
                    zoomTween?.Kill();
                    zoomingOut = false;
                    waitingToZoomOut = false;
                    zoomTween = camera.CreateTween();
                    if (hidden)
                    {
                        zoomTween.TweenProperty(camera, "size", CloseZoom, ZoomInDuration)
                            .SetTrans(Tween.TransitionType.Sine)
                            .SetEase(Tween.EaseType.InOut);
                    }
                    else
                    {
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

            // Skip the delay if player starts moving, but don't interrupt an active zoom-out
            vm.Player.CharacterBody2D.Velocity.Subscribe(v =>
            {
                Callable.From(() =>
                {
                    if (!Node.IsInstanceValid(camera)) return;
                    if ((float)v.X == 0f && (float)v.Y == 0f) return;
                    if (!waitingToZoomOut) return;

                    // Player moved during delay — skip delay, start zoom-out immediately
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
}
