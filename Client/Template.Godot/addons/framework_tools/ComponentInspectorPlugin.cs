#if TOOLS
using Godot;

namespace Template.Godot.Framework.Editor;

/// <summary>
/// Adds an "Open Source Struct" button to the inspector when a ComponentNode is selected,
/// linking directly to the IComponent .cs file in Server/Template.Shared/Components/.
/// </summary>
[Tool]
public partial class ComponentInspectorPlugin : EditorInspectorPlugin
{
    private const string ComponentsDir = "../../Server/Template.Shared/Components/";

    public override bool _CanHandle(GodotObject @object)
    {
        var className = @object.GetType().Name;
        return className.EndsWith("ComponentNode") || className.EndsWith("EntityNode");
    }

    public override void _ParseBegin(GodotObject @object)
    {
        var className = @object.GetType().Name;
        // Strip "Node" suffix to get component name: PlayerEntityNode → PlayerEntity
        if (!className.EndsWith("Node") || className.Length <= 4) return;
        var componentName = className[..^4];

        var projectPath = ProjectSettings.GlobalizePath("res://");
        var structPath = System.IO.Path.Combine(projectPath, ComponentsDir, componentName + ".cs");
        structPath = System.IO.Path.GetFullPath(structPath);

        GD.Print($"[ComponentInspector] class={className} component={componentName} path={structPath} exists={System.IO.File.Exists(structPath)}");

        if (!System.IO.File.Exists(structPath)) return;

        var button = new Button();
        button.Text = $"Open {componentName}.cs";
        button.Pressed += () =>
        {
            OS.ShellOpen(structPath);
        };

        AddCustomControl(button);
    }
}
#endif
