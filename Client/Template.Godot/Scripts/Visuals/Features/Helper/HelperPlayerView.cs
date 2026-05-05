using System;
using Godot;
using R3;
using Template.Godot.Core;
using Template.Shared.Components;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

public partial class HelperPlayerView
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
        filter = entity => state.HasComponent<HelperPlayerComponent>(entity);
    }

    partial void OnSpawned(HelperPlayerViewModel vm, Node3D visualNode)
    {
        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.HelperPlayer.CharacterBody2D.Velocity, flipPivot, characterNode);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

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

            vm.HelperPlayer.CharacterBody2D.Velocity.Subscribe(v =>
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

    partial void OnDespawned(HelperPlayerViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
