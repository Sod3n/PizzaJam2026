using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Template.Godot.Framework;

/// <summary>
/// Root node for a framework entity definition.
/// Add as a child node in a .tscn scene. Add ComponentNodes, FrameworkPhysicsBody,
/// FrameworkNavAgent, FrameworkState, FrameworkSkin, FrameworkVisualBinding, FrameworkChildEntity as children.
/// The CodeGen tool will parse this scene to generate server components, definitions, and client viewmodels.
/// </summary>
[Tool]
[Icon("res://Scripts/Framework/icons/entity.svg")]
public partial class FrameworkEntity : Node3D
{
    [Export] public string EntityName { get; set; } = "";
    [Export] public string StableIdSeed { get; set; } = "";

    public override void _Ready()
    {
        if (Engine.IsEditorHint() && string.IsNullOrEmpty(StableIdSeed))
            StableIdSeed = System.Guid.NewGuid().ToString();
        if (Engine.IsEditorHint() && string.IsNullOrEmpty(EntityName))
            EntityName = Name;
    }

    public override string[] _GetConfigurationWarnings()
    {
        var warnings = new List<string>();

        if (string.IsNullOrEmpty(EntityName))
            warnings.Add("EntityName is required.");

        if (string.IsNullOrEmpty(StableIdSeed))
            warnings.Add("StableIdSeed is empty. It will be auto-generated on _Ready.");

        // Child type validation
        foreach (var child in GetChildren())
        {
            if (child is not (FrameworkComponent or FrameworkPhysicsBody or FrameworkNavAgent
                or FrameworkState or FrameworkSkin or FrameworkVisualBinding or FrameworkChildEntity or Node3D)
                && !IsComponentNode(child))
            {
                warnings.Add($"Child '{child.Name}' is not a recognized framework node type.");
            }
        }

        // Duplicate StableIdSeed detection
        if (!string.IsNullOrEmpty(StableIdSeed) && Engine.IsEditorHint())
        {
            var duplicates = FindDuplicateSeeds();
            if (duplicates.Count > 0)
                warnings.Add($"StableIdSeed '{StableIdSeed}' is also used by: {string.Join(", ", duplicates)}. Each entity must have a unique seed.");
        }

        // Entity summary
        if (!string.IsNullOrEmpty(EntityName))
        {
            var summary = BuildCodeGenSummary();
            if (!string.IsNullOrEmpty(summary))
                warnings.Add(summary);
        }

        return warnings.ToArray();
    }

    private string BuildCodeGenSummary()
    {
        var lines = new List<string> { $"ℹ CodeGen will produce:" };

        lines.Add($"  • {EntityName}Definition.g.cs");
        lines.Add($"  • {EntityName}ViewModel.g.cs");
        lines.Add($"  • {EntityName}View.g.cs");

        var attrTypes = new List<string> { "Transform2D" };

        // Count ComponentNode children
        foreach (var child in GetChildren())
        {
            if (IsComponentNode(child))
                attrTypes.Add((string)child.Name);
            else if (child is FrameworkComponent fc)
                attrTypes.Add(!string.IsNullOrEmpty(fc.ComponentName) ? fc.ComponentName : (string)fc.Name);
        }

        if (GetChildren().OfType<FrameworkPhysicsBody>().Any())
        {
            attrTypes.Add("PhysicsBody");
            attrTypes.Add("CollisionShape2D");
        }
        if (GetChildren().OfType<FrameworkState>().Any())
            attrTypes.Add("StateComponent");
        if (GetChildren().OfType<FrameworkSkin>().Any())
            attrTypes.Add("SkinComponent");
        if (GetChildren().OfType<FrameworkNavAgent>().Any())
            attrTypes.Add("NavigationAgent2D");

        lines.Add($"  [{string.Join(", ", attrTypes)}]");

        return string.Join("\n", lines);
    }

    private static bool IsComponentNode(Node child)
    {
        var script = child.GetScript();
        if (script.Obj is Script s && s.ResourcePath != null)
            return s.ResourcePath.EndsWith("Node.g.cs") || s.ResourcePath.EndsWith("Node.cs");
        return child.GetType().Name.EndsWith("Node") && child.GetType().Namespace == "Template.Godot.Framework";
    }

    private List<string> FindDuplicateSeeds()
    {
        var duplicates = new List<string>();
        try
        {
            var scenesDir = GetTree()?.EditedSceneRoot?.SceneFilePath;
            if (string.IsNullOrEmpty(scenesDir)) return duplicates;

            var templatesDir = "res://templates";
            if (!DirAccess.DirExistsAbsolute(templatesDir)) return duplicates;

            ScanDirectoryForDuplicateSeeds(templatesDir, duplicates);
        }
        catch
        {
            // Silently ignore filesystem errors in editor
        }
        return duplicates;
    }

    private void ScanDirectoryForDuplicateSeeds(string path, List<string> duplicates)
    {
        using var dir = DirAccess.Open(path);
        if (dir == null) return;

        dir.ListDirBegin();
        var fileName = dir.GetNext();
        while (!string.IsNullOrEmpty(fileName))
        {
            var fullPath = $"{path}/{fileName}";
            if (dir.CurrentIsDir() && fileName != "." && fileName != "..")
            {
                ScanDirectoryForDuplicateSeeds(fullPath, duplicates);
            }
            else if (fileName.EndsWith(".tscn"))
            {
                var currentScene = GetTree()?.EditedSceneRoot?.SceneFilePath;
                if (fullPath != currentScene)
                {
                    var content = FileAccess.GetFileAsString(fullPath);
                    if (!string.IsNullOrEmpty(content) && content.Contains(StableIdSeed))
                        duplicates.Add(fileName);
                }
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();
    }
}
