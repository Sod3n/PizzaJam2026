using System.Collections.Generic;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public static class BuildingHints
{
    private static readonly Dictionary<LandType, string> _hints = new()
    {
        { LandType.House,           "— assign a cow to milk it, or a helper to help you." },
        { LandType.LoveHouse,       "— breed two cows together for a calf." },
        { LandType.SellPoint,       "— sell milk on regular days, cows on special days." },
        { LandType.FinalStructure,  "— deposit coins to complete the goal." },
        { LandType.CarrotFarm,      "— grows carrots in the world." },
        { LandType.AppleOrchard,    "— grows apples in the world." },
        { LandType.MushroomCave,    "— grows mushrooms in the world." },
        { LandType.HelperAssistant, "— pick up a helper pet and boost anybody with it." },
        { LandType.Decoration,      "— cosmetic building." },
        { LandType.Warehouse,       "— auto-deposits helper resources and loads them." },
        { LandType.PlayerHouse,     "— sleep to advance the day." },
        { LandType.Library,         "— browse your cows' family tree." },
        { LandType.Smithy,          "— spawn a hammer to demolish buildings." },
    };

    public static bool TryGet(LandType type, out string text) => _hints.TryGetValue(type, out text);
}
