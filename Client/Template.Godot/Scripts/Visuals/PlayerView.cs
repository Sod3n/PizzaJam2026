using Godot;
using R3;
using Template.Godot.Core;
using Template.Shared.Components;
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

    partial void OnSpawned(PlayerViewModel vm, Node3D visualNode)
    {
        if (GameManager.Instance.LocalPlayerId != vm.Entity.Id)
            visualNode.GetNode<Camera3D>("Camera").QueueFree();

        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.Player.CharacterBody2D.Velocity, flipPivot, characterNode);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var state = ReactiveSystem.Instance.BoundState;
        if (state != null && state.HasComponent<HelperPlayerComponent>(vm.Entity))
            SetupHelperPlayerBagUI(vm, visualNode);

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

    private static void SetupHelperPlayerBagUI(PlayerViewModel vm, Node3D visualNode)
    {
        var bagLabel = new Label3D
        {
            Text = "",
            FontSize = 64,
            Modulate = Colors.White,
            OutlineModulate = Colors.Black,
            Position = new GVector3(0, 3.0f, 0),
            NoDepthTest = true,
            RenderPriority = 4,
            OutlineRenderPriority = 3,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        visualNode.AddChild(bagLabel);

        var roleLabel = new Label3D
        {
            Text = "",
            FontSize = 48,
            Modulate = new Color(0.85f, 0.95f, 1f, 1f),
            OutlineModulate = Colors.Black,
            Position = new GVector3(0, 3.6f, 0),
            NoDepthTest = true,
            RenderPriority = 4,
            OutlineRenderPriority = 3,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        visualNode.AddChild(roleLabel);

        var timer = new Timer { WaitTime = 0.2f, Autostart = true };
        timer.Timeout += () =>
        {
            if (!Node.IsInstanceValid(bagLabel) || !Node.IsInstanceValid(roleLabel)) return;
            var rs = ReactiveSystem.Instance.BoundState;
            if (rs == null || !rs.HasComponent<HelperPlayerComponent>(vm.Entity)) return;
            var hp = rs.GetComponent<HelperPlayerComponent>(vm.Entity);
            roleLabel.Text = RoleName(hp.Type);
            bagLabel.Text = FormatBag(hp);
        };
        visualNode.AddChild(timer);
    }

    private static string RoleName(int type) => type switch
    {
        HelperType.Gatherer => "Gatherer",
        HelperType.Seller => "Seller",
        HelperType.Builder => "Builder",
        HelperType.Milker => "Milker",
        HelperType.Assistant => "Assistant",
        _ => "?",
    };

    private static string FormatBag(HelperPlayerComponent hp)
    {
        var sb = new System.Text.StringBuilder();
        if (hp.BagGrass > 0) sb.Append($"G{hp.BagGrass} ");
        if (hp.BagCarrot > 0) sb.Append($"C{hp.BagCarrot} ");
        if (hp.BagApple > 0) sb.Append($"A{hp.BagApple} ");
        if (hp.BagMushroom > 0) sb.Append($"M{hp.BagMushroom} ");
        if (hp.BagMilk > 0) sb.Append($"Mk{hp.BagMilk} ");
        if (hp.BagCoins > 0) sb.Append($"${hp.BagCoins} ");
        if (sb.Length == 0) return $"[{hp.GetBagTotal()}/{hp.BagCapacity}]";
        return $"{sb.ToString().TrimEnd()} [{hp.GetBagTotal()}/{hp.BagCapacity}]";
    }
}
