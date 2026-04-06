#if TOOLS
using Godot;

namespace Template.Godot.Editor;

[Tool]
public partial class ImportButton : Button
{
    public override void _Pressed()
    {
        GD.Print("[PSD Importer] Starting import...");
        var importer = new PsdImporter();
        importer._Run();
        GD.Print("[PSD Importer] Done!");
    }
}
#endif
