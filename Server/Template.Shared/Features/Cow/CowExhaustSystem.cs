using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared.Systems;

public class CowExhaustSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var cowRef in state.Filter<CowArchetype>())
        {
            if (!cowRef.Cow.IsExhausted) continue;
            if (cowRef.Cow.HouseId == Entity.Null) continue;
            if (!CowFollowSystem.TryGetHouseStandPosition(state, cowRef, out var standPos)) continue;
            var distSq = (standPos - cowRef.Transform2D.Position).SqrMagnitude;
            if (distSq > (Float)0.01f)
                cowRef.WalkTo(standPos, arrivalDistance: (Float)0.1f);
            else
                cowRef.StopMoving();
        }
    }
}
