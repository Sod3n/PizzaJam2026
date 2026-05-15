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
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        // Twitch integration: try to assign a chatter name to this cow
        TwitchIntegration.TryAssignChatterName(vm.Entity);

        // Wanderer cows (no house, not following) keep bounce but disable sway.
        // Use a single polling observer so both fields are read atomically in the
        // same tick, avoiding a transient isUnassigned=true when CombineLatest
        // would fire for HouseId before FollowingPlayer's observer has polled.
        var cowEntity = vm.Entity;
        ReactiveSystem.Instance.Subscribe(
            () =>
            {
                var s = ReactiveSystem.Instance.BoundState;
                if (s == null || !s.HasComponent<CowComponent>(cowEntity)) return false;
                var cow = s.GetComponent<CowComponent>(cowEntity);
                return cow.HouseId == Entity.Null && cow.FollowingPlayer == Entity.Null;
            },
            isUnassigned =>
            {
                Callable.From(() =>
                {
                    if (characterNode != null && IsInstanceValid(characterNode))
                        characterNode.SetDeferred("enable_idle_sway", !isUnassigned);
                }).CallDeferred();
            }
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

        // Poll heart and need icon visibility via a timer
        UpdateLoveIcons(vm.Entity, heartIcon, needIcon);
        var heartTimer = new Timer();
        heartTimer.WaitTime = 0.5f;
        heartTimer.Autostart = true;
        heartTimer.Timeout += () => UpdateLoveIcons(vm.Entity, heartIcon, needIcon);
        visualNode.AddChild(heartTimer);

        var hornyIcon = visualNode.GetNodeOrNull<Sprite3D>("HornyHeart");
        if (hornyIcon != null)
        {
            var sharedMat = hornyIcon.MaterialOverride as ShaderMaterial;
            var hornyMaterial = sharedMat != null
                ? (ShaderMaterial)sharedMat.Duplicate()
                : new ShaderMaterial { Shader = _heartFillShader };
            hornyIcon.MaterialOverride = hornyMaterial;
            hornyMaterial.SetShaderParameter("fill", 0f);

            var hornyState = new HornyIconState();
            UpdateHornyIcon(vm.Entity, hornyIcon, hornyMaterial, hornyState);
            var hornyTimer = new Timer();
            hornyTimer.WaitTime = 0.05f;
            hornyTimer.Autostart = true;
            hornyTimer.Timeout += () => UpdateHornyIcon(vm.Entity, hornyIcon, hornyMaterial, hornyState);
            visualNode.AddChild(hornyTimer);
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

    private sealed class HornyIconState
    {
        public bool IsAttacking;
        public Tween PulseTween;
        public Vector3 BaseScale = Vector3.One;
    }

    private static void UpdateHornyIcon(Entity thisEntity, Sprite3D hornyIcon, ShaderMaterial mat, HornyIconState state)
    {
        if (!Node.IsInstanceValid(hornyIcon)) return;
        var s = ReactiveSystem.Instance.BoundState;
        if (s == null || !s.HasComponent<CowComponent>(thisEntity))
        {
            hornyIcon.Visible = false;
            return;
        }
        var cow = s.GetComponent<CowComponent>(thisEntity);
        if (cow.MaxHorny <= 0)
        {
            hornyIcon.Visible = false;
            return;
        }
        float fill = cow.Horny / (float)cow.MaxHorny;
        mat.SetShaderParameter("fill", fill);
        hornyIcon.Visible = cow.Horny > 0 || cow.IsExhausted;
        hornyIcon.Modulate = cow.IsExhausted
            ? new Color(0.4f, 0.5f, 0.9f)
            : new Color(1, 1, 1);

        if (cow.IsAttacking && !state.IsAttacking)
        {
            state.IsAttacking = true;
            state.BaseScale = hornyIcon.Scale;
            state.PulseTween?.Kill();
            var tw = hornyIcon.CreateTween();
            tw.SetLoops();
            tw.TweenProperty(hornyIcon, "scale", state.BaseScale * 1.25f, 0.25f);
            tw.TweenProperty(hornyIcon, "scale", state.BaseScale, 0.25f);
            state.PulseTween = tw;
        }
        else if (!cow.IsAttacking && state.IsAttacking)
        {
            state.IsAttacking = false;
            state.PulseTween?.Kill();
            state.PulseTween = null;
            hornyIcon.Scale = state.BaseScale;
        }
    }

    private static void UpdateLoveIcons(Entity thisEntity, Sprite3D heartIcon, Label3D needIcon)
    {
        if (!Node.IsInstanceValid(heartIcon)) return;
        if (!Node.IsInstanceValid(needIcon)) return;
        var reactiveState = ReactiveSystem.Instance.BoundState;
        if (reactiveState == null) { heartIcon.Visible = false; needIcon.Visible = false; return; }

        bool isLoveTarget = false;
        bool isLover = false;
        bool showNeedIcon = false;

        // Check if this cow itself has a LoveTarget (meaning it IS the lover)
        if (reactiveState.HasComponent<CowComponent>(thisEntity))
        {
            var thisCow = reactiveState.GetComponent<CowComponent>(thisEntity);
            if (thisCow.LoveTarget != Entity.Null)
            {
                isLover = true;
                // Show need icon if following a player but hasn't confessed yet
                if (thisCow.FollowingPlayer != Entity.Null && !thisCow.LoveConfessed)
                    showNeedIcon = true;
            }
        }

        // Check if any other cow has this cow as its LoveTarget (meaning this cow is the target)
        foreach (var cowEntity in reactiveState.Filter<CowComponent>())
        {
            if (cowEntity == thisEntity) continue;
            var cow = reactiveState.GetComponent<CowComponent>(cowEntity);
            if (cow.LoveTarget == thisEntity)
            {
                isLoveTarget = true;
                break;
            }
        }

        // Show heart on both the lover and the target
        heartIcon.Visible = isLoveTarget || isLover;
        needIcon.Visible = showNeedIcon;
    }

    partial void OnDespawned(CowViewModel vm, Node3D visualNode)
    {
        // Clean up Twitch name override for this entity
        TwitchIntegration.RemoveNameOverride(vm.Entity.Id);

        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
