using Godot;
using R3;

namespace Template.Godot.Visuals;

public partial class WarehouseSignView
{
    private static readonly string EnabledIconPath = "res://sprites/export/icons/Apply_/1.png";
    private static readonly string DisabledIconPath = "res://sprites/export/icons/Grass_/1.png";

    partial void OnSpawned(WarehouseSignViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var statusSprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("StatusIcon");
        if (statusSprite == null) return;

        // Set initial icon
        UpdateStatusIcon(statusSprite, vm.WarehouseSign.WarehouseSign.Enabled.CurrentValue);

        // React to changes
        vm.WarehouseSign.WarehouseSign.Enabled.Subscribe(enabled =>
        {
            Callable.From(() => UpdateStatusIcon(statusSprite, enabled)).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(WarehouseSignViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }

    private static void UpdateStatusIcon(AnimatedSprite3D sprite, int enabled)
    {
        var iconPath = enabled == 1 ? EnabledIconPath : DisabledIconPath;
        var texture = GD.Load<Texture2D>(iconPath);
        if (texture != null)
        {
            var frames = new SpriteFrames();
            frames.AddAnimation("default");
            frames.AddFrame("default", texture);
            sprite.SpriteFrames = frames;
            sprite.Animation = "default";
            sprite.Frame = 0;
        }

        // Tint green when enabled, red when disabled
        sprite.Modulate = enabled == 1
            ? new Color(0.2f, 0.8f, 0.2f)
            : new Color(0.8f, 0.2f, 0.2f);
    }
}
