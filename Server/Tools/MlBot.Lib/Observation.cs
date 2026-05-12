using System;

namespace MlBot.Lib;

public sealed class WorldObservation
{
    public Globals Globals { get; set; } = new();
    public PlayerInfo Player { get; set; } = new();
    public LandInfo[] Land { get; set; } = Array.Empty<LandInfo>();
    public BuildingInfo[] Buildings { get; set; } = Array.Empty<BuildingInfo>();
    public CowInfo[] Cows { get; set; } = Array.Empty<CowInfo>();
    public HelperInfo[] Helpers { get; set; } = Array.Empty<HelperInfo>();
    public FoodInfo[] Food { get; set; } = Array.Empty<FoodInfo>();
    public int Tick { get; set; }
}

public sealed class Globals
{
    public int Coins { get; set; }
    public int Milk { get; set; }
    public int CarrotMilkshake { get; set; }
    public int VitaminMix { get; set; }
    public int PurplePotion { get; set; }
    public int Grass { get; set; }
    public int Carrot { get; set; }
    public int Apple { get; set; }
    public int Mushroom { get; set; }
    public int DayCounter { get; set; }
    public int TotalBreedCount { get; set; }
    public int HelpersSpawned { get; set; }
    public int HelpersEnabled { get; set; }
}

public sealed class PlayerInfo
{
    public float X { get; set; }
    public float Y { get; set; }
    public int PetCount { get; set; }
    public bool Active { get; set; }
    public string StateKey { get; set; } = "";
}

public sealed class LandInfo
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int Type { get; set; }
    public int Locked { get; set; }
    public int Threshold { get; set; }
    public int CurrentCoins { get; set; }
    public int Ring { get; set; }
}

public sealed class BuildingInfo
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public string Type { get; set; } = "";
    public int OccupantCowId { get; set; }
}

public sealed class CowInfo
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int PreferredFood { get; set; }
    public int SecondaryPreferredFood { get; set; }
    public int Exhaust { get; set; }
    public int MaxExhaust { get; set; }
    public bool Depressed { get; set; }
    public bool Milking { get; set; }
    public int HouseId { get; set; }
    public int FollowingPlayer { get; set; }
}

public sealed class HelperInfo
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int Type { get; set; }
    public int State { get; set; }
    public int OwnerPlayer { get; set; }
    public int WantedFoodType { get; set; }
    public int BagTotal { get; set; }
    public int PetCount { get; set; }
}

public sealed class FoodInfo
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int Type { get; set; }
}

public sealed class Deltas
{
    public int Coins { get; set; }
    public int Milk { get; set; }
    public int Food { get; set; }
    public int Built { get; set; }
    public int FinalBuilt { get; set; }
    public int Helpers { get; set; }
    public int Cows { get; set; }
    public int Pets { get; set; }
    public int LandLost { get; set; }
    public int TicksElapsed { get; set; }
}
