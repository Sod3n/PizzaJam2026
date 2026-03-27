using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Template.Godot.Framework;

/// <summary>
/// Defines a custom component for an entity. Add as a child of FrameworkEntity.
/// </summary>
[Tool]
[Icon("res://Scripts/Framework/icons/component.svg")]
public partial class FrameworkComponent : Node
{
    private static readonly HashSet<string> SupportedTypes = new()
    {
        "int", "bool", "float", "byte", "Entity", "Vector2", "string", "FixedString32"
    };

    [Export] public string ComponentName { get; set; } = "";

    [Export] public string[] FieldNames { get; set; } = System.Array.Empty<string>();
    [Export] public string[] FieldTypes { get; set; } = System.Array.Empty<string>();
    [Export] public string[] FieldDefaults { get; set; } = System.Array.Empty<string>();

    public override void _Ready()
    {
        if (Engine.IsEditorHint() && string.IsNullOrEmpty(ComponentName))
            ComponentName = Name;
    }

    public override string[] _GetConfigurationWarnings()
    {
        var warnings = new List<string>();

        if (string.IsNullOrEmpty(ComponentName))
            warnings.Add("ComponentName is required.");

        if (GetParent() is not FrameworkEntity)
            warnings.Add("FrameworkComponent must be a direct child of FrameworkEntity.");

        if (FieldNames.Length != FieldTypes.Length)
            warnings.Add("FieldNames and FieldTypes must have the same length.");

        // Field type validation (#8)
        for (int i = 0; i < FieldTypes.Length; i++)
        {
            var type = FieldTypes[i];
            if (!SupportedTypes.Contains(type))
            {
                warnings.Add($"Field type '{type}' (at index {i}) is not a recognized type. Supported: {string.Join(", ", SupportedTypes.OrderBy(t => t))}. Custom types are allowed but must exist in the framework.");
            }
        }

        // Warn on duplicate field names
        var duplicates = FieldNames
            .GroupBy(n => n)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            warnings.Add($"Duplicate field names: {string.Join(", ", duplicates)}");

        // Warn on empty field names
        for (int i = 0; i < FieldNames.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(FieldNames[i]))
                warnings.Add($"FieldName at index {i} is empty.");
        }

        return warnings.ToArray();
    }
}
