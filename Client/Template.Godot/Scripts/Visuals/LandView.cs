using Godot;
using R3;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class LandView
{
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
