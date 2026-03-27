using Godot;

namespace Template.Godot.Framework;

/// <summary>
/// Marks the entity as having a SkinComponent. Add as a child of FrameworkEntity.
/// </summary>
[Tool]
[Icon("res://Scripts/Framework/icons/skin.svg")]
public partial class FrameworkSkin : Node
{
    public override string[] _GetConfigurationWarnings()
    {
        if (GetParent() is not FrameworkEntity)
            return new[] { "FrameworkSkin must be a direct child of FrameworkEntity." };
        return System.Array.Empty<string>();
    }
}
