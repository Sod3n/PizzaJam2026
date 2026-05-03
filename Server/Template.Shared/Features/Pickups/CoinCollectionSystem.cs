using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class CoinCollectionSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        // Iterate coins in deterministic order (Filter is already deterministic by entity ID)
        foreach (var coinEntity in state.Filter<CoinComponent, Area2D>())
        {
            ref var area = ref state.GetComponent<Area2D>(coinEntity);

            if (!area.HasOverlappingBodies) continue;

            for (int i = 0; i < area.OverlappingEntities.Count; i++)
            {
                int targetId = area.OverlappingEntities[i];
                var targetEntity = new Entity(targetId);

                // Check if the overlapping entity is a player
                if (state.HasComponent<PlayerEntity>(targetEntity))
                {
                    // Get Component data
                    ref var coin = ref state.GetComponent<CoinComponent>(coinEntity);
                    ref var score = ref state.GetComponent<ScoreComponent>(targetEntity);

                    // Apply Logic
                    score.Value += coin.Value;

                    // Destroy coin
                    state.DeleteEntity(coinEntity);

                    // Stop processing this coin since it's destroyed
                    break;
                }
            }
        }
    }
}

