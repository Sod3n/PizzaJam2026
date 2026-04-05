namespace Template.Shared;

public static class StateKeys
{
    public const string Idle = "idle";
    public const string Interacted = "interacted";
    public const string GainedResource = "gained_resource";
    public const string NotEnoughResource = "not_enough_resource";
    public const string Milking = "milking";
    public const string Taming = "taming";
    public const string Assign = "assign";
    public const string Breed = "breed";
    public const string LoveCow = "love_cow"; // Cow interaction triggers love popup
    public const string BuildingInfo = "building_info";

    // Resource keys (used as Param in EnterStateComponent)
    public const string Grass = "grass";
    public const string Carrot = "carrot";
    public const string Apple = "apple";
    public const string Mushroom = "mushroom";
    public const string Milk = "milk";
    public const string CarrotMilkshake = "carrot_milkshake";
    public const string VitaminMix = "vitamin_mix";
    public const string PurplePotion = "purple_potion";
    public const string Food = "food"; // Generic "no food" key
    public const string Coins = "coins";
    public const string Houses = "houses";
    public const string Cows = "cows";

    // BuildingInfo param keys (used as Param when Key == BuildingInfo)
    public const string InfoSellPoint = "sell_point";
    public const string InfoHouse = "house";
    public const string InfoLoveHouse = "love_house";
    public const string InfoCarrotFarm = "carrot_farm";
    public const string InfoAppleOrchard = "apple_orchard";
    public const string InfoMushroomCave = "mushroom_cave";
    public const string InfoHelperAssistant = "helper_assistant";
    public const string InfoUpgradeGatherer = "upgrade_gatherer";
    public const string InfoUpgradeBuilder = "upgrade_builder";
    public const string InfoUpgradeSeller = "upgrade_seller";
    public const string InfoUpgradeAssistant = "upgrade_assistant";
    public const string InfoDecoration = "decoration";
}
