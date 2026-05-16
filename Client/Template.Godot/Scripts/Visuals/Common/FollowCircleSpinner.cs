using Godot;

namespace Template.Godot.Visuals;

internal static class FollowCircleSpinner
{
    private const float PeriodSeconds = 6.0f;

    public static void Spin(Node3D node)
    {
        if (!Node.IsInstanceValid(node)) return;
        var tween = node.CreateTween().SetLoops();
        tween.TweenProperty(node, "rotation:z", Mathf.Tau, PeriodSeconds).AsRelative();
    }
}
