using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class DayTimeSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            gr.TicksSinceDayStart++;
            break;
        }
    }
}
