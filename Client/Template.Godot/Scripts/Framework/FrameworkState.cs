using Godot;

namespace Template.Godot.Framework;

/// <summary>
/// Marks the entity as having a StateComponent. Add as a child of FrameworkEntity.
/// </summary>
[Tool]
[Icon("res://Scripts/Framework/icons/state.svg")]
public partial class FrameworkState : Node
{
    public override string[] _GetConfigurationWarnings()
    {
        if (GetParent() is not FrameworkEntity)
            return new[] { "FrameworkState must be a direct child of FrameworkEntity." };
        return System.Array.Empty<string>();
    }
}
