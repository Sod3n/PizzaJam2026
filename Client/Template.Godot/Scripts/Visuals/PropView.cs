using Godot;

namespace Template.Godot.Visuals;

public partial class PropView
{
    [Export] public PackedScene BarrelScene;
    [Export] public PackedScene Bush1Scene;
    [Export] public PackedScene Bush2Scene;
    [Export] public PackedScene FlowersScene;
    [Export] public PackedScene TreeScene;

    private PackedScene GetSceneForPropType(int propType)
    {
        return propType switch
        {
            0 => BarrelScene,
            1 => Bush1Scene,
            2 => Bush2Scene,
            3 => FlowersScene,
            4 => TreeScene,
            _ => Prefab
        };
    }

    partial void OnSpawned(PropViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        var propType = vm.Prop.Prop.PropType.CurrentValue;
        var scene = GetSceneForPropType(propType);

        if (scene != null && scene != Prefab)
        {
            // Replace the default prefab instance with the type-specific scene
            var replacement = scene.Instantiate<Node3D>();
            var pos = vm.Transform.Position.CurrentValue;
            replacement.Position = new Vector3((float)pos.X, 0f, (float)pos.Y);
            var parent = visualNode.GetParent();
            parent.AddChild(replacement);
            visualNode.QueueFree();
            _spawnedEntities[vm] = replacement;
            ViewHelpers.SetupInteractAnimation(vm, replacement);
        }
        else
        {
            ViewHelpers.SetupInteractAnimation(vm, visualNode);
        }
    }

    partial void OnDespawned(PropViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
