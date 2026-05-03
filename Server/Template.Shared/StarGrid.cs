using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared;

public static class StarGrid
{
    public const float GridStep = 12.6f;
    public const float OuterRadius = 90f;
    public const float InnerRadius = 25f;
    private const int StarPoints = 5;

    private static readonly Float GridStepF = (Float)GridStep;
    private static readonly Float OuterRadiusF = (Float)OuterRadius;
    private static readonly Float InnerRadiusF = (Float)InnerRadius;

    /// <summary>
    /// Minimum grid distance (Manhattan) between special buildings (LoveHouse, SellPoint).
    /// Ensures ~10-15 houses between each special building.
    /// </summary>
    private const int MinSpecialDistance = 3; // 3 grid steps between specials (thin star arms)

    // Fixed positions: only economy bootstrap buildings that MUST be at known locations.
    // Farms are spawned dynamically (see DynamicSpecials) so they scatter across the star.
    public static LandType? GetFixedType(int gx, int gy)
    {
        if (gx == 0 && gy == 0) return LandType.PlayerHouse; // Center: sleep to advance day
        if (gx == 0 && gy == 1) return LandType.SellPoint;   // Shifted north — opposite the win direction
        if (gx == 1 && gy == 0) return LandType.LoveHouse;
        // Final structure — fixed at center-south, dist 7 forces expansion through all farm tiers
        if (gx == 0 && gy == -7) return LandType.FinalStructure;
        return null;
    }

    // Pre-computed star vertices for deterministic point-in-polygon test
    private static readonly Vector2[] StarVertices;

    static StarGrid()
    {
        int vertCount = StarPoints * 2;
        StarVertices = new Vector2[vertCount];
        Float startAngle = -Float.Pi / (Float)2;

        for (int i = 0; i < vertCount; i++)
        {
            Float angle = startAngle + (Float)i * Float.Pi / (Float)StarPoints;
            Float radius = (i % 2 == 0) ? OuterRadiusF : InnerRadiusF;
            StarVertices[i] = new Vector2(Float.Cos(angle) * radius, Float.Sin(angle) * radius);
        }
    }

    public static bool IsInsideStar(float px, float py)
    {
        return IsInsideStarF((Float)px, (Float)py);
    }

    public static bool IsInsideStarF(Float px, Float py)
    {
        bool inside = false;
        int vertCount = StarVertices.Length;

        for (int i = 0, j = vertCount - 1; i < vertCount; j = i++)
        {
            Float xi = StarVertices[i].X, yi = StarVertices[i].Y;
            Float xj = StarVertices[j].X, yj = StarVertices[j].Y;

            if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                inside = !inside;
        }

        return inside;
    }

    public static int GetThreshold(int gx, int gy)
    {
        int gridDist = System.Math.Max(1, System.Math.Abs(gx) + System.Math.Abs(gy));
        var type = GetBuildingType(gx, gy);
        int priceMult = GetPriceMultiplier(type);
        if (priceMult < 0) // Decoration: quarter cost
            return gridDist * GetEraMultiplier(gridDist) * GameData.Balance.Build.BasePriceMultiplier / 4;
        return gridDist * GetEraMultiplier(gridDist) * priceMult * GameData.Balance.Build.BasePriceMultiplier;
    }

    // Food farm frequency: every Nth house-slot becomes a food farm instead
    private const int FoodFarmFreq = 5;

    public static int GetPriceMultiplier(LandType landType)
    {
        switch (landType)
        {
            case LandType.FinalStructure: return 4;
            case LandType.LoveHouse: return 2;
            case LandType.SellPoint: return 1;
            case LandType.CarrotFarm: return 1;
            case LandType.AppleOrchard: return 1;
            case LandType.MushroomCave: return 1;
            case LandType.HelperAssistant: return 1;
            case LandType.Warehouse: return 2;
            case LandType.PlayerHouse: return 1;
            case LandType.Decoration: return -1; // half cost (handled in GetThreshold)
            case LandType.Library: return 1;
            case LandType.Smithy: return 3;
            default: return 1; // House
        }
    }

    /// <summary>
    /// Era pricing for general-milk economy. Food tiers satisfy later cows rather than
    /// multiplying milk value, so distance scaling stays modest and helper/pet unlocks
    /// provide the main progression pressure.
    /// </summary>
    public static int GetEraMultiplier(int gridDist)
    {
        if (gridDist >= 6) return GameData.Balance.Build.EraMultiplier_Ring6Plus;
        if (gridDist >= 5) return GameData.Balance.Build.EraMultiplier_Ring5;
        if (gridDist >= 4) return GameData.Balance.Build.EraMultiplier_Ring4;
        if (gridDist >= 3) return GameData.Balance.Build.EraMultiplier_Ring3;
        return GameData.Balance.Build.EraMultiplier_RingDefault;
    }

    // ─── Dynamic special buildings: spawn at player's expansion frontier ───

    /// <summary>
    /// Split grid into 4 quadrants for angular separation.
    /// Ensures consecutive farms spawn in different directions.
    /// </summary>
    public static int GetQuadrant(int gx, int gy)
    {
        if (gx > 0 && gy >= 0) return 0;
        if (gx <= 0 && gy > 0) return 1;
        if (gx < 0 && gy <= 0) return 2;
        return 3;
    }

    /// <summary>
    /// Special buildings that spawn dynamically when the player reaches a grid distance.
    /// Each spawns exactly once (tracked via GlobalResourcesComponent.SpawnedSpecials bitmask).
    /// Farms use angular separation: each new farm must be in a different quadrant than the last,
    /// rewarding players who expand in multiple directions.
    /// </summary>
    private static readonly (LandType type, int triggerDist, int bit, bool isFarm)[] DynamicSpecials =
    {
        // Farms — each appears 1 era BEFORE it's needed (affordable with current-era income)
        (LandType.CarrotFarm,    2, 4, true),
        (LandType.CarrotFarm,    3, 5, true),
        (LandType.AppleOrchard,  4, 6, true),
        (LandType.AppleOrchard,  5, 7, true),
        (LandType.MushroomCave,  6, 8, true),
        (LandType.MushroomCave,  7, 9, true),
        // post-pet-refactor cleanup complete — HelperAssistant is the unified generic pet building.
        (LandType.HelperAssistant, 2, 0, false),
        (LandType.HelperAssistant, 5, 14, false),
        // Warehouse — mid-early game, helpers auto-deposit resources
        (LandType.Warehouse,    3, 15, false),
        // Second sell point — mid game expansion
        (LandType.SellPoint,    5, 12, false),
        // Love houses
        (LandType.LoveHouse,    4, 13, false),
        (LandType.LoveHouse,    3, 11, false),
    };

    /// <summary>
    /// How far past the trigger distance before we relax the farm quadrant constraint.
    /// Guarantees farms always spawn even if the player expands in only one direction.
    /// </summary>
    private const int FarmQuadrantRelaxDist = 2;

    /// <summary>
    /// Check if this grid position should become a special building.
    /// Returns the LandType if yes, null if no.
    /// </summary>
    private static LandType? TryGetSpecialType(EntityWorld state, int gx, int gy)
    {
        int dist = System.Math.Abs(gx) + System.Math.Abs(gy);

        // Find the GlobalResources to check/update the spawned bitmask
        Entity grEntity = Entity.Null;
        foreach (var e in state.Filter<GlobalResourcesComponent>())
        { grEntity = e; break; }
        if (grEntity == Entity.Null) return null;

        ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);

        foreach (var (type, triggerDist, bit, isFarm) in DynamicSpecials)
        {
            if (dist < triggerDist) continue;
            if ((gr.SpawnedSpecials & (1 << bit)) != 0) continue;

            // Helper/upgrade buildings only spawn when helpers are enabled
            // (LoveHouse and SellPoint are not farms but also not helper-gated)
            if (!isFarm && type != LandType.LoveHouse && type != LandType.SellPoint && gr.HelpersEnabled == 0) continue;

            // Farms require angular separation: different quadrant than last farm.
            // First farm (LastFarmGX/GY both 0) has no constraint.
            // Relax constraint when far enough past trigger distance to guarantee placement.
            if (isFarm && (gr.LastFarmGX != 0 || gr.LastFarmGY != 0))
            {
                bool relaxed = dist >= triggerDist + FarmQuadrantRelaxDist;
                if (!relaxed && GetQuadrant(gx, gy) == GetQuadrant(gr.LastFarmGX, gr.LastFarmGY))
                    continue;
            }

            gr.SpawnedSpecials |= (1 << bit);
            if (isFarm)
            {
                gr.LastFarmGX = gx;
                gr.LastFarmGY = gy;
            }
            return type;
        }
        return null;
    }

    // Pre-computed special building positions on a sparse grid.
    // Uses deterministic hash to place LoveHouses and SellPoints with guaranteed min distance.
    private static System.Collections.Generic.HashSet<(int, int)> _specialPositions;
    private static readonly object _specialLock = new();

    private static void EnsureSpecialsComputed()
    {
        if (_specialPositions != null) return;
        lock (_specialLock)
        {
            if (_specialPositions != null) return;
            _specialPositions = ComputeSpecialPositions();
        }
    }

    private static System.Collections.Generic.HashSet<(int, int)> ComputeSpecialPositions()
    {
        var result = new System.Collections.Generic.HashSet<(int, int)>();

        // Scan the full grid and assign specials with minimum distance
        int maxCoord = (int)(OuterRadius / GridStep) + 1;

        // Collect all valid grid positions sorted by distance from center
        var candidates = new System.Collections.Generic.List<(int gx, int gy, int dist)>();
        for (int gy = -maxCoord; gy <= maxCoord; gy++)
            for (int gx = -maxCoord; gx <= maxCoord; gx++)
            {
                if (!IsInsideStarF((Float)gx * GridStepF, (Float)gy * GridStepF)) continue;
                if (GetFixedType(gx, gy).HasValue) continue; // skip fixed positions
                candidates.Add((gx, gy, System.Math.Abs(gx) + System.Math.Abs(gy)));
            }
        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

        // Place specials ensuring minimum distance between them
        // Include fixed positions as already-placed specials
        var placed = new System.Collections.Generic.List<(int gx, int gy)>();
        placed.Add((0, 0));   // PlayerHouse at center
        placed.Add((0, 1));   // SellPoint (shifted north)
        placed.Add((1, 0));   // LoveHouse (neighbor of player house)
        placed.Add((0, -7));  // FinalStructure

        foreach (var (gx, gy, dist) in candidates)
        {
            // Use deterministic hash to decide if this position WANTS to be special
            int hash = System.Math.Abs(gx * 7919 + gy * 104729);
            if (hash % 12 != 0) continue; // ~1 in 12 candidates wants to be special

            // Check minimum distance to all existing specials
            bool tooClose = false;
            foreach (var (sx, sy) in placed)
            {
                if (System.Math.Abs(gx - sx) + System.Math.Abs(gy - sy) < MinSpecialDistance)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            result.Add((gx, gy));
            placed.Add((gx, gy));
        }
        return result;
    }

    public static LandType GetBuildingType(int gx, int gy)
    {
        // Check for fixed special positions first
        var fixedType = GetFixedType(gx, gy);
        if (fixedType.HasValue) return fixedType.Value;

        EnsureSpecialsComputed();

        if (_specialPositions.Contains((gx, gy)))
        {
            // Alternate between LoveHouse and SellPoint based on hash
            int hash = System.Math.Abs(gx * 7919 + gy * 104729);
            return (hash % 3 == 0) ? LandType.SellPoint : LandType.LoveHouse;
        }

        return LandType.House;
    }

    /// <summary>
    /// Try to spawn a land plot at grid coords (gx, gy).
    /// Returns true if spawned, false if position is invalid or occupied.
    /// </summary>
    public static bool TrySpawnLand(Context ctx, int gx, int gy)
    {
        Float px = (Float)gx * GridStepF;
        Float py = (Float)gy * GridStepF;
        if (!IsInsideStarF(px, py)) return false;

        // Check if a land plot already exists at these grid coords
        foreach (var entity in ctx.State.Filter<LandComponent>())
        {
            var lc = ctx.State.GetComponent<LandComponent>(entity);
            if (lc.Arm == gx && lc.Ring == gy) return false;
        }

        // Also check if a building already exists at this position (sell point, house, etc.)
        foreach (var entity in ctx.State.Filter<Transform2D>())
        {
            if (!ctx.State.HasComponent<LandComponent>(entity) &&
                (ctx.State.HasComponent<HouseComponent>(entity) ||
                 ctx.State.HasComponent<LoveHouseComponent>(entity) ||
                 ctx.State.HasComponent<SellPointComponent>(entity) ||
                 ctx.State.HasComponent<FinalStructureComponent>(entity) ||
                 ctx.State.HasComponent<CarrotFarmComponent>(entity) ||
                 ctx.State.HasComponent<AppleOrchardComponent>(entity) ||
                 ctx.State.HasComponent<MushroomCaveComponent>(entity) ||
                 ctx.State.HasComponent<HelperAssistantComponent>(entity) ||
                 ctx.State.HasComponent<DecorationComponent>(entity) ||
                 ctx.State.HasComponent<WarehouseComponent>(entity)))
            {
                var pos = ctx.State.GetComponent<Transform2D>(entity).Position;
                var diff = pos - new Vector2(px, py);
                if (diff.SqrMagnitude < Float.One) return false;
            }
        }

        var type = GetBuildingType(gx, gy);

        // Dynamic specials: override type if this distance triggers a special building
        // Never override fixed positions (SellPoint, LoveHouse, FinalStructure)
        if (!GetFixedType(gx, gy).HasValue)
        {
            var specialType = TryGetSpecialType(ctx.State, gx, gy);
            if (specialType.HasValue)
                type = specialType.Value;
        }

        int gridDist = System.Math.Max(1, System.Math.Abs(gx) + System.Math.Abs(gy));
        int pm = GetPriceMultiplier(type);
        int threshold = pm < 0
            ? gridDist * GetEraMultiplier(gridDist) * GameData.Balance.Build.BasePriceMultiplier / 4
            : gridDist * GetEraMultiplier(gridDist) * pm * GameData.Balance.Build.BasePriceMultiplier;
        LandDefinition.Create(ctx, new Vector2(px, py), threshold, type, gx, gy, 0);
        return true;
    }

    /// <summary>
    /// Whole-ring gating: spawn the entire ring at <paramref name="ringDist"/> only when no
    /// unbuilt LandComponent plots remain at <paramref name="ringDist"/>-1.
    /// </summary>
    public static void SpawnRingIfPriorComplete(Context ctx, int ringDist)
    {
        if (ringDist <= 0) return;

        int priorRing = ringDist - 1;
        foreach (var entity in ctx.State.Filter<LandComponent>())
        {
            var lc = ctx.State.GetComponent<LandComponent>(entity);
            int d = System.Math.Abs(lc.Arm) + System.Math.Abs(lc.Ring);
            if (d == priorRing) return;
        }

        int maxCoord = ringDist;
        for (int gx = -maxCoord; gx <= maxCoord; gx++)
        {
            int gy = ringDist - System.Math.Abs(gx);
            TrySpawnLand(ctx, gx, gy);
            if (gy != 0)
                TrySpawnLand(ctx, gx, -gy);
        }
    }

    /// <summary>Forwarding stub: when a plot at (gx,gy) completes, attempt to spawn the next ring.</summary>
    public static void SpawnNeighbors(Context ctx, int gx, int gy)
    {
        int dist = System.Math.Abs(gx) + System.Math.Abs(gy);
        SpawnRingIfPriorComplete(ctx, dist + 1);
    }

    // Global build-count limits. A plot only counts toward the limit once it's been
    // *committed* (CurrentCoins > 0 → cycling is locked) or actually built into a
    // standing building. Plots that have merely been cycled to the type without any
    // investment don't reserve a slot — the player can keep window-shopping.
    private static int CountWorldType(EntityWorld state, LandType landType, int excludeGx, int excludeGy)
    {
        int count = 0;
        // Built buildings
        switch (landType)
        {
            case LandType.CarrotFarm:
                foreach (var _ in state.Filter<CarrotFarmComponent>()) count++;
                break;
            case LandType.AppleOrchard:
                foreach (var _ in state.Filter<AppleOrchardComponent>()) count++;
                break;
            case LandType.MushroomCave:
                foreach (var _ in state.Filter<MushroomCaveComponent>()) count++;
                break;
            case LandType.Warehouse:
                foreach (var _ in state.Filter<WarehouseComponent>()) count++;
                break;
            case LandType.HelperAssistant:
                foreach (var _ in state.Filter<HelperAssistantComponent>()) count++;
                break;
            case LandType.Library:
                foreach (var _ in state.Filter<LibraryComponent>()) count++;
                break;
        }
        // Locked-in plots (player has invested at least 1 coin → cycling is rejected),
        // excluding the plot doing the query.
        foreach (var le in state.Filter<LandComponent>())
        {
            var lc = state.GetComponent<LandComponent>(le);
            if (lc.Arm == excludeGx && lc.Ring == excludeGy) continue;
            if (lc.Type == landType && lc.CurrentCoins > 0) count++;
        }
        return count;
    }

    private static bool TransformIsAtRing(EntityWorld state, Entity e, int ringDist)
    {
        if (!state.HasComponent<Transform2D>(e)) return false;
        var pos = state.GetComponent<Transform2D>(e).Position;
        int gx = (int)System.Math.Round((float)pos.X / GridStep);
        int gy = (int)System.Math.Round((float)pos.Y / GridStep);
        return System.Math.Abs(gx) + System.Math.Abs(gy) == ringDist;
    }

    private static int CountInRing(EntityWorld state, LandType landType, int ringDist, int excludeGx, int excludeGy)
    {
        int count = 0;
        // Built buildings at this ring — gridify their world position back to grid coords.
        switch (landType)
        {
            case LandType.HelperAssistant:
                foreach (var e in state.Filter<HelperAssistantComponent>())
                    if (TransformIsAtRing(state, e, ringDist)) count++;
                break;
            case LandType.CarrotFarm:
                foreach (var e in state.Filter<CarrotFarmComponent>())
                    if (TransformIsAtRing(state, e, ringDist)) count++;
                break;
            case LandType.AppleOrchard:
                foreach (var e in state.Filter<AppleOrchardComponent>())
                    if (TransformIsAtRing(state, e, ringDist)) count++;
                break;
            case LandType.MushroomCave:
                foreach (var e in state.Filter<MushroomCaveComponent>())
                    if (TransformIsAtRing(state, e, ringDist)) count++;
                break;
            case LandType.Warehouse:
                foreach (var e in state.Filter<WarehouseComponent>())
                    if (TransformIsAtRing(state, e, ringDist)) count++;
                break;
            case LandType.Library:
                foreach (var e in state.Filter<LibraryComponent>())
                    if (TransformIsAtRing(state, e, ringDist)) count++;
                break;
            case LandType.LoveHouse:
                foreach (var e in state.Filter<LoveHouseComponent>())
                    if (TransformIsAtRing(state, e, ringDist)) count++;
                break;
            case LandType.SellPoint:
                foreach (var e in state.Filter<SellPointComponent>())
                    if (TransformIsAtRing(state, e, ringDist)) count++;
                break;
        }
        // Locked-in plots in this ring (CurrentCoins > 0).
        foreach (var le in state.Filter<LandComponent>())
        {
            var lc = state.GetComponent<LandComponent>(le);
            if (lc.Arm == excludeGx && lc.Ring == excludeGy) continue;
            if (System.Math.Abs(lc.Arm) + System.Math.Abs(lc.Ring) != ringDist) continue;
            if (lc.Type == landType && lc.CurrentCoins > 0) count++;
        }
        return count;
    }

    /// <summary>
    /// Returns true when both the world and per-ring caps still have room for one more
    /// of the given <paramref name="landType"/>. -1 on either side means uncapped.
    /// </summary>
    private static bool PassesLimits(EntityWorld state, LandType landType, int worldLimit, int ringLimit, int ringDist, int gx, int gy)
    {
        if (worldLimit >= 0 && CountWorldType(state, landType, gx, gy) >= worldLimit) return false;
        if (ringLimit >= 0 && CountInRing(state, landType, ringDist, gx, gy) >= ringLimit) return false;
        return true;
    }

    /// <summary>
    /// Pool of LandTypes the player can cycle through on a sign at a given ring distance.
    /// Each type has an unlock-ring threshold + world cap (Balance.Build.Limit.*) +
    /// per-ring cap (Balance.Build.LimitPerRing.*). -1 on a cap means uncapped.
    /// </summary>
    public static LandType[] GetCycleableTypesForRing(EntityWorld state, int ringDist, int gx, int gy)
    {
        var list = new System.Collections.Generic.List<LandType> { LandType.House };

        if (ringDist >= GameData.Balance.Build.UnlockRing.Library
            && PassesLimits(state, LandType.Library, GameData.Balance.Build.Limit.Library.World, GameData.Balance.Build.Limit.Library.PerRing, ringDist, gx, gy))
            list.Add(LandType.Library);

        if (ringDist >= GameData.Balance.Build.UnlockRing.LoveHouse
            && PassesLimits(state, LandType.LoveHouse, GameData.Balance.Build.Limit.LoveHouse.World, GameData.Balance.Build.Limit.LoveHouse.PerRing, ringDist, gx, gy))
            list.Add(LandType.LoveHouse);

        if (ringDist >= GameData.Balance.Build.UnlockRing.SellPoint
            && PassesLimits(state, LandType.SellPoint, GameData.Balance.Build.Limit.SellPoint.World, GameData.Balance.Build.Limit.SellPoint.PerRing, ringDist, gx, gy))
            list.Add(LandType.SellPoint);

        if (ringDist >= GameData.Balance.Build.UnlockRing.CarrotFarm
            && PassesLimits(state, LandType.CarrotFarm, GameData.Balance.Build.Limit.CarrotFarm.World, GameData.Balance.Build.Limit.CarrotFarm.PerRing, ringDist, gx, gy))
            list.Add(LandType.CarrotFarm);

        if (ringDist >= GameData.Balance.Build.UnlockRing.AppleOrchard
            && PassesLimits(state, LandType.AppleOrchard, GameData.Balance.Build.Limit.AppleOrchard.World, GameData.Balance.Build.Limit.AppleOrchard.PerRing, ringDist, gx, gy))
            list.Add(LandType.AppleOrchard);

        if (ringDist >= GameData.Balance.Build.UnlockRing.MushroomCave
            && PassesLimits(state, LandType.MushroomCave, GameData.Balance.Build.Limit.MushroomCave.World, GameData.Balance.Build.Limit.MushroomCave.PerRing, ringDist, gx, gy))
            list.Add(LandType.MushroomCave);

        if (ringDist >= GameData.Balance.Build.UnlockRing.Warehouse
            && PassesLimits(state, LandType.Warehouse, GameData.Balance.Build.Limit.Warehouse.World, GameData.Balance.Build.Limit.Warehouse.PerRing, ringDist, gx, gy))
            list.Add(LandType.Warehouse);

        if (ringDist >= GameData.Balance.Build.UnlockRing.HelperAssistant
            && PassesLimits(state, LandType.HelperAssistant, GameData.Balance.Build.Limit.HelperAssistant.World, GameData.Balance.Build.Limit.HelperAssistant.PerRing, ringDist, gx, gy))
            list.Add(LandType.HelperAssistant);

        if (ringDist >= GameData.Balance.Build.UnlockRing.Smithy
            && PassesLimits(state, LandType.Smithy, GameData.Balance.Build.Limit.Smithy.World, GameData.Balance.Build.Limit.Smithy.PerRing, ringDist, gx, gy))
            list.Add(LandType.Smithy);

        return list.ToArray();
    }
}
