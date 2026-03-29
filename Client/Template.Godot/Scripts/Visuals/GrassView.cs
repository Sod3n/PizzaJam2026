using Godot;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class GrassView
{
    private static readonly string[] FoodIconPaths =
    {
        "res://sprites/IMG_0375.PNG",                  // 0 = Grass (original sprite)
        "res://sprites/export/icons/carrot.png",       // 1 = Carrot
        "res://sprites/export/icons/apple.png",        // 2 = Apple
        "res://sprites/export/icons/mushroom.png",     // 3 = Mushroom
    };

    partial void OnSpawned(GrassViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var foodType = vm.Grass.Grass.FoodType.CurrentValue;
        if (foodType > 0 && foodType < FoodIconPaths.Length)
        {
            var sprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("AnimatedSprite3D");
            if (sprite != null)
            {
                var texture = GD.Load<Texture2D>(FoodIconPaths[foodType]);
                if (texture != null)
                {
                    var frames = new SpriteFrames();
                    frames.AddAnimation("default");
                    frames.AddFrame("default", texture);
                    frames.SetAnimationLoop("default", true);
                    sprite.SpriteFrames = frames;
                    sprite.Animation = "default";
                    sprite.Frame = 0;
                }
            }
        }
    }

    partial void OnDespawned(GrassViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
