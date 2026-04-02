namespace Template.Godot.Visuals;

public partial class CarrotFarmView
{
    partial void OnSpawned(CarrotFarmViewModel vm, global::Godot.Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }

    partial void OnDespawned(CarrotFarmViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
