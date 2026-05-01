using Godot;
using R3;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class RoleSignView
{
    private static readonly System.Collections.Generic.Dictionary<int, string> RoleIcons = new()
    {
        { HelperType.Assistant, "res://sprites/export/icons/Money_/1.png" },
        { HelperType.Gatherer, "res://sprites/export/icons/Carrot_/1.png" },
        { HelperType.Seller, "res://sprites/export/icons/Money_/1.png" },
        { HelperType.Builder, "res://sprites/export/icons/Money_/1.png" },
        { HelperType.Milker, "res://sprites/export/icons/Milky_/1.png" },
    };

    partial void OnSpawned(RoleSignViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var icon = visualNode.GetNodeOrNull<AnimatedSprite3D>("FoodIcon")
                   ?? visualNode.GetNodeOrNull<AnimatedSprite3D>("RoleIcon");
        if (icon == null) return;

        vm.RoleSign.RoleSign.Role.Subscribe(role =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(icon)) return;
                if (!RoleIcons.TryGetValue(role, out var iconPath)) return;
                var texture = GD.Load<Texture2D>(iconPath);
                if (texture == null) return;
                var frames = new SpriteFrames();
                frames.AddAnimation("default");
                frames.AddFrame("default", texture);
                icon.SpriteFrames = frames;
                icon.Animation = "default";
                icon.Frame = 0;
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(RoleSignViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
