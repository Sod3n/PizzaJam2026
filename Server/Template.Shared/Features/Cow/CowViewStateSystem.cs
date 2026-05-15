using Deterministic.GameFramework.ECS;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

[UpdateOrder(100)]
public class CowViewStateSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        int alertThreshold = Balance.Cow.HornyOffscreenIndicatorThresholdPercent;

        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);

            cow.IsWanderer = cow.HouseId == Entity.Null && cow.FollowingPlayer == Entity.Null;
            cow.IsLoveTarget = false;
            cow.ShowLoveNeedIcon = false;

            bool visible = cow.Horny > 0 || cow.IsExhausted;
            if (!visible) cow.HornyIconState = HornyIconState.None;
            else if (cow.IsExhausted) cow.HornyIconState = HornyIconState.Exhausted;
            else if (cow.IsAttacking) cow.HornyIconState = HornyIconState.Attacking;
            else cow.HornyIconState = HornyIconState.Active;

            bool alerting = false;
            if (!state.HasComponent<HelperComponent>(cowEntity) && cow.MaxHorny > 0)
            {
                int pct = cow.Horny * 100 / cow.MaxHorny;
                alerting = pct >= alertThreshold;
            }
            cow.IsHornyAlerting = alerting;
        }

        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            var cow = state.GetComponent<CowComponent>(cowEntity);
            if (cow.LoveTarget == Entity.Null) continue;

            ref var lover = ref state.GetComponent<CowComponent>(cowEntity);
            lover.IsLoveTarget = true;
            lover.ShowLoveNeedIcon = lover.FollowingPlayer != Entity.Null && !lover.LoveConfessed;

            if (state.HasComponent<CowComponent>(cow.LoveTarget))
            {
                ref var target = ref state.GetComponent<CowComponent>(cow.LoveTarget);
                target.IsLoveTarget = true;
            }
        }
    }
}
