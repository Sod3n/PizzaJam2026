using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Shared.Systems;

// Click on the player's house → advance the day (sleep). On cooldown, each click subtracts a
// fixed chunk from the remaining time instead. Strategic action — main player only.
public class PlayerHouseSleepSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var houseEntity = req.Target;
            if (!state.HasComponent<PlayerHouseComponent>(houseEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            HandleSleep(state, playerEntity, houseEntity);
        }
    }

    private static void HandleSleep(EntityWorld state, Entity playerEntity, Entity houseEntity)
    {
        var ctx = new Context(state, playerEntity, null!);

        if (state.HasComponent<CooldownComponent>(houseEntity))
        {
            int remaining;
            {
                ref var cd = ref state.GetComponent<CooldownComponent>(houseEntity);
                remaining = cd.TicksRemaining;
            }
            if (remaining > 0)
            {
                {
                    ref var cd = ref state.GetComponent<CooldownComponent>(houseEntity);
                    cd.TicksRemaining = System.Math.Max(0, cd.TicksRemaining - Template.Shared.GameData.Balance.PlayerHouse.ClickToSkipTicks);
                }
                state.AddComponent(houseEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "cooldown_skip", Age = 0 });
                state.RemoveComponent<InteractRequestComponent>(playerEntity);
                return;
            }
        }

        SleepLogic.AdvanceDay(state);
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
        state.AddComponent(houseEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "sleep", Age = 0 });
        ILogger.Log($"[PlayerHouseSleepSystem] Player {playerEntity.Id} slept at PlayerHouse {houseEntity.Id} — day advanced");
        state.RemoveComponent<InteractRequestComponent>(playerEntity);
    }
}
