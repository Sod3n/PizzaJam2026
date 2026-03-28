namespace Template.Godot.Visuals;

public partial class FoodSignView
{
    partial void OnSpawned(FoodSignViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }
}
