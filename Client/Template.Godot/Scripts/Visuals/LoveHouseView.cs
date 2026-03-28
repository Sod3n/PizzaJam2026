namespace Template.Godot.Visuals;

public partial class LoveHouseView
{
    partial void OnSpawned(LoveHouseViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }
}
