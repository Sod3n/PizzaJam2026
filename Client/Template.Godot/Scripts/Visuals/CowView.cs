using Godot;
using R3;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class CowView
{
    private static readonly Texture2D _heartSprite =
        GD.Load<Texture2D>("res://sprites/heart.png");

    partial void OnSpawned(CowViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.Cow.CharacterBody2D.RealVelocity, flipPivot, characterNode, invertFlip: true);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        // Wanderer cows (no house, not following) keep bounce but disable sway
        vm.Cow.Cow.HouseId.CombineLatest(vm.Cow.Cow.FollowingPlayer, (houseId, followingPlayer) =>
            houseId == Entity.Null && followingPlayer == Entity.Null
        ).Subscribe(isUnassigned =>
        {
            Callable.From(() =>
            {
                if (characterNode != null && IsInstanceValid(characterNode))
                    characterNode.SetDeferred("enable_idle_sway", !isUnassigned);
            }).CallDeferred();
        }).AddTo(vm.Disposables);

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

        // Heart icon — visible above cow when it is someone's love target
        var heartIcon = new Sprite3D();
        heartIcon.Texture = _heartSprite;
        heartIcon.PixelSize = 0.005f;
        heartIcon.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        heartIcon.AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass;
        heartIcon.Shaded = false;
        heartIcon.Position = new Vector3(0, 3.0f, 0);
        heartIcon.NoDepthTest = true;
        heartIcon.RenderPriority = 4;
        heartIcon.Visible = false;
        visualNode.AddChild(heartIcon);

        // Check if any cow in the world has this cow as its LoveTarget
        // We poll via a timer since we need to check all cows, not just this one
        UpdateHeartVisibility(vm.Entity, heartIcon);
        var heartTimer = new Timer();
        heartTimer.WaitTime = 0.5f;
        heartTimer.Autostart = true;
        heartTimer.Timeout += () => UpdateHeartVisibility(vm.Entity, heartIcon);
        visualNode.AddChild(heartTimer);

        // Love popup — when this cow is interacted with as a love cow, show the popup
        ReactiveSystem.Instance.ObserveAdd<EnterStateComponent>()
            .Where(x => x == vm.Entity && ReactiveSystem.Instance.BoundState != null
                && ReactiveSystem.Instance.BoundState.GetComponent<EnterStateComponent>(x).Key == StateKeys.LoveCow)
            .Subscribe(x =>
            {
                var param = ReactiveSystem.Instance.BoundState.GetComponent<EnterStateComponent>(x).Param;
                var targetName = string.IsNullOrEmpty(param.ToString()) ? "???" : param.ToString();
                Callable.From(() => LovePopupOverlay.Show(GetTree(), vm.Entity, targetName)).CallDeferred();
            }).AddTo(vm.Disposables);
    }

    private static void UpdateHeartVisibility(Entity thisEntity, Sprite3D heartIcon)
    {
        if (!Node.IsInstanceValid(heartIcon)) return;
        var reactiveState = ReactiveSystem.Instance.BoundState;
        if (reactiveState == null) { heartIcon.Visible = false; return; }

        // Check if any cow has this cow as its love target AND is following a player (the lover is active)
        bool isLoveTarget = false;
        foreach (var cowEntity in reactiveState.Filter<CowComponent>())
        {
            var cow = reactiveState.GetComponent<CowComponent>(cowEntity);
            if (cow.LoveTarget == thisEntity && cow.FollowingPlayer != Entity.Null)
            {
                isLoveTarget = true;
                break;
            }
        }
        heartIcon.Visible = isLoveTarget;
    }

    partial void OnDespawned(CowViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
