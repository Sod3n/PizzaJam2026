using Godot;
using Template.Shared.Components;

namespace Template.Godot.GameResources;

/// <summary>
/// Inspector-editable mapping from LandType ids to Texture2D icons.
/// Author once at <c>res://Resources/LandTypeIcons.tres</c> and reference
/// the same .tres anywhere it's needed (cycle sign, build menu, etc.).
/// </summary>
[GlobalClass]
public partial class LandTypeIconSet : Resource
{
    [Export] public LandTypeIconEntry[] Entries { get; set; } = System.Array.Empty<LandTypeIconEntry>();

    public bool TryGet(LandType landType, out Texture2D icon)
    {
        if (Entries != null)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                var e = Entries[i];
                if (e != null && e.LandType == landType && e.Icon != null)
                {
                    icon = e.Icon;
                    return true;
                }
            }
        }
        icon = null;
        return false;
    }
}
