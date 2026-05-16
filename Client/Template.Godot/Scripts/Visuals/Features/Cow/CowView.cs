using Godot;
using R3;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared;
using Template.Shared.Components;
using Template.Godot.Twitch;

namespace Template.Godot.Visuals;

public partial class CowView
{
    private static readonly Texture2D _heartSprite =
        GD.Load<Texture2D>("res://sprites/heart.png");

    private static readonly Shader _heartFillShader =
        GD.Load<Shader>("res://shaders/heart_fill.gdshader");

    partial void OnSpawned(CowViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.Cow.CharacterBody2D.RealVelocity, flipPivot, characterNode, invertFlip: true);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode, animateNode: characterNode);

        // Twitch integration: try to assign a chatter name to this cow
        TwitchIntegration.TryAssignChatterName(vm.Entity);

        vm.Cow.Cow.IsWanderer.Subscribe(isWanderer =>
            Callable.From(() =>
            {
                if (characterNode != null && IsInstanceValid(characterNode))
                    characterNode.SetDeferred("enable_idle_sway", !isWanderer);
            }).CallDeferred()
        ).AddTo(vm.Disposables);

        // Show breed result overlay for newly-born cows (tagged server-side with BreedBornComponent)
        var state = ReactiveSystem.Instance.BoundState;
        if (state != null && state.HasComponent<BreedBornComponent>(vm.Entity))
            Callable.From(() => BreedResultOverlay.ShowForCow(GetTree(), vm, visualNode)).CallDeferred();

        // Depression indicator — visible above cow while depressed
        var depressionIcon = new Label3D();
        depressionIcon.Text = "zzZ";
        depressionIcon.FontSize = 96;
        depressionIcon.Modulate = new Color(0.6f, 0.6f, 1f, 0.9f);
        depressionIcon.OutlineModulate = new Color(0.2f, 0.2f, 0.5f, 1f);
        depressionIcon.Position = new Vector3(0, 2.5f, 0);
        depressionIcon.NoDepthTest = true;
        depressionIcon.RenderPriority = 3;
        depressionIcon.OutlineRenderPriority = 2;
        depressionIcon.Visible = false;
        visualNode.AddChild(depressionIcon);

        vm.Cow.Cow.IsDepressed.Subscribe(depressed =>
        {
            Callable.From(() =>
            {
                if (IsInstanceValid(depressionIcon))
                    depressionIcon.Visible = depressed;
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        // Heart icon — visible above cow when it is part of a love pair (either lover or target)
        var heartIcon = new Sprite3D();
        heartIcon.Texture = _heartSprite;
        heartIcon.PixelSize = 0.0005f;
        heartIcon.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        heartIcon.AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass;
        heartIcon.Shaded = false;
        heartIcon.Position = new Vector3(0, 3.0f, 0);
        heartIcon.NoDepthTest = true;
        heartIcon.RenderPriority = 4;
        heartIcon.Visible = false;
        visualNode.AddChild(heartIcon);

        // Need icon — visible above love cow that hasn't confessed yet (wants player interaction)
        var needIcon = new Label3D();
        needIcon.Text = "!";
        needIcon.FontSize = 128;
        needIcon.Modulate = new Color(1f, 0.3f, 0.5f, 1f);
        needIcon.OutlineModulate = new Color(0.5f, 0f, 0.2f, 1f);
        needIcon.Position = new Vector3(0, 3.5f, 0);
        needIcon.NoDepthTest = true;
        needIcon.RenderPriority = 5;
        needIcon.OutlineRenderPriority = 4;
        needIcon.Visible = false;
        visualNode.AddChild(needIcon);

        vm.Cow.Cow.IsLoveTarget.Subscribe(_ =>
            Callable.From(() =>
            {
                if (!IsInstanceValid(heartIcon)) return;
                heartIcon.Visible = vm.Cow.Cow.IsLoveTarget.CurrentValue;
            }).CallDeferred()
        ).AddTo(vm.Disposables);

        vm.Cow.Cow.ShowLoveNeedIcon.Subscribe(show =>
            Callable.From(() =>
            {
                if (IsInstanceValid(needIcon)) needIcon.Visible = show;
            }).CallDeferred()
        ).AddTo(vm.Disposables);

        var followAnchor = visualNode.GetNodeOrNull<Node3D>("%FollowCircleScaleAnchor");
        var followCircle = visualNode.GetNodeOrNull<Node3D>("%FollowCircle");
        if (followAnchor != null) followAnchor.Scale = Vector3.Zero;
        if (followCircle != null) FollowCircleSpinner.Spin(followCircle);
        if (followAnchor != null)
        {
            vm.Cow.Cow.FollowingPlayer.Subscribe(target =>
                Callable.From(() => TweenAnchorScale(followAnchor, target > 0)).CallDeferred()
            ).AddTo(vm.Disposables);
        }

        var hornyIcon = visualNode.GetNodeOrNull<Sprite3D>("HornyHeart");
        if (hornyIcon != null)
        {
            var sharedMat = hornyIcon.MaterialOverride as ShaderMaterial;
            var hornyMaterial = sharedMat != null
                ? (ShaderMaterial)sharedMat.Duplicate()
                : new ShaderMaterial { Shader = _heartFillShader };
            hornyIcon.MaterialOverride = hornyMaterial;
            hornyMaterial.SetShaderParameter("fill", 0f);

            var pulseState = new HornyIconPulseState();

            void UpdateFill()
            {
                int max = vm.Cow.Cow.MaxHorny.CurrentValue;
                int h = vm.Cow.Cow.Horny.CurrentValue;
                hornyMaterial.SetShaderParameter("fill", max > 0 ? h / (float)max : 0f);
            }

            vm.Cow.Cow.Horny.Subscribe(_ =>
                Callable.From(() => { if (IsInstanceValid(hornyIcon)) UpdateFill(); }).CallDeferred()
            ).AddTo(vm.Disposables);
            vm.Cow.Cow.MaxHorny.Subscribe(_ =>
                Callable.From(() => { if (IsInstanceValid(hornyIcon)) UpdateFill(); }).CallDeferred()
            ).AddTo(vm.Disposables);

            vm.Cow.Cow.HornyIconState.Subscribe(iconState =>
                Callable.From(() =>
                {
                    if (!IsInstanceValid(hornyIcon)) return;
                    hornyIcon.Visible = iconState != HornyIconState.None;
                    hornyIcon.Modulate = iconState == HornyIconState.Exhausted
                        ? new Color(0.4f, 0.5f, 0.9f)
                        : new Color(1, 1, 1);

                    bool attacking = iconState == HornyIconState.Attacking;
                    if (attacking && !pulseState.IsAttacking)
                    {
                        pulseState.IsAttacking = true;
                        pulseState.BaseScale = hornyIcon.Scale;
                        pulseState.PulseTween?.Kill();
                        var tw = hornyIcon.CreateTween();
                        tw.SetLoops();
                        tw.TweenProperty(hornyIcon, "scale", pulseState.BaseScale * 1.25f, 0.25f);
                        tw.TweenProperty(hornyIcon, "scale", pulseState.BaseScale, 0.25f);
                        pulseState.PulseTween = tw;
                    }
                    else if (!attacking && pulseState.IsAttacking)
                    {
                        pulseState.IsAttacking = false;
                        pulseState.PulseTween?.Kill();
                        pulseState.PulseTween = null;
                        hornyIcon.Scale = pulseState.BaseScale;
                    }
                }).CallDeferred()
            ).AddTo(vm.Disposables);
        }

        ReactiveSystem.Instance.ObserveAdd<EnterStateComponent>()
            .Where(x => x == vm.Entity && ReactiveSystem.Instance.BoundState != null
                && ReactiveSystem.Instance.BoundState.GetComponent<EnterStateComponent>(x).Key == StateKeys.CowAttack)
            .Subscribe(_ =>
            {
                Callable.From(() =>
                {
                    GD.Print($"[CowView] CowAttack entered for entity {vm.Entity.Id}");
                }).CallDeferred();
            }).AddTo(vm.Disposables);

        var jumpTweens = new JumpTweens();

        ReactiveSystem.Instance.ObserveAdd<EnterStateComponent>()
            .Where(x => x == vm.Entity && ReactiveSystem.Instance.BoundState != null
                && ReactiveSystem.Instance.BoundState.GetComponent<EnterStateComponent>(x).Key == StateKeys.CowJumpWindup)
            .Subscribe(_ =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(characterNode)) return;
                    characterNode.SetDeferred("enable_bounce", false);
                    KillJumpTweens(jumpTweens);
                    var tw = characterNode.CreateTween();
                    tw.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
                    tw.TweenProperty(characterNode, "scale", new Vector3(1.4f, 0.5f, 1.4f), 0.3f);
                    jumpTweens.Scale = tw;
                }).CallDeferred();
            }).AddTo(vm.Disposables);

        // Windup disables bounce + leaves characterNode mid-squish; the leap tween
        // restores scale but bounce stays off, and a catch can pin the cow before the
        // tween finishes. Always reset both when CowJumpComponent goes away (catch end,
        // attack cancel, etc.) so the cow doesn't sit squished back at its house.
        ReactiveSystem.Instance.ObserveRemove<CowJumpComponent>()
            .Where(x => x == vm.Entity)
            .Subscribe(_ =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(characterNode) || !IsInstanceValid(visualNode)) return;
                    KillJumpTweens(jumpTweens);
                    var existing = characterNode.CreateTween();
                    existing.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
                    existing.TweenProperty(characterNode, "scale", Vector3.One, 0.1f);
                    jumpTweens.Scale = existing;
                    characterNode.SetDeferred("enable_bounce", true);
                }).CallDeferred();
            }).AddTo(vm.Disposables);

        ReactiveSystem.Instance.ObserveAdd<EnterStateComponent>()
            .Where(x => x == vm.Entity && ReactiveSystem.Instance.BoundState != null
                && ReactiveSystem.Instance.BoundState.GetComponent<EnterStateComponent>(x).Key == StateKeys.CowJumpLeap)
            .Subscribe(_ =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(characterNode) || !IsInstanceValid(visualNode)) return;
                    KillJumpTweens(jumpTweens);
                    var scaleTw = characterNode.CreateTween();
                    scaleTw.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
                    scaleTw.TweenProperty(characterNode, "scale", new Vector3(0.7f, 1.6f, 0.7f), 0.12f);
                    scaleTw.TweenProperty(characterNode, "scale", Vector3.One, 0.18f);

                    var arcTw = visualNode.CreateTween();
                    arcTw.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
                    arcTw.TweenProperty(visualNode, "position:y", visualNode.Position.Y + 1.8f, 0.15f);
                    arcTw.Chain().SetEase(Tween.EaseType.In)
                        .TweenProperty(visualNode, "position:y", visualNode.Position.Y, 0.15f);
                    jumpTweens.Scale = scaleTw;
                    jumpTweens.Arc = arcTw;
                }).CallDeferred();
            }).AddTo(vm.Disposables);

        // Love popup — when this cow is interacted with as a love cow, show the popup
        ReactiveSystem.Instance.ObserveAdd<EnterStateComponent>()
            .Where(x => x == vm.Entity && ReactiveSystem.Instance.BoundState != null
                && ReactiveSystem.Instance.BoundState.GetComponent<EnterStateComponent>(x).Key == StateKeys.LoveCow)
            .Subscribe(x =>
            {
                // Try to resolve the target name via Twitch override (using LoveTarget entity)
                var rState = ReactiveSystem.Instance.BoundState;
                string targetName = "???";
                if (rState != null && rState.HasComponent<CowComponent>(vm.Entity))
                {
                    var loveTarget = rState.GetComponent<CowComponent>(vm.Entity).LoveTarget;
                    if (loveTarget != Entity.Null)
                        targetName = TwitchIntegration.GetDisplayName(loveTarget);
                }
                if (targetName == "???")
                {
                    var param = rState?.GetComponent<EnterStateComponent>(x).Param;
                    if (param != null && !string.IsNullOrEmpty(param.ToString()))
                        targetName = param.ToString();
                }
                Callable.From(() => LovePopupOverlay.Show(GetTree(), vm.Entity, targetName)).CallDeferred();
            }).AddTo(vm.Disposables);
    }

    private static void TweenAnchorScale(Node3D anchor, bool show)
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

    private sealed class JumpTweens
    {
        public Tween Scale;
        public Tween Arc;
    }

    private static void KillJumpTweens(JumpTweens t)
    {
        if (t.Scale != null && t.Scale.IsValid()) t.Scale.Kill();
        if (t.Arc != null && t.Arc.IsValid()) t.Arc.Kill();
        t.Scale = null;
        t.Arc = null;
    }

    private sealed class HornyIconPulseState
    {
        public bool IsAttacking;
        public Tween PulseTween;
        public Vector3 BaseScale = Vector3.One;
    }

    partial void OnDespawned(CowViewModel vm, Node3D visualNode)
    {
        // Clean up Twitch name override for this entity
        TwitchIntegration.RemoveNameOverride(vm.Entity.Id);

        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
