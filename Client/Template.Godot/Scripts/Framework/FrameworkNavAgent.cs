using Godot;

namespace Template.Godot.Framework;

/// <summary>
/// Adds a NavigationAgent2D to the entity. Add as a child of FrameworkEntity.
/// Enable the "Framework Tools" addon for editor gizmos with interactive handles.
/// </summary>
[Tool]
[Icon("res://Scripts/Framework/icons/nav_agent.svg")]
public partial class FrameworkNavAgent : Node3D
{
    [Export(PropertyHint.Range, "0.1,1000,0.1")] public float MaxSpeed { get; set; } = 10f;
    [Export(PropertyHint.Range, "0.1,100,0.1")] public float TargetDesiredDistance { get; set; } = 2.0f;
    [Export(PropertyHint.Range, "0.1,100,0.1")] public float PathDesiredDistance { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0.01,100,0.01")] public float Radius { get; set; } = 0.5f;

    public override string[] _GetConfigurationWarnings()
    {
        var warnings = new System.Collections.Generic.List<string>();
        if (GetParent() is not FrameworkEntity)
            warnings.Add("FrameworkNavAgent must be a direct child of FrameworkEntity.");
        return warnings.ToArray();
    }
}
