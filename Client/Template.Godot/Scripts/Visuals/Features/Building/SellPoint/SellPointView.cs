namespace Template.Godot.Visuals;

public partial class SellPointView
{
    partial void OnSpawned(SellPointViewModel vm, global::Godot.Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        // Static entity — no position tween needed
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }

    partial void OnDespawned(SellPointViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
