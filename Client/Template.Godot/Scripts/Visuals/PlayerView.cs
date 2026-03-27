using Godot;
using R3;
using Template.Godot.Core;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

public partial class PlayerView
{
    partial void OnSpawned(PlayerViewModel vm, Node3D visualNode)
    {
        if (GameManager.Instance.LocalPlayerId != vm.Entity.Id)
            visualNode.GetNode<Camera3D>("Camera").QueueFree();

        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.Player.CharacterBody2D.Velocity, flipPivot, characterNode);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }
}
