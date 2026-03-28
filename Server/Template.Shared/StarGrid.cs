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

    // Per-arm LoveHouse frequency: arm 0=balanced, 1=breeding, 2=production, 3=mixed, 4=breeding
    private static readonly int[] LoveHouseFreq = { 6, 3, 10, 4, 2 };

    // Fixed special grid positions that become specific structure types
    public static int? GetFixedType(int gx, int gy)
    {
        if (gx == 0 && gy == 0) return LandType.SellPoint;
        // Place final structure and love house a few steps from center on different arms
        if (gx == 0 && gy == -3) return LandType.FinalStructure;
        if (gx == 3 && gy == 0) return LandType.LoveHouse;
        if (gx == -3 && gy == 0) return LandType.LoveHouse;
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

        return basePrice * multiplier;
    }

    public static int GetPriceMultiplier(int landType)
    {
        switch (landType)
        {
            case LandType.FinalStructure: return 50;
            case LandType.LoveHouse: return 3;
            case LandType.SellPoint: return 1;
            default: return 1; // House
        }
    }

    public static int GetBuildingType(int gx, int gy)
    {
        // Check for fixed special positions first
        var fixedType = GetFixedType(gx, gy);
        if (fixedType.HasValue) return fixedType.Value;

        float px = gx * GridStep;
        float py = gy * GridStep;
        float twoPi = 2f * (float)System.Math.PI;

        float angle = (float)System.Math.Atan2(py, px) + (float)System.Math.PI / 2f;
        if (angle < 0) angle += twoPi;
        int arm = (int)System.Math.Round(angle / (twoPi / 5f)) % 5;

        // Use a deterministic hash of grid coords for consistent type assignment
        int hash = System.Math.Abs(gx * 7919 + gy * 104729);
        return (hash % LoveHouseFreq[arm] == 0) ? LandType.LoveHouse : LandType.House;
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
                 ctx.State.HasComponent<FinalStructureComponent>(entity)))
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
