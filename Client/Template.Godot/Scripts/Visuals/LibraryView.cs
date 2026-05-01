using Godot;
using R3;
using Template.Godot.Core;

namespace Template.Godot.Visuals;

public partial class LibraryView
{
    partial void OnSpawned(LibraryViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        vm.OnInteract.Subscribe(_ =>
        {
            Callable.From(() =>
            {
                if (FamilyTreeOverlay.IsActive) return;
                FamilyTreeOverlay.Toggle(GetTree());
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(LibraryViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
