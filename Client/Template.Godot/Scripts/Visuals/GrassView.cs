using Godot;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class GrassView
{
    [Export] public PackedScene CarrotPrefab;
    [Export] public PackedScene ApplePrefab;
    [Export] public PackedScene MushroomPrefab;

    private PackedScene GetPrefabForFoodType(int foodType) => foodType switch
    {
        FoodType.Carrot => CarrotPrefab,
        FoodType.Apple => ApplePrefab,
        FoodType.Mushroom => MushroomPrefab,
        _ => null
    };

    partial void OnSpawned(GrassViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;

        var foodType = vm.Grass.Grass.FoodType.CurrentValue;
        var altPrefab = GetPrefabForFoodType(foodType);
        if (altPrefab != null)
        {
            // Replace default visual with food-specific scene
            var replacement = altPrefab.Instantiate<Node3D>();
            replacement.Position = visualNode.Position;
            visualNode.GetParent().AddChild(replacement);
            _spawnedEntities[vm] = replacement;
            visualNode.QueueFree();
            visualNode = replacement;
        }

        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }

    partial void OnDespawned(GrassViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
