using Godot;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class HelperPetView
{
    partial void OnSpawned(HelperPetViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.HelperPet.CharacterBody2D.Velocity, flipPivot, characterNode);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        // Show breed result overlay for newly-spawned pets
        var state = ReactiveSystem.Instance.BoundState;
        if (state != null && state.HasComponent<BreedBornComponent>(vm.Entity))
            Callable.From(() => BreedResultOverlay.ShowForPet(GetTree(), vm, visualNode)).CallDeferred();
    }

    partial void OnDespawned(HelperPetViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
