using Template.Shared.Components;

namespace Template.Shared.Actions;

// Static lookups keyed by LandType. Single place to add new buildings' UI strings.
public static class BuildingInfo
{
    public static string GetInfoKey(LandType type) => type switch
    {
        LandType.SellPoint        => StateKeys.InfoSellPoint,
        LandType.House            => StateKeys.InfoHouse,
        LandType.LoveHouse        => StateKeys.InfoLoveHouse,
        LandType.CarrotFarm       => StateKeys.InfoCarrotFarm,
        LandType.AppleOrchard     => StateKeys.InfoAppleOrchard,
        LandType.MushroomCave     => StateKeys.InfoMushroomCave,
        LandType.HelperAssistant  => StateKeys.InfoHelperAssistant,
        LandType.Decoration       => StateKeys.InfoDecoration,
        LandType.Warehouse        => StateKeys.InfoWarehouse,
        _                         => null
    };
}
