using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared;

public static class StarGrid
{
    public const float GridStep = 10f;
    public const float OuterRadius = 90f;
    public const float InnerRadius = 32f;
    private const int StarPoints = 5;

    /// <summary>
    /// Minimum grid distance (Manhattan) between special buildings (LoveHouse, SellPoint).
    /// Ensures ~10-15 houses between each special building.
    /// </summary>
    private const int MinSpecialDistance = 4; // 4 grid steps ≈ 12-16 houses between specials

    // Fixed special grid positions that become specific structure types
    public static int? GetFixedType(int gx, int gy)
    {
        if (gx == 0 && gy == 0) return LandType.SellPoint;
        if (gx == 1 && gy == 0) return LandType.LoveHouse; // Direct neighbor of starting sell point
        if (gx == 0 && gy == -3) return LandType.FinalStructure;
        return null;
    }

    public static bool IsInsideStar(float px, float py)
    {
        float startAngle = -(float)System.Math.PI / 2f;
        bool inside = false;

        for (int i = 0, j = StarPoints * 2 - 1; i < StarPoints * 2; j = i++)
        {
            float ai = startAngle + i * (float)System.Math.PI / StarPoints;
            float aj = startAngle + j * (float)System.Math.PI / StarPoints;
            float ri = (i % 2 == 0) ? OuterRadius : InnerRadius;
            float rj = (j % 2 == 0) ? OuterRadius : InnerRadius;

            float xi = (float)System.Math.Cos(ai) * ri;
            float yi = (float)System.Math.Sin(ai) * ri;
            float xj = (float)System.Math.Cos(aj) * rj;
            float yj = (float)System.Math.Sin(aj) * rj;

            if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                inside = !inside;
        }

        return inside;
    }

    public static int GetThreshold(int gx, int gy)
    {
        int gridDist = System.Math.Abs(gx) + System.Math.Abs(gy);
        int basePrice = System.Math.Max(1, gridDist);

        // Price multiplier per building type
        int type = GetBuildingType(gx, gy);
        int multiplier = GetPriceMultiplier(type);

        return basePrice * multiplier * 10;
    }

    // Food farm frequency: every Nth house-slot becomes a food farm instead
    private const int FoodFarmFreq = 5;

    public static int GetPriceMultiplier(int landType)
    {
        switch (landType)
        {
            case LandType.FinalStructure: return 50;
            case LandType.LoveHouse: return 3;
            case LandType.SellPoint: return 1;
            case LandType.CarrotFarm: return 2;
            case LandType.AppleOrchard: return 2;
            case LandType.MushroomCave: return 2;
            case LandType.HelperGatherer: return 5;
            case LandType.HelperSeller: return 5;
            case LandType.HelperBuilder: return 5;
            default: return 1; // House
        }
    }

    // Pre-computed special building positions on a sparse grid.
    // Uses deterministic hash to place LoveHouses and SellPoints with guaranteed min distance.
    private static readonly System.Collections.Generic.HashSet<(int, int)> _specialPositions = new();
    private static bool _specialsComputed;

    private static void EnsureSpecialsComputed()
    {
        if (_specialsComputed) return;
        _specialsComputed = true;

        // Scan the full grid and assign specials with minimum distance
        int maxCoord = (int)(OuterRadius / GridStep) + 1;

        // Collect all valid grid positions sorted by distance from center
        var candidates = new System.Collections.Generic.List<(int gx, int gy, int dist)>();
        for (int gy = -maxCoord; gy <= maxCoord; gy++)
            for (int gx = -maxCoord; gx <= maxCoord; gx++)
            {
                if (!IsInsideStar(gx * GridStep, gy * GridStep)) continue;
                if (GetFixedType(gx, gy).HasValue) continue; // skip fixed positions
                candidates.Add((gx, gy, System.Math.Abs(gx) + System.Math.Abs(gy)));
            }
        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

        // Place specials ensuring minimum distance between them
        // Include fixed positions as already-placed specials
        var placed = new System.Collections.Generic.List<(int gx, int gy)>();
        placed.Add((0, 0));   // SellPoint at center
        placed.Add((1, 0));   // LoveHouse (neighbor of sell point)
        placed.Add((0, -3));  // FinalStructure

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

            _specialPositions.Add((gx, gy));
            placed.Add((gx, gy));
        }
    }

    public static int GetBuildingType(int gx, int gy)
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

        // Every Nth remaining slot becomes a food farm (cycling through Carrot, Apple, Mushroom)
        int hash2 = System.Math.Abs(gx * 31337 + gy * 65537);
        if (hash2 % FoodFarmFreq == 0)
        {
            int farmKind = hash2 % 3; // 0=Carrot, 1=Apple, 2=Mushroom
            return farmKind switch
            {
                0 => LandType.CarrotFarm,
                1 => LandType.AppleOrchard,
                _ => LandType.MushroomCave
            };
        }

        // Rare gatherer shops (~1 in 20 remaining slots)
        int hash3 = System.Math.Abs(gx * 48271 + gy * 99991);
        if (hash3 % 20 == 0)
        {
            return LandType.HelperGatherer;
        }

        return LandType.House;
    }

    /// <summary>
    /// Try to spawn a land plot at grid coords (gx, gy).
    /// Returns true if spawned, false if position is invalid or occupied.
    /// </summary>
    public static bool TrySpawnLand(Context ctx, int gx, int gy)
    {
        float px = gx * GridStep;
        float py = gy * GridStep;
        if (!IsInsideStar(px, py)) return false;

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
                 ctx.State.HasComponent<FoodFarmComponent>(entity)))
            {
                var pos = ctx.State.GetComponent<Transform2D>(entity).Position;
                float dx = (float)(pos.X - px);
                float dy = (float)(pos.Y - py);
                if (dx * dx + dy * dy < 1f) return false;
            }
        }

        int threshold = GetThreshold(gx, gy);
        int type = GetBuildingType(gx, gy);
        LandDefinition.Create(ctx, new Vector2(px, py), threshold, type, gx, gy, 0);
        return true;
    }

    /// <summary>
    /// Spawn land plots at the 4 cardinal neighbors of the given grid position.
    /// </summary>
    public static void SpawnNeighbors(Context ctx, int gx, int gy)
    {
        TrySpawnLand(ctx, gx + 1, gy);
        TrySpawnLand(ctx, gx - 1, gy);
        TrySpawnLand(ctx, gx, gy + 1);
        TrySpawnLand(ctx, gx, gy - 1);
    }
}
