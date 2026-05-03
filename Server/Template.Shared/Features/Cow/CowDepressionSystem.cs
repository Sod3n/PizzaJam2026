using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Components;

namespace Template.Shared.Systems;

// Ticks the depression countdown on cows that failed a breed. CowVisibilitySystem reads
// cow.IsDepressed to decide whether to keep them visible (depressed cows stay visible but
// can't be interacted with).
public class CowDepressionSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            if (!cow.IsDepressed) continue;

            if (cow.DepressionTicksRemaining > 0)
                cow.DepressionTicksRemaining--;

            if (cow.DepressionTicksRemaining <= 0)
            {
                cow.IsDepressed = false;
                cow.DepressionTicksRemaining = 0;
                ILogger.Log($"[CowDepressionSystem] Cow {cowEntity.Id} recovered from depression");
            }
        }
    }
}
