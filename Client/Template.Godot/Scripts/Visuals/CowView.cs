using Godot;

namespace Template.Godot.Visuals;

public partial class CowView
{
    partial void OnSpawned(CowViewModel vm, Node3D visualNode)
    {
        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.Cow.CharacterBody2D.Velocity, flipPivot, characterNode);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }
}
