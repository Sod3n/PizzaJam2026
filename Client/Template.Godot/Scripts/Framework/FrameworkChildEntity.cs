using Godot;
using System.Collections.Generic;

namespace Template.Godot.Framework;

/// <summary>
/// Defines a child entity that is parented to the main entity.
/// Add as a child of FrameworkEntity. Add FrameworkPhysicsBody children to configure physics.
/// The child entity gets a Transform2D parented to the main entity.
/// Use StoreInField to save the child entity ID into a field on a parent component.
/// </summary>
[Tool]
[Icon("res://Scripts/Framework/icons/entity.svg")]
public partial class FrameworkChildEntity : Node
{
    /// <summary>Name for identification in generated code.</summary>
    [Export] public string ChildName { get; set; } = "";

    /// <summary>If true, child Transform2D.Parent is set to the parent entity.</summary>
    [Export] public bool ParentToEntity { get; set; } = true;

    /// <summary>If true, child entity is destroyed when unparented.</summary>
    [Export] public bool DestroyOnUnparent { get; set; } = true;

    /// <summary>Component type to store the child Entity ID in. Leave empty to use primary component.</summary>
    [Export] public string StoreInComponent { get; set; } = "";

    /// <summary>Field name on the target component to store the child Entity ID. Leave empty to skip.</summary>
    [Export] public string StoreInField { get; set; } = "";

    public override void _Ready()
    {
        if (Engine.IsEditorHint() && string.IsNullOrEmpty(ChildName))
            ChildName = Name;
    }

    public override string[] _GetConfigurationWarnings()
    {
        var warnings = new List<string>();
        if (GetParent() is not FrameworkEntity)
            warnings.Add("FrameworkChildEntity must be a direct child of FrameworkEntity.");
        if (string.IsNullOrEmpty(ChildName))
            warnings.Add("ChildName is required.");
        return warnings.ToArray();
    }
}
