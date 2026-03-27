namespace Template.Godot.Visuals;

public partial class SellPointView
{
    partial void OnSpawned(SellPointViewModel vm, global::Godot.Node3D visualNode)
    {
        // Static entity — no position tween needed
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }
}
