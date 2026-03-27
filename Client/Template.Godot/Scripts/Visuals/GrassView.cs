namespace Template.Godot.Visuals;

public partial class GrassView
{
    partial void OnSpawned(GrassViewModel vm, global::Godot.Node3D visualNode)
    {
        // Static entity — no position tween needed
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }
}
