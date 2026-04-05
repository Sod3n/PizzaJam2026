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
        { LandType.Warehouse, "res://sprites/export/homes/A_bar.png" },
    };

    /// <summary>
    /// Returns a dark tint color for the land sign based on Manhattan grid distance.
    /// Colors progress through the spectrum so players can visually gauge how far
    /// a plot is from the center (and therefore how expensive it will be).
    /// </summary>
    private static Color GetDistanceTintColor(int gridDist)
    {
        return gridDist switch
        {
            <= 1 => new Color(0.1f, 0.5f, 0.1f),    // dark green
            2    => new Color(0.1f, 0.15f, 0.55f),  // dark blue
            3    => new Color(0.4f, 0.1f, 0.5f),    // dark purple
            4    => new Color(0.55f, 0.08f, 0.08f),  // dark red
            5    => new Color(0.6f, 0.3f, 0.05f),   // dark orange
            _    => new Color(0.6f, 0.5f, 0.05f),   // dark gold (dist 6+)
        };
    }

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

        // Tint the sign background based on grid distance from center
        int gx = vm.Land.Land.Arm.CurrentValue;
        int gy = vm.Land.Land.Ring.CurrentValue;
        int gridDist = System.Math.Abs(gx) + System.Math.Abs(gy);
        var signSprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("AnimatedSprite3D2");
        if (signSprite != null)
            signSprite.Modulate = GetDistanceTintColor(gridDist);

        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }

    partial void OnDespawned(LandViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
