using Godot;
using R3;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class FoodSignView
{
    private static readonly string[] FoodIconPaths =
    {
        "res://sprites/export/icons/Grass_/1.png",
        "res://sprites/export/icons/Carrot_/1.png",
        "res://sprites/export/icons/Apply_/1.png",
        "res://sprites/export/icons/Mashroom/1.png",
    };

    partial void OnSpawned(FoodSignViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var sprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("FoodIcon");
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

    private static void UpdateFoodIcon(AnimatedSprite3D sprite, int foodType)
    {
        if (foodType >= 0 && foodType < FoodIconPaths.Length)
        {
            var texture = GD.Load<Texture2D>(FoodIconPaths[foodType]);
            if (texture != null)
            {
                var frames = new SpriteFrames();
                frames.AddAnimation("default");
                frames.AddFrame("default", texture);
                sprite.SpriteFrames = frames;
                sprite.Animation = "default";
                sprite.Frame = 0;
            }
        }
    }
}
