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
        /// <summary>Hard cap on MaxExhaust for the cows spawned at match start — keeps day-1 milking short.</summary>
        public static int StarterCowMaxExhaust { get; private set; } = 30;
    }

    public static class Player
    {
        public static float WalkSpeed { get; private set; } = 20f;
        public static float SprintSpeed { get; private set; } = 22f;
        /// <summary>Hold-to-repeat threshold (ticks). Lower = faster auto-fire while holding interact.</summary>
        public static int HoldRepeatThreshold { get; private set; } = 15;
        // How long the cow holds the player after a successful catch before the forced sleep kicks in.
        public static int CaughtTicks { get; private set; } = 60 * 5;
        // Final stretch of CaughtTicks during which the sleep fade fills the screen — must be ≥
        // SleepFadeOverlay.FadeInSeconds * TickRate so the teleport happens behind black.
        public static int CaughtFadeTicks { get; private set; } = 30;
        // Ticks between cow "boop" interactions on the player while caught (each fires a squish + hearts).
        public static int CaughtTapIntervalTicks { get; private set; } = 24;
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

        // Skin Exhaust values act as a *weight* — summing them ranks cows from cheap→expensive.
        // The actual MaxExhaust is then remapped onto [MinExhaust, MaxExhaust] using a curve:
        // t = (sum - sumMin) / (sumMax - sumMin), MaxExhaust = lerp(Min, Max, t^Curve).
        // Curve > 1 = exponential (rare cows much harder), Curve < 1 = logarithmic (rare cows only slightly harder).
        public static int MinExhaust { get; private set; } = 10;
        public static int MaxExhaust { get; private set; } = 80;
        public static float ExhaustCurve { get; private set; } = 2f;
        public static int DepressionTicks { get; private set; } = 1800;
        public static int NonPreferredFoodFailPercent { get; private set; } = 50;

        // Per-click milk scales based on how well the food matches the cow's preferences.
        // SuccessPercent gates whether the click produces milk at all; YieldOnSuccess is how
        // many milk units it produces when it does. Hardcoded; not exposed through JSON
        // balance overrides on purpose — these define the core feel of the milking minigame.
        public static class MilkScale
        {
            // Yield depends on whether the food is "discovered" — i.e. the player has fed
            // the cow MaxExhaust units of it (see CowComponent.IsFoodDiscovered).
            // Undiscovered foods give a smaller yield, rewarding the player for committing
            // to learning a cow's preference rather than guessing every click.

            // cow.PreferredFood
            public const int PrimarySuccessPercent = 100;
            public const int PrimaryYieldDiscovered = 2;
            public const int PrimaryYieldUndiscovered = 1;
            // cow.SecondaryPreferredFood
            public const int SecondarySuccessPercent = 80;
            public const int SecondaryYieldDiscovered = 2;
            public const int SecondaryYieldUndiscovered = 1;
            // non-preferred but within cow's tier
            public const int OtherSameTierSuccessPercent = 60;
            public const int OtherSameTierYieldDiscovered = 1;
            public const int OtherSameTierYieldUndiscovered = 1;
            // tier-down fallback (e.g. Grass for a Carrot cow)
            public const int OtherLowerTierSuccessPercent = 40;
            public const int OtherLowerTierYieldDiscovered = 1;
            public const int OtherLowerTierYieldUndiscovered = 1;
        }
        public static bool DepressionEnabled { get; private set; } = false;
        public static int TwinChancePercent { get; private set; } = 1;
        public static int BreedInheritParentChancePercent { get; private set; } = 25;
        public static int SecondaryPreferenceChancePercent { get; private set; } = 20;
        public static int MaxHorny { get; private set; } = 7200/10;
        // Per-cow MaxHorny = MaxHorny * (HornyExhaustBaseline / cow.MaxExhaust)^HornyExhaustCurve.
        // A cow with MaxExhaust == HornyExhaustBaseline always fills in exactly MaxHorny ticks.
        // HornyExhaustCurve controls steepness: 1.0 = linear, 2.0 = quadratic (current default —
        // strong cows fill much faster, weak cows much slower), 0.5 = square-root (gentler spread).
        public static int HornyExhaustBaseline { get; private set; } = 66;
        public static float HornyExhaustCurve { get; private set; } = 0.35f;
        public static int HornyPerMilkClick { get; private set; } = 150;
        public static int AttackCatchDistanceSq { get; private set; } = 4;
        public static int HornyOffscreenIndicatorThresholdPercent { get; private set; } = 75;
        // Slightly below player sprint (22) so the player must actually run to escape.
        public static float AttackChaseSpeed { get; private set; } = 8f;
        public static float DefaultMaxSpeed { get; private set; } = 10f;
        // Finisher leap: once the chasing cow is within JumpTriggerDistance it freezes for a
        // windup, then arcs onto the player and always lands the catch.
        public static float AttackJumpTriggerDistance { get; private set; } = 8f;
        public static int AttackJumpWindupTicks { get; private set; } = 24;
        public static int AttackJumpLeapTicks { get; private set; } = 24;
        // World-space distance the cow stops short of the player after a catch — keeps them
        // visually beside each other instead of overlapping while CaughtSystem pins them.
        public static float CaughtStandoffDistance { get; private set; } = 1f;
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
        // Cooldown is tied to the day cycle: house finishes "charging" exactly as
        // the visual day reaches evening, so the sleep button comes back online
        // when it actually looks like night-time. Single-tick offset keeps the
        // first sleep available immediately after match start.
        public static int SleepCooldownTicks => System.Math.Max(1, Day.LengthTicks);
        public static int ClickToSkipTicks { get; private set; } = 60;
        // Total ticks the player is in the "sleeping" state — drives client fade-in/hold/fade-out.
        // Day advance fires at the midpoint, so the world swap is hidden behind a full-black hold.
        public static int SleepStateTicks { get; private set; } = 150; // ~2.5s @ 60 TPS
    }

    public static class Day
    {
        // Nominal day length in ticks — drives the visual day/night lerp on the client
        // and the food-spawn window on the server. Independent of when the player actually
        // sleeps (sleeping always advances the day).
        public static int LengthTicks { get; private set; } = 60 * 60 * 2; // 1 min @ 60 TPS
        // Fraction of the day during which food can spawn, measured from the start of the day.
        // 0.5 = food grows during the first half only, 1.0 = grows all day.
        public static float FoodSpawnFraction { get; private set; } = 0.1f;
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
            public static int BasePerDay { get; private set; } = 25;
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
        /// <summary>Extra ticks added between cow.Horny ticks per pet assigned to that cow. 1 pet + value 1 = horny accrues at half rate.</summary>
        public static int HornySlowTicksPerPet { get; private set; } = 2;
    }

    public static class FoodSpawn
    {
        // Minimum gap between spawn attempts. Auto-spread math (<see cref="IntervalTicksForCap"/>)
        // still clamps to this lower bound so a very large cap doesn't churn the system every tick.
        public static int MinIntervalTicks { get; private set; } = 60;

        /// <summary>
        /// Auto-spread: returns the tick interval needed to evenly distribute <paramref name="cap"/>
        /// spawn attempts across the day's food-spawn window. Lets balance be defined in terms of
        /// "N items per day" without hand-tuning IntervalTicks.
        /// </summary>
        public static int IntervalTicksForCap(int cap)
        {
            if (cap <= 0) return int.MaxValue;
            int window = (int)(Day.LengthTicks * Day.FoodSpawnFraction);
            if (window <= 0) return MinIntervalTicks;
            return System.Math.Max(MinIntervalTicks, window / cap);
        }
        public static int MaxSpawnAttempts { get; private set; } = 10;
        // Food spawns inside a ring around already-unlocked land plots so the player
        // doesn't have to scour the empty edges of the map. Falls back to the wider
        // [MinPos..MaxPos] box if no anchors exist (shouldn't happen — there's always
        // the player house).
        public static int AnchorRadius { get; private set; } = 12;
        public static int AnchorMinDistance { get; private set; } = 2;
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
        public static int BasePriceMultiplier { get; private set; } = 5;
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
