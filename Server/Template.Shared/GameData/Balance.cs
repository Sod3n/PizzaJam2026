using Template.Shared.Components;

namespace Template.Shared.GameData;

/// <summary>
/// Single source of truth for tunable balance values. Anything you'd realistically
/// adjust to change game feel / economy lives here. Structural numbers (collision
/// sizes, struct defaults, the star map's geometry) stay near their definitions.
///
/// All values are <c>const</c> so they remain compile-time constants — no runtime
/// dispatch, deterministic across server and client.
/// </summary>
public static class Balance
{
    public const int TickRate = 60; // 60 ticks per second — used to convert seconds → ticks

    public static class Match
    {
        public const int StartingCoins = 60;
        public const int StartingCowCount = 2;
        /// <summary>
        /// Primary food preference per starter cow. Index N is applied to starter cow N.
        /// If <see cref="StartingCowCount"/> exceeds the array length, the indexing wraps modulo length.
        /// Edit this list to change the starter line-up.
        /// </summary>
        public static readonly int[] StarterCowFoods = [FoodType.Grass, FoodType.Grass];
    }

    public static class Player
    {
        public const float WalkSpeed = 14f;
        public const float SprintSpeed = 16f;
    }

    public static class HelperPlayer
    {
        public const float WalkSpeed = 20f;
        public const float SprintSpeed = 22f;
        /// <summary>Effective "infinite" — high enough that no realistic gather session fills it.</summary>
        public const int BagCapacity = 999_999;
        /// <summary>Baseline ClickMultiplier (vs main player's 1). HelperSystem recomputes per tick adding pets on top.</summary>
        public const int ClickMultiplier = 2;
        /// <summary>Hold-to-repeat threshold (ticks). Lower = faster auto-fire while holding interact.</summary>
        public const int HoldRepeatThreshold = 7;
    }

    public static class Cow
    {
        /// <summary>Clicks needed to produce one milk (4-click cycle).</summary>
        public const int ClicksPerMilk = 3;

        /// <summary>Failed-breed depression duration: 30s at 60 TPS.</summary>
        public const int DepressionTicks = 1800;

        /// <summary>Per-milk fail roll for non-preferred food (percent 0-100).</summary>
        public const int NonPreferredFoodFailPercent = 50;

        /// <summary>
        /// Master switch for failed-breed depression. False = breeds never fail (cross-tier
        /// always upgrades, no depression). Set true to re-enable the original mechanic.
        /// </summary>
        public const bool DepressionEnabled = false;

        /// <summary>Roll-under percent for twins on a same-pref breed. 1 = ~1% chance.</summary>
        public const int TwinChancePercent = 1;

        /// <summary>Per-parent inherit chance for offspring's primary food (each parent rolled separately).</summary>
        public const int BreedInheritParentChancePercent = 25;

        /// <summary>Chance the offspring (or starter cow) also has a secondary food preference.</summary>
        public const int SecondaryPreferenceChancePercent = 20;
    }

    public static class Breed
    {
        /// <summary>Floor on breed cost no matter what the parent exhausts add up to.</summary>
        public const int MinCost = 3;

        /// <summary>Cost multiplier applied when the pre-roll predicts failure (only with DepressionEnabled).</summary>
        public const int FailCostMultiplier = 2;

        /// <summary>Failed-breed roll percent when parents are 1 tier apart in food preference.</summary>
        public const int FailChanceTier1 = 50;
        /// <summary>Failed-breed roll percent when parents are 2 tiers apart.</summary>
        public const int FailChanceTier2 = 75;
        /// <summary>Failed-breed roll percent when parents are 3+ tiers apart.</summary>
        public const int FailChanceTier3Plus = 90;

        // Heart popup percentage shown to the player as a "luck" hint.
        public const int HeartDefault = 50;
        public const int HeartLovePair = 95;
        /// <summary>During-breed heart hint for same-tier pairs (after breeding has started).</summary>
        public const int HeartSameTierDuring = 70;
        /// <summary>Pre-breed heart hint for same-tier pairs (when the player decides to start).</summary>
        public const int HeartSameTierPre = 85;
        public const int HeartTierGap1 = 45;
        public const int HeartTierGap2 = 25;
        public const int HeartTierGap3Plus = 15;
    }

    public static class Love
    {
        /// <summary>
        /// Master switch. When false, love events never fire — no threshold init, no
        /// timer scheduling, no deferred-event tick-down. Set true to re-enable.
        /// </summary>
        public const bool Enabled = true;

        /// <summary>Inclusive lower bound of breed-counter increment when scheduling next love event.</summary>
        public const int NextEventBreedsMin = 2;
        /// <summary>Exclusive upper bound (passed directly to NextInt) — actual range is Min..Max-1.</summary>
        public const int NextEventBreedsMax = 5;

        /// <summary>Min ticks before deferred love event fires after threshold trigger.</summary>
        public const int EventDelayTicksMin = 0;
        /// <summary>Exclusive upper bound for love-event delay roll. 10801 = up to 3 minutes at 60 TPS.</summary>
        public const int EventDelayTicksMax = 10801;
    }

    // LoveHouse cooldown is binary now — set on breed, cleared on sleep, no per-tick decay.
    // No tunable here; CooldownTicksRemaining is used as a flag (any non-zero = on cooldown).

    public static class PlayerHouse
    {
        /// <summary>Sleep cooldown: 120s at 60 TPS.</summary>
        public const int SleepCooldownTicks = 7200;

        /// <summary>Per-click skip while on cooldown: 1s at 60 TPS.</summary>
        public const int ClickToSkipTicks = 60;
    }

    public static class Sell
    {
        /// <summary>How often (in days) the sell point accepts cows instead of milk. Cycle = 3 days.</summary>
        public const int DayCycle = 3;

        /// <summary>Day-counter remainder that means "today is cow-day": (Day % 3) == 2.</summary>
        public const int CowDayRemainder = 2;

        // Cow sale formula (cow-buyer day):
        //   price = CowBasePrice + (PreferredFood + 1) * CowTierPrice + rested * CowRestedPrice
        // where `rested = MaxExhaust - Exhaust` (clamped to 0).

        /// <summary>Flat coin offset added to every cow sale, regardless of tier or rest.</summary>
        public const int CowBasePrice = 0;

        /// <summary>Coins per food-tier step (Grass=1×, Carrot=2×, Apple=3×, Mushroom=4×).</summary>
        public const int CowTierPrice = 10;

        /// <summary>Coin bonus per unit of remaining (rested) exhaust at sale time.</summary>
        public const int CowRestedPrice = 3;
    }

    public static class Sleep
    {
        // Per-food per-day spawn cap = BasePerDay + PerFarm * <count of matching farm>.
        // Counters reset on AdvanceDay. Tune each food type independently.
        public static class Grass
        {
            public const int BasePerDay = 12; // grass spawns without a farm
            public const int PerFarm = 0;     // there is no grass farm
        }
        public static class Carrot
        {
            public const int BasePerDay = 0;
            public const int PerFarm = 5;
        }
        public static class Apple
        {
            public const int BasePerDay = 0;
            public const int PerFarm = 5;
        }
        public static class Mushroom
        {
            public const int BasePerDay = 0;
            public const int PerFarm = 5;
        }
    }

    public static class Helper
    {
        // Work durations — ticks per atomic action
        public const int GatherWorkDuration = 30; // 0.5s
        public const int SellWorkDuration = 10;   // per item
        public const int BuildWorkDuration = 15;  // per coin
        public const int MilkWorkDuration = 20;   // per milk action

        // Helper unlocks: breed counter thresholds for spawning each helper type
        public const int GathererUnlockBreed = 2;
        public const int BuilderUnlockBreed = 4;
        public const int SellerUnlockBreed = 6;
        public const int MilkerUnlockBreed = 10;
        public const int GuaranteedMegaBreed = 12;

        // Distance-squared thresholds for helper navigation
        public const float TargetReachedDistSq = 9f;   // 3 units from sell points / land
        public const float PlayerReturnDistSq = 36f;   // 6 units from owner player
        public const float GatherReachedDistSq = 4f;   // 2 units from food
        /// <summary>Owner-switch hysteresis: new player must be this much closer (squared units) to steal ownership.</summary>
        public const float OwnerSwitchThresholdSq = 25f;

        /// <summary>Builder coins deposited per work cycle.</summary>
        public const int BuildCoinsPerWork = 3;
    }

    public static class Pets
    {
        /// <summary>Additive base of the pet boost formula (Capacity, Speed scale by base + perPet * petCount).</summary>
        public const int AdditiveBoostBase = 1;
        /// <summary>Per-pet additive multiplier (capacity / speed scale).</summary>
        public const int BoostPerPet = 1;
        /// <summary>Movement-speed bonus added per cat carried (additive, applies to both player types).</summary>
        public const float SpeedPerPet = 3f;
    }

    public static class FoodSpawn
    {
        /// <summary>How often the grass-spawn system fires: 10s at 60 TPS.</summary>
        public const int IntervalTicks = 600;

        public const int MaxSpawnAttempts = 10;
    }

    public static class Props
    {
        public const int Count = 350;
        public const float MinPropDistance = 4f;
        public const float MinSameTypeDistance = 8f;
        public const float MinLandLabelBuffer = 2f;
        public const uint Seed = 98765u;
    }

    public static class Build
    {
        /// <summary>Cost base — every land threshold = gridDist * EraMultiplier * priceMultiplier * BasePriceMultiplier.</summary>
        public const int BasePriceMultiplier = 10;

        /// <summary>Era cost multipliers by Manhattan ring distance from origin.</summary>
        public const int EraMultiplier_Ring6Plus = 6;
        public const int EraMultiplier_Ring5 = 4;
        public const int EraMultiplier_Ring4 = 3;
        public const int EraMultiplier_Ring3 = 2;
        public const int EraMultiplier_RingDefault = 1;

        /// <summary>Manhattan grid distance at which each LandType first becomes selectable on a cycle sign.</summary>
        public static class UnlockRing
        {
            public const int CarrotFarm = 2;
            public const int AppleOrchard = 4;
            public const int MushroomCave = 6;
            public const int Warehouse = 3;
            public const int Library = 3;
            public const int LoveHouse = 3;
            public const int SellPoint = 5;
            public const int HelperAssistant = 2;
        }

        /// <summary>
        /// Per-building cap config. Each type has both a <c>World</c> (worldwide max)
        /// and <c>PerRing</c> (max within a single Manhattan-distance ring) axis.
        /// <c>-1</c> on either axis disables that check; both checks must pass to
        /// keep the type in the cycle pool.
        /// </summary>
        public static class Limit
        {
            public static class CarrotFarm     { public const int World = 2;  public const int PerRing = -1; }
            public static class AppleOrchard   { public const int World = 2;  public const int PerRing = -1; }
            public static class MushroomCave   { public const int World = 2;  public const int PerRing = -1; }
            public static class Warehouse      { public const int World = 1;  public const int PerRing = -1; }
            public static class Library        { public const int World = 1;  public const int PerRing = -1; }
            public static class HelperAssistant{ public const int World = -1; public const int PerRing = 1;  }
            public static class LoveHouse      { public const int World = -1; public const int PerRing = -1; }
            public static class SellPoint      { public const int World = -1; public const int PerRing = -1; }
        }
    }
}
