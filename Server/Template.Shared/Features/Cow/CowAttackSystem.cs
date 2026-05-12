using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

public class CowAttackSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        Entity player = Entity.Null;
        Vector2 playerPos = default;
        foreach (var e in state.Filter<PlayerEntity>())
        {
            if (state.HasComponent<HelperPlayerComponent>(e)) continue;
            if (!state.TryGetComponent<Transform2D>(e, out var t)) continue;
            player = e;
            playerPos = t.Position;
            break;
        }
        if (player == Entity.Null) return;

        Float catchDistSq = (Float)Balance.Cow.AttackCatchDistanceSq;
        bool caught = false;

        foreach (var cowRef in state.Filter<CowArchetype>())
        {
            if (!cowRef.Cow.IsAttacking) continue;

            var cowPos = cowRef.Transform2D.Position;
            var diff = cowPos - playerPos;
            if (diff.SqrMagnitude < catchDistSq)
            {
                caught = true;
                continue;
            }

            // Stop avoiding the player while hunting — the whole point is to ram into them.
            cowRef.NavigationAgent2D.AvoidanceEnabled = false;
            cowRef.WalkTo(playerPos, arrivalDistance: (Float)0f);
        }

        if (caught)
        {
            state.AddComponent(player, new EnterStateComponent { Key = StateKeys.GameOver, Param = "caught", Age = 0 });
        }
    }
}
