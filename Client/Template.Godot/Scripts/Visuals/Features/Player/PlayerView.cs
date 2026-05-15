using System;
using Godot;
using R3;
using Template.Godot.Core;
using Template.Shared.Components;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;

namespace Template.Godot.Visuals;

public partial class PlayerView
{
    partial void GetEntityFilter(ref Func<Entity, bool> filter)
    {
        var state = GameManager.Instance?.Game?.State;
        if (state == null) return;
        filter = entity => !state.HasComponent<HelperPlayerComponent>(entity);
    }

    partial void OnSpawned(PlayerViewModel vm, Node3D visualNode)
    {
        PlayerSharedSetup.Setup(vm, visualNode, vm.IsHidden, vm.Player.CharacterBody2D.Velocity);

        vm.OnInteract
            .Where(p => p == "caught_tap")
            .Subscribe(_ =>
            {
                Callable.From(() =>
                {
                    if (!Node.IsInstanceValid(visualNode)) return;
                    var state = ReactiveSystem.Instance?.BoundState;
                    if (state == null || !state.HasComponent<CaughtComponent>(vm.Entity)) return;
                    var caught = state.GetComponent<CaughtComponent>(vm.Entity);
                    if (caught.CowEntity == Entity.Null) return;
                    if (!EntityViewModel.EntityVisualNodes.TryGetValue(caught.CowEntity.Id, out var cowNode)
                        || !Node.IsInstanceValid(cowNode)) return;
                    var mid = (visualNode.GlobalPosition + cowNode.GlobalPosition) * 0.5f;
                    ViewHelpers.SpawnHeartsAtWorld(visualNode.GetTree().Root, mid, halfArc: 1f, baseHalfWidth: 1.2f);
                }).CallDeferred();
            }).AddTo(vm.Disposables);
    }
}
