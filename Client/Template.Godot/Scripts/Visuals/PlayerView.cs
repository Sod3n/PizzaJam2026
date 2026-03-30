using Godot;
using R3;
using Template.Godot.Core;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

public partial class PlayerView
{
    private const float NormalZoom = 20f;
    private const float CloseZoom = 10f;
    private const float ZoomInDuration = 0.5f;
    private const float ZoomOutDelay = 1.0f;
    private const float ZoomOutDuration = 0.8f;

    partial void OnSpawned(PlayerViewModel vm, Node3D visualNode)
    {
        if (GameManager.Instance.LocalPlayerId != vm.Entity.Id)
            visualNode.GetNode<Camera3D>("Camera").QueueFree();

        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.Player.CharacterBody2D.Velocity, flipPivot, characterNode);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        // Zoom camera when player is hidden (milking, breeding)
        var camera = visualNode.GetNodeOrNull<Camera3D>("Camera");
        if (camera != null)
        {
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
