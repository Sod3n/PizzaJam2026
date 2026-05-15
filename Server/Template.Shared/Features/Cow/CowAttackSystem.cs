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
        // Sleeping/hidden players are unreachable — reaching the house is a safe escape.
        if (state.HasComponent<SleepingComponent>(player) || state.HasComponent<HiddenComponent>(player)) return;

        // While already caught, CaughtSystem owns the camera/positions — skip chase logic.
        if (state.HasComponent<CaughtComponent>(player)) return;

        Float catchDistSq = (Float)Balance.Cow.AttackCatchDistanceSq;
        Float jumpTriggerSq = (Float)(Balance.Cow.AttackJumpTriggerDistance * Balance.Cow.AttackJumpTriggerDistance);
        Float catchStandoff = (Float)Balance.Cow.CaughtStandoffDistance;
        Entity catchingCow = Entity.Null;
        Vector2 catchOffset = default;

        foreach (var cowRef in state.Filter<CowArchetype>())
        {
            var cowEntity = cowRef.Entity;

            if (!cowRef.Cow.IsAttacking)
            {
                if (cowRef.NavigationAgent2D.MaxSpeed != (Float)Balance.Cow.DefaultMaxSpeed)
                    cowRef.NavigationAgent2D.MaxSpeed = (Float)Balance.Cow.DefaultMaxSpeed;
                if (state.HasComponent<CowJumpComponent>(cowEntity))
                    state.RemoveComponent<CowJumpComponent>(cowEntity);
                continue;
            }

            var cowPos = cowRef.Transform2D.Position;
            var diff = cowPos - playerPos;

            if (state.HasComponent<CowJumpComponent>(cowEntity))
            {
                ref var jump = ref state.GetComponent<CowJumpComponent>(cowEntity);

                if (jump.WindupTicksLeft > 0)
                {
                    cowRef.StopMoving();
                    cowRef.NavigationAgent2D.MaxSpeed = (Float)0f;
                    jump.WindupTicksLeft--;
                    if (jump.WindupTicksLeft == 0)
                        state.AddComponent(cowEntity, new EnterStateComponent { Key = StateKeys.CowJumpLeap, Param = "", Age = 0 });
                    continue;
                }

                if (jump.LeapTicksLeft > 0)
                {
                    // Homing arc — re-aim at the player each tick so the leap never misses.
                    var leapDir = cowPos - playerPos;
                    var leapDirMag = leapDir.Magnitude;
                    Vector2 landing = leapDirMag > (Float)0.001f
                        ? playerPos + leapDir * (catchStandoff / leapDirMag)
                        : cowPos;
                    Float step = (Float)1f / (Float)jump.LeapTicksLeft;
                    cowRef.Transform2D.Position = cowPos + (landing - cowPos) * step;
                    jump.LeapTicksLeft--;
                    if (jump.LeapTicksLeft == 0)
                    {
                        cowRef.Transform2D.Position = landing;
                        catchingCow = cowEntity;
                        catchOffset = landing - playerPos;
                    }
                    continue;
                }

                // Stale component (shouldn't happen) — clear and resume chase.
                state.RemoveComponent<CowJumpComponent>(cowEntity);
            }

            if (diff.SqrMagnitude < catchDistSq)
            {
                if (catchingCow == Entity.Null)
                {
                    catchingCow = cowEntity;
                    var mag = diff.Magnitude;
                    catchOffset = mag > (Float)0.001f
                        ? diff * (catchStandoff / mag)
                        : new Vector2(catchStandoff, (Float)0f);
                    cowRef.Transform2D.Position = playerPos + catchOffset;
                }
                continue;
            }

            if (diff.SqrMagnitude < jumpTriggerSq)
            {
                state.AddComponent(cowEntity, new CowJumpComponent
                {
                    WindupTicksLeft = Balance.Cow.AttackJumpWindupTicks,
                    LeapTicksLeft = Balance.Cow.AttackJumpLeapTicks,
                    LeapDurationTicks = Balance.Cow.AttackJumpLeapTicks,
                });
                state.AddComponent(cowEntity, new EnterStateComponent { Key = StateKeys.CowJumpWindup, Param = "", Age = 0 });
                cowRef.StopMoving();
                continue;
            }

            // Stop avoiding the player while hunting — the whole point is to ram into them.
            cowRef.NavigationAgent2D.AvoidanceEnabled = false;
            cowRef.NavigationAgent2D.MaxSpeed = (Float)Balance.Cow.AttackChaseSpeed;
            cowRef.WalkTo(playerPos, arrivalDistance: (Float)0f);
        }

        if (catchingCow != Entity.Null)
        {
            int totalTicks = Balance.Player.CaughtTicks;
            state.AddComponent(player, new CaughtComponent
            {
                TicksRemaining = totalTicks,
                TotalTicks = totalTicks,
                CowEntity = catchingCow,
                CowOffsetX = catchOffset.X,
                CowOffsetY = catchOffset.Y,
            });
            state.AddComponent(player, new EnterStateComponent { Key = StateKeys.Caught, Param = "", Age = 0 });

            // All other cows go idle while the player is being held — no second cow piling on.
            foreach (var otherRef in state.Filter<CowArchetype>())
            {
                var otherEntity = otherRef.Entity;
                if (otherEntity == catchingCow) continue;
                ref var oc = ref otherRef.Cow;
                if (!oc.IsAttacking && oc.Horny == 0) continue;
                oc.IsAttacking = false;
                oc.Horny = 0;
                otherRef.NavigationAgent2D.MaxSpeed = (Float)Balance.Cow.DefaultMaxSpeed;
                otherRef.NavigationAgent2D.AvoidanceEnabled = true;
                otherRef.StopMoving();
                if (state.HasComponent<CowJumpComponent>(otherEntity))
                    state.RemoveComponent<CowJumpComponent>(otherEntity);
            }
        }
    }
}
