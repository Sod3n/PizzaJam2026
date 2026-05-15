using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class SleepingPlayerSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        System.Collections.Generic.List<Entity> done = null;

        foreach (var playerEntity in state.Filter<SleepingComponent>())
        {
            ref var s = ref state.GetComponent<SleepingComponent>(playerEntity);

            int half = s.TotalTicks / 2;
            if (s.DayAdvanced == 0 && s.TicksRemaining <= half)
            {
                SleepLogic.AdvanceDay(state);

                var houseEntity = s.House;
                if (state.HasComponent<CooldownComponent>(houseEntity))
                {
                    ref var cd = ref state.GetComponent<CooldownComponent>(houseEntity);
                    cd.MaxTicks = PlayerHouseComponent.SleepCooldownTicks;
                    cd.TicksRemaining = PlayerHouseComponent.SleepCooldownTicks;
                    cd.Unit = CooldownUnit.Ticks;
                }
                else
                {
                    state.AddComponent(houseEntity, new CooldownComponent
                    {
                        MaxTicks = PlayerHouseComponent.SleepCooldownTicks,
                        TicksRemaining = PlayerHouseComponent.SleepCooldownTicks,
                        Unit = CooldownUnit.Ticks,
                    });
                }
                s.DayAdvanced = 1;
            }

            if (s.TicksRemaining <= 0)
            {
                (done ??= new System.Collections.Generic.List<Entity>()).Add(playerEntity);
            }
            else
            {
                s.TicksRemaining--;
            }
        }

        if (done != null)
        {
            foreach (var e in done)
            {
                state.UnhideEntity(e);
                state.RemoveComponent<SleepingComponent>(e);
            }
        }
    }
}
