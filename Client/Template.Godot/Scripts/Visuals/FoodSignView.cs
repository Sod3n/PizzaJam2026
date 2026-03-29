using Godot;
using R3;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class FoodSignView
{
    private static readonly string[] FoodIconNames = { "grass", "carrot", "apple", "mushroom" };

    partial void OnSpawned(FoodSignViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var sprite = visualNode.GetNodeOrNull<Sprite3D>("FoodIcon");
        if (sprite == null) return;

        // Set initial icon
        UpdateFoodIcon(sprite, vm.FoodSign.FoodSign.SelectedFood.CurrentValue);

        // React to changes
        vm.FoodSign.FoodSign.SelectedFood.Subscribe(foodType =>
        {
            Callable.From(() => UpdateFoodIcon(sprite, foodType)).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(FoodSignViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }

    private static void UpdateFoodIcon(Sprite3D sprite, int foodType)
    {
        if (foodType >= 0 && foodType < FoodIconNames.Length)
        {
            var texture = GD.Load<Texture2D>($"res://sprites/export/icons/{FoodIconNames[foodType]}.png");
            if (texture != null)
                sprite.Texture = texture;
        }
    }
}
