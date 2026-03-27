using Godot;

namespace Template.Godot.Framework;

/// <summary>
/// Binds a component field to a visual node property. Add as a child of FrameworkEntity.
/// Supports expression-based bindings with {FieldName} syntax.
/// </summary>
[Tool]
[Icon("res://Scripts/Framework/icons/binding.svg")]
public partial class FrameworkVisualBinding : Node
{
    [Export] public string ComponentField { get; set; } = "";
    [Export] public NodePath TargetNodePath { get; set; } = "";
    [Export] public string TargetProperty { get; set; } = "text";

    /// <summary>
    /// Optional expression using {FieldName} syntax.
    /// E.g., "{Threshold} - {CurrentCoins}" generates a CombineLatest reactive property.
    /// If empty, uses ComponentField value directly.
    /// </summary>
    [Export(PropertyHint.MultilineText)] public string Expression { get; set; } = "";

    public override string[] _GetConfigurationWarnings()
    {
        var warnings = new System.Collections.Generic.List<string>();

        if (GetParent() is not FrameworkEntity)
            warnings.Add("FrameworkVisualBinding must be a direct child of FrameworkEntity.");

        if (string.IsNullOrEmpty(ComponentField) && string.IsNullOrEmpty(Expression))
            warnings.Add("Either ComponentField or Expression is required.");

        if (TargetNodePath == null || string.IsNullOrEmpty((string)TargetNodePath))
            warnings.Add("TargetNodePath is required.");

        return warnings.ToArray();
    }
}
