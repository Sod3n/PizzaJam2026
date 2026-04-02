namespace Template.Godot.Visuals;

public partial class DecorationView
{
    partial void OnSpawned(DecorationViewModel vm, global::Godot.Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }

    partial void OnDespawned(DecorationViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
