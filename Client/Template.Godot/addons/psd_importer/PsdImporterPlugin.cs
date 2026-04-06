#if TOOLS
using Godot;

namespace Template.Godot.Editor;

[Tool]
public partial class PsdImporterPlugin : EditorPlugin
{
    private ImportButton _button;

    public override void _EnterTree()
    {
        _button = new ImportButton();
        _button.Text = "Import PSDs";
        AddControlToContainer(CustomControlContainer.Toolbar, _button);
    }

    public override void _ExitTree()
    {
        if (_button != null)
        {
            RemoveControlFromContainer(CustomControlContainer.Toolbar, _button);
            _button.QueueFree();
            _button = null;
        }
    }
}
#endif
