using Godot;
using R3;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class LandView
{
    private static readonly System.Collections.Generic.Dictionary<int, string> LandTypeIcons = new()
    {
        { LandType.House, "res://sprites/export/homes/A_bar.png" },
        { LandType.LoveHouse, "res://sprites/export/homes/Love_Hotel_.png" },
        { LandType.SellPoint, "res://sprites/export/homes/Sell_Point_.png" },
        { LandType.CarrotFarm, "res://sprites/export/homes/Rabbit_Home_.png" },
        { LandType.AppleOrchard, "res://sprites/export/homes/Hours_Home_.png" },
        { LandType.MushroomCave, "res://sprites/export/homes/Mash_Home_.png" },
    };

    partial void OnSpawned(LandViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        vm.Remaining.Subscribe(x =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(visualNode)) return;
                visualNode.GetNodeOrNull<Label3D>("Remaining")?.SetDeferred("text", x.ToString());
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        // Set house type icon
        var landType = vm.Land.Land.Type.CurrentValue;
        var sprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("HouseType");
        if (sprite != null && LandTypeIcons.TryGetValue(landType, out var iconPath))
        {
            // Read original texture size before replacing
            float baseSize = 0;
            var origTex = sprite.SpriteFrames?.GetFrameTexture("default", 0);
            if (origTex != null)
                baseSize = Mathf.Max(origTex.GetWidth(), origTex.GetHeight());

            var texture = GD.Load<Texture2D>(iconPath);
            if (texture != null)
            {
                var frames = new SpriteFrames();
                frames.AddAnimation("default");
                frames.AddFrame("default", texture);
                sprite.SpriteFrames = frames;
                sprite.Animation = "default";
                sprite.Frame = 0;

                // Scale so new texture fits same visual bounds as original
                float newSize = Mathf.Max(texture.GetWidth(), texture.GetHeight());
                if (newSize > 0 && baseSize > 0)
                    sprite.PixelSize *= baseSize / newSize;
            }
        }

        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }

    partial void OnDespawned(LandViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
