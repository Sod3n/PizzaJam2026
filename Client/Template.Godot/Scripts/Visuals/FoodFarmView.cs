namespace Template.Godot.Visuals;

public partial class FoodFarmView
{
    partial void OnSpawned(FoodFarmViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }
}
