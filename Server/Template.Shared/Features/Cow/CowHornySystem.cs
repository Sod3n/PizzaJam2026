using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

[UpdateOrder(-100)]
public class CowHornySystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            if (state.HasComponent<HelperComponent>(cowEntity)) continue;

            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            if (cow.IsExhausted || cow.IsDepressed || cow.IsAttacking) continue;

            if (cow.Horny < cow.MaxHorny)
                cow.Horny++;

            if (cow.Horny >= cow.MaxHorny && cow.MaxHorny > 0)
            {
                cow.IsAttacking = true;
                state.AddComponent(cowEntity, new EnterStateComponent { Key = StateKeys.CowAttack, Param = "", Age = 0 });
            }
        }
    }
}
