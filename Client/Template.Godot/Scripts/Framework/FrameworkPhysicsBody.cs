using Godot;

namespace Template.Godot.Framework;

public enum FrameworkPhysicsBodyType { CharacterBody2D, StaticBody2D, Area2D }
public enum FrameworkCollisionShapeType { Circle, Rectangle }

/// <summary>
/// Configures physics for the entity. Add as a child of FrameworkEntity.
/// Enable the "Framework Tools" addon for editor gizmos with interactive handles.
/// </summary>
[Tool]
[Icon("res://Scripts/Framework/icons/physics.svg")]
public partial class FrameworkPhysicsBody : Node3D
{
    [Export] public FrameworkPhysicsBodyType BodyType { get; set; } = FrameworkPhysicsBodyType.StaticBody2D;
    [Export] public FrameworkCollisionShapeType ShapeType { get; set; } = FrameworkCollisionShapeType.Circle;
    [Export(PropertyHint.Range, "0.01,100,0.01")] public float ShapeRadius { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "0.01,100,0.01")] public float ShapeWidth { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0.01,100,0.01")] public float ShapeHeight { get; set; } = 1.0f;
    [Export(PropertyHint.Layers2DPhysics)] public uint CollisionLayer { get; set; } = 1;
    [Export(PropertyHint.Layers2DPhysics)] public uint CollisionMask { get; set; } = 1;
    [Export] public bool Monitoring { get; set; } = true;
    [Export] public bool Monitorable { get; set; } = false;

    public override void _ValidateProperty(global::Godot.Collections.Dictionary property)
    {
        var name = (string)property["name"];

        // Hide radius when rectangle, hide width/height when circle
        if (ShapeType == FrameworkCollisionShapeType.Circle && name is "ShapeWidth" or "ShapeHeight")
            property["usage"] = (int)(PropertyUsageFlags.NoEditor);
        else if (ShapeType == FrameworkCollisionShapeType.Rectangle && name == "ShapeRadius")
            property["usage"] = (int)(PropertyUsageFlags.NoEditor);

        // Hide Monitoring/Monitorable when not Area2D
        if (BodyType != FrameworkPhysicsBodyType.Area2D && name is "Monitoring" or "Monitorable")
            property["usage"] = (int)(PropertyUsageFlags.NoEditor);
    }

    public override string[] _GetConfigurationWarnings()
    {
        var warnings = new System.Collections.Generic.List<string>();
        if (GetParent() is not (FrameworkEntity or FrameworkChildEntity))
            warnings.Add("FrameworkPhysicsBody must be a direct child of FrameworkEntity or FrameworkChildEntity.");
        return warnings.ToArray();
    }
}
