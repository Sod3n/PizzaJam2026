using Godot;
using Template.Shared.Components;

namespace Template.Godot.GameResources;

public static class LandTypeIcons
{
    public static bool TryGet(Resource set, LandType landType, out Texture2D icon)
    {
        icon = null;
        if (set == null) return false;
        var v = set.Call("try_get", (int)landType);
        icon = v.As<Texture2D>();
        return icon != null;
    }
}
