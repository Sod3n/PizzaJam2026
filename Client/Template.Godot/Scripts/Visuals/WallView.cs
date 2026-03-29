namespace Template.Godot.Visuals;

public partial class WallView
{
    partial void OnSpawned(WallViewModel vm, global::Godot.Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        // Static entity — no position tween needed
    }

    partial void OnDespawned(WallViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
