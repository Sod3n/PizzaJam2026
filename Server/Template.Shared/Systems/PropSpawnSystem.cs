using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.DAR;

namespace Template.Shared.Systems;

public class PropSpawnSystem : ISystem
{
    private const int SpawnTick = 0;
    private const int PropCount = 150;
    private const float MinLandLabelBuffer = 2f; // Buffer so props don't spawn too close to land/buildings
    private const float MinPropDistance = 4f;    // Minimum distance between props
    private const float MinSameTypeDistance = 8f; // Minimum distance between same prop type (prevents clusters)
    private const uint Seed = 98765;

    // Spawn weights per prop type (higher = more common)
    // Barrel=0, Bush1=1, Bush2=2, Flowers=3, Tree=4
    private static readonly int[] SpawnWeights = { 1, 5, 4, 8, 2 };
    private static readonly int TotalWeight;

    static PropSpawnSystem()
    {
        TotalWeight = 0;
        for (int i = 0; i < SpawnWeights.Length; i++)
            TotalWeight += SpawnWeights[i];
    }

    public void Update(EntityWorld state)
    {
        var gameTime = state.GetCustomData<IGameTime>();
        if (gameTime == null || gameTime.CurrentTick != SpawnTick) return;

        // Only spawn once — check if any props already exist in this world
        foreach (var _ in state.Filter<PropComponent>())
            return;

        SpawnAllProps(state);
    }

    private void SpawnAllProps(EntityWorld state)
    {
        var context = new Context(state, Entity.Null, null!);
        var random = new DeterministicRandom(Seed);

        // Collect current land plot positions (small buffer so props don't sit on price labels)
        var landPositions = new System.Collections.Generic.List<Vector2>();
        foreach (var entity in state.Filter<LandComponent>())
        {
            var pos = state.GetComponent<Transform2D>(entity).Position;
            landPositions.Add(pos);
        }

        // Track placed prop positions (all and per-type for cluster prevention)
        var propPositions = new System.Collections.Generic.List<Vector2>();
        var propTypePositions = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Vector2>>();
        for (int i = 0; i < SpawnWeights.Length; i++)
            propTypePositions[i] = new System.Collections.Generic.List<Vector2>();

        int placed = 0;
        int attempts = 0;
        int maxAttempts = PropCount * 10;

        while (placed < PropCount && attempts < maxAttempts)
        {
            attempts++;

            // Generate random position within the star boundary
            float x = (float)random.NextFloat(new Float(-StarGrid.OuterRadius), new Float(StarGrid.OuterRadius));
            float y = (float)random.NextFloat(new Float(-StarGrid.OuterRadius), new Float(StarGrid.OuterRadius));

            // Must be inside the star shape
            if (!StarGrid.IsInsideStar(x, y)) continue;

            var candidatePos = new Vector2(x, y);

            // Small buffer around existing land plots (avoid overlapping price labels)
            if (IsTooClose(candidatePos, landPositions, MinLandLabelBuffer)) continue;

            // Check distance to other props
            if (IsTooClose(candidatePos, propPositions, MinPropDistance)) continue;

            // Pick weighted random prop type
            int propType = PickWeightedPropType(ref random);

            // Prevent clusters of same type
            if (IsTooClose(candidatePos, propTypePositions[propType], MinSameTypeDistance)) continue;

            var propEntity = propType switch
            {
                0 => BarrelDefinition.Create(context, candidatePos),
                1 => Bush1Definition.Create(context, candidatePos),
                2 => Bush2Definition.Create(context, candidatePos),
                3 => FlowersDefinition.Create(context, candidatePos),
                4 => TreeDefinition.Create(context, candidatePos),
                _ => PropDefinition.Create(context, candidatePos),
            };
            context.State.GetComponent<Components.PropComponent>(propEntity).PropType = propType;
            propPositions.Add(candidatePos);
            propTypePositions[propType].Add(candidatePos);
            placed++;
        }
    }

    private static bool IsTooClose(Vector2 candidate, System.Collections.Generic.List<Vector2> positions, float minDist)
    {
        float minDistSq = minDist * minDist;
        for (int i = 0; i < positions.Count; i++)
        {
            float dx = (float)(candidate.X - positions[i].X);
            float dy = (float)(candidate.Y - positions[i].Y);
            if (dx * dx + dy * dy < minDistSq) return true;
        }
        return false;
    }

    private static int PickWeightedPropType(ref DeterministicRandom random)
    {
        int roll = random.NextInt(TotalWeight);
        int cumulative = 0;
        for (int i = 0; i < SpawnWeights.Length; i++)
        {
            cumulative += SpawnWeights[i];
            if (roll < cumulative) return i;
        }
        return SpawnWeights.Length - 1;
    }
}
