using System;
using Godot;
using Template.Godot.Core;
using Template.Shared.Components;
using Deterministic.GameFramework.ECS;

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
    }
}
