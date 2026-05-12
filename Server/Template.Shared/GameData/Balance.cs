using Template.Shared.Components;

namespace Template.Shared.GameData;

/// <summary>
/// Single source of truth for tunable balance values.
///
/// Values are mutable (<c>static</c> with private setter) so they can be overridden
/// at process startup via <see cref="LoadFromJson"/>. Defaults baked here are the
/// "hard" production values; pass a different JSON to override.
///
/// DETERMINISM NOTE: because values can differ between processes, a JSON content
/// hash must be stored in world state so a divergent balance config produces a
/// divergent state hash (desync detection). TODO: emit <c>BalanceHashComponent</c>
/// from <see cref="LoadFromJson"/> and include it in the world entity at scene init.
/// </summary>
public static class Balance
{
    public static int TickRate { get; private set; } = 60;

    public static class Match
    {
        public static int StartingCoins { get; private set; } = 60;
        public static int StartingCowCount { get; private set; } = 2;
        /// <summary>
        /// Primary food preference per starter cow. Index N is applied to starter cow N.
        /// If <see cref="StartingCowCount"/> exceeds the array length, the indexing wraps modulo length.
        /// </summary>
        public static int[] StarterCowFoods { get; private set; } = [FoodType.Grass, FoodType.Grass];
    }

    public static class Player
    {
        public static float WalkSpeed { get; private set; } = 20f;
        public static float SprintSpeed { get; private set; } = 22f;
        /// <summary>Hold-to-repeat threshold (ticks). Lower = faster auto-fire while holding interact.</summary>
        public static int HoldRepeatThreshold { get; private set; } = 15;
    }

    public static class HelperPlayer
    {
        public static float WalkSpeed { get; private set; } = 20f;
        public static float SprintSpeed { get; private set; } = 22f;
        public static int BagCapacity { get; private set; } = 999_999;
        public static int HoldRepeatThreshold { get; private set; } = 14;
    }

    public static class Cow
    {
        public static int ClicksPerMilk { get; private set; } = 1;
        public static int DepressionTicks { get; private set; } = 1800;
        public static int NonPreferredFoodFailPercent { get; private set; } = 50;
        public static bool DepressionEnabled { get; private set; } = false;
        public static int TwinChancePercent { get; private set; } = 1;
        public static int BreedInheritParentChancePercent { get; private set; } = 25;
        public static int SecondaryPreferenceChancePercent { get; private set; } = 20;
        public static int MaxHorny { get; private set; } = 7200;
        // Per-cow MaxHorny = MaxHorny * (HornyExhaustBaseline / cow.MaxExhaust)^HornyExhaustCurve.
        // A cow with MaxExhaust == HornyExhaustBaseline always fills in exactly MaxHorny ticks.
        // HornyExhaustCurve controls steepness: 1.0 = linear, 2.0 = quadratic (current default —
        // strong cows fill much faster, weak cows much slower), 0.5 = square-root (gentler spread).
        public static int HornyExhaustBaseline { get; private set; } = 66;
        public static float HornyExhaustCurve { get; private set; } = 0.35f;
        public static int HornyPerMilkClick { get; private set; } = 300;
        public static int AttackCatchDistanceSq { get; private set; } = 4;
        public static int HornyOffscreenIndicatorThresholdPercent { get; private set; } = 75;
    }

    public static class Breed
    {
        public static int MinCost { get; private set; } = 3;
        public static int FailCostMultiplier { get; private set; } = 2;
        public static int FailChanceTier1 { get; private set; } = 50;
        public static int FailChanceTier2 { get; private set; } = 75;
        public static int FailChanceTier3Plus { get; private set; } = 90;
        public static int HeartDefault { get; private set; } = 50;
        public static int HeartLovePair { get; private set; } = 95;
        public static int HeartSameTierDuring { get; private set; } = 70;
        public static int HeartSameTierPre { get; private set; } = 85;
        public static int HeartTierGap1 { get; private set; } = 45;
        public static int HeartTierGap2 { get; private set; } = 25;
        public static int HeartTierGap3Plus { get; private set; } = 15;
    }

    public static class Love
    {
        public static bool Enabled { get; private set; } = true;
        public static int NextEventBreedsMin { get; private set; } = 2;
        public static int NextEventBreedsMax { get; private set; } = 5;
        public static int EventDelayTicksMin { get; private set; } = 0;
        public static int EventDelayTicksMax { get; private set; } = 10801;
    }

    public static class PlayerHouse
    {
        public static int SleepCooldownTicks { get; private set; } = 7200;
        public static int ClickToSkipTicks { get; private set; } = 60;
    }

    public static class Sell
    {
        public static int DayCycle { get; private set; } = 3;
        public static int CowDayRemainder { get; private set; } = 2;
        public static int CowBasePrice { get; private set; } = 0;
        public static int CowTierPrice { get; private set; } = 10;
        public static int CowRestedPrice { get; private set; } = 3;
    }

    public static class Sleep
    {
        public static class Grass
        {
            public static int BasePerDay { get; private set; } = 12;
            public static int PerFarm { get; private set; } = 0;
        }
        public static class Carrot
        {
            public static int BasePerDay { get; private set; } = 0;
            public static int PerFarm { get; private set; } = 5;
        }
        public static class Apple
        {
            public static int BasePerDay { get; private set; } = 0;
            public static int PerFarm { get; private set; } = 5;
        }
        public static class Mushroom
        {
            public static int BasePerDay { get; private set; } = 0;
            public static int PerFarm { get; private set; } = 5;
        }
    }

    public static class Helper
    {
        public static int GatherWorkDuration { get; private set; } = 30;
        public static int SellWorkDuration { get; private set; } = 10;
        public static int BuildWorkDuration { get; private set; } = 15;
        public static int MilkWorkDuration { get; private set; } = 20;
        public static int[] HelperUnlockBreeds { get; private set; } = [2, 4, 6, 10];
        public static int GuaranteedMegaBreed { get; private set; } = 12;
        public static float TargetReachedDistSq { get; private set; } = 9f;
        public static float PlayerReturnDistSq { get; private set; } = 36f;
        public static float GatherReachedDistSq { get; private set; } = 4f;
        public static float OwnerSwitchThresholdSq { get; private set; } = 25f;
    }

    public static class Pets
    {
        public static int AdditiveBoostBase { get; private set; } = 1;
        public static int BoostPerPet { get; private set; } = 1;
        public static float SpeedPerPet { get; private set; } = 3f;
        public static int HoldRepeatReductionPerPet { get; private set; } = 1;
        public static int HoldRepeatFloor { get; private set; } = 3;
    }

    public static class FoodSpawn
    {
        public static int IntervalTicks { get; private set; } = 600;
        public static int MaxSpawnAttempts { get; private set; } = 10;
    }

    public static class Props
    {
        public static int Count { get; private set; } = 350;
        public static float MinPropDistance { get; private set; } = 4f;
        public static float MinSameTypeDistance { get; private set; } = 8f;
        public static float MinLandLabelBuffer { get; private set; } = 2f;
        public static uint Seed { get; private set; } = 98765u;
    }

    public static class Build
    {
        public static int BasePriceMultiplier { get; private set; } = 10;
        public static int EraMultiplier_Ring6Plus { get; private set; } = 6;
        public static int EraMultiplier_Ring5 { get; private set; } = 4;
        public static int EraMultiplier_Ring4 { get; private set; } = 3;
        public static int EraMultiplier_Ring3 { get; private set; } = 2;
        public static int EraMultiplier_RingDefault { get; private set; } = 1;

        public static class UnlockRing
        {
            public static int CarrotFarm { get; private set; } = 2;
            public static int AppleOrchard { get; private set; } = 4;
            public static int MushroomCave { get; private set; } = 6;
            public static int Warehouse { get; private set; } = 3;
            public static int Library { get; private set; } = 3;
            public static int LoveHouse { get; private set; } = 3;
            public static int SellPoint { get; private set; } = 5;
            public static int HelperAssistant { get; private set; } = 2;
            public static int Smithy { get; private set; } = 4;
        }

        public static class Limit
        {
            public static class CarrotFarm      { public static int World { get; private set; } = 2;  public static int PerRing { get; private set; } = -1; }
            public static class AppleOrchard    { public static int World { get; private set; } = 2;  public static int PerRing { get; private set; } = -1; }
            public static class MushroomCave    { public static int World { get; private set; } = 2;  public static int PerRing { get; private set; } = -1; }
            public static class Warehouse       { public static int World { get; private set; } = 1;  public static int PerRing { get; private set; } = -1; }
            public static class Library         { public static int World { get; private set; } = 1;  public static int PerRing { get; private set; } = -1; }
            public static class HelperAssistant { public static int World { get; private set; } = -1; public static int PerRing { get; private set; } = 1;  }
            public static class LoveHouse       { public static int World { get; private set; } = -1; public static int PerRing { get; private set; } = -1; }
            public static class SellPoint       { public static int World { get; private set; } = -1; public static int PerRing { get; private set; } = -1; }
            public static class Smithy          { public static int World { get; private set; } = -1; public static int PerRing { get; private set; } = -1; }
        }
    }

    /// <summary>
    /// SHA-256 of the JSON content used to override defaults. Empty string if defaults
    /// were used. Persist this on the world entity so divergent balance configs produce
    /// divergent state hashes (desync detection).
    /// </summary>
    public static string JsonHash { get; private set; } = "";

    /// <summary>
    /// Override any subset of values from a JSON object. Missing keys keep their default.
    /// Schema mirrors the static class hierarchy: { "Match": { "StartingCoins": 200 }, ... }.
    /// Calls are idempotent and order-independent. Not thread-safe — call once at startup.
    /// </summary>
    public static void LoadFromJson(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Apply(typeof(Balance), doc.RootElement);

        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var hash = sha.ComputeHash(bytes);
        JsonHash = System.BitConverter.ToString(hash).Replace("-", "");
    }

    private static void Apply(System.Type owner, System.Text.Json.JsonElement element)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return;

        foreach (var prop in owner.GetProperties(
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.NonPublic |
                     System.Reflection.BindingFlags.Static))
        {
            if (!element.TryGetProperty(prop.Name, out var val)) continue;
            var setter = prop.GetSetMethod(nonPublic: true);
            if (setter == null) continue;
            var converted = Convert(val, prop.PropertyType);
            if (converted == null && prop.PropertyType.IsValueType) continue;
            setter.Invoke(null, new[] { converted });
        }

        foreach (var nested in owner.GetNestedTypes(
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.NonPublic))
        {
            if (!element.TryGetProperty(nested.Name, out var val)) continue;
            Apply(nested, val);
        }
    }

    private static object? Convert(System.Text.Json.JsonElement val, System.Type targetType)
    {
        if (targetType == typeof(int)) return val.GetInt32();
        if (targetType == typeof(long)) return val.GetInt64();
        if (targetType == typeof(uint)) return val.GetUInt32();
        if (targetType == typeof(float)) return val.GetSingle();
        if (targetType == typeof(double)) return val.GetDouble();
        if (targetType == typeof(bool)) return val.GetBoolean();
        if (targetType == typeof(string)) return val.GetString();
        if (targetType == typeof(int[]))
        {
            var len = val.GetArrayLength();
            var arr = new int[len];
            int i = 0;
            foreach (var e in val.EnumerateArray()) arr[i++] = e.GetInt32();
            return arr;
        }
        return null;
    }
}
