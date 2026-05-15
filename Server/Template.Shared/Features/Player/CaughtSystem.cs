using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Runs after CowAttackSystem. While a player has CaughtComponent the cow is pinned on top
// and movement is blocked (see SetMoveDirectionActionService). When the timer expires we
// teleport the player to their house, queue the existing SleepingComponent flow (advances
// day + reuses the sleep fade overlay), and reset the offending cow to its home spot.
public class CaughtSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        System.Collections.Generic.List<Entity> done = null;
        int fadeThreshold = Balance.Player.CaughtFadeTicks;

        Entity houseEntity = Entity.Null;
        Vector2 housePos = default;
        foreach (var e in state.Filter<PlayerHouseComponent>())
        {
            houseEntity = e;
            if (state.TryGetComponent<Transform2D>(e, out var ht)) housePos = ht.Position;
            break;
        }

        foreach (var playerEntity in state.Filter<CaughtComponent>())
        {
            ref var caught = ref state.GetComponent<CaughtComponent>(playerEntity);

            if (state.HasComponent<Transform2D>(playerEntity) && caught.CowEntity != Entity.Null
                && state.HasComponent<Transform2D>(caught.CowEntity))
            {
                var pPos = state.GetComponent<Transform2D>(playerEntity).Position;
                state.GetComponent<Transform2D>(caught.CowEntity).Position =
                    pPos + new Vector2(caught.CowOffsetX, caught.CowOffsetY);
                if (state.HasComponent<CharacterBody2D>(caught.CowEntity))
                    state.GetComponent<CharacterBody2D>(caught.CowEntity).Velocity = Vector2.Zero;
                if (state.HasComponent<CharacterBody2D>(playerEntity))
                    state.GetComponent<CharacterBody2D>(playerEntity).Velocity = Vector2.Zero;
            }

            // Periodic "boop" — emit Interacted on the player so the existing squish animation
            // fires; client view spawns hearts at the midpoint on this same event.
            int elapsed = caught.TotalTicks - caught.TicksRemaining;
            int tapInterval = System.Math.Max(1, Balance.Player.CaughtTapIntervalTicks);
            if (caught.TicksRemaining > fadeThreshold && elapsed > 0 && elapsed % tapInterval == 0)
                state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "caught_tap", Age = 0 });

            // Kick off the fade BEFORE teleport so the screen is already black when the
            // camera snaps to the house. SleepFadeOverlay watches SleepingComponent add.
            if (caught.TicksRemaining <= fadeThreshold
                && houseEntity != Entity.Null
                && !state.HasComponent<SleepingComponent>(playerEntity))
            {
                int totalTicks = Balance.PlayerHouse.SleepStateTicks;
                state.AddComponent(playerEntity, new SleepingComponent
                {
                    TicksRemaining = totalTicks,
                    TotalTicks = totalTicks,
                    House = houseEntity,
                    DayAdvanced = 0,
                });
            }

            if (caught.TicksRemaining <= 0)
                (done ??= new System.Collections.Generic.List<Entity>()).Add(playerEntity);
            else
                caught.TicksRemaining--;
        }

        if (done == null) return;

        foreach (var playerEntity in done)
        {
            var caught = state.GetComponent<CaughtComponent>(playerEntity);

            if (houseEntity != Entity.Null)
            {
                if (state.HasComponent<Transform2D>(playerEntity))
                    state.GetComponent<Transform2D>(playerEntity).Position = housePos;
                state.HideEntity(playerEntity);
                if (state.HasComponent<PlayerStateComponent>(playerEntity))
                    state.GetComponent<PlayerStateComponent>(playerEntity).InteractionTarget = houseEntity;
            }

            if (caught.CowEntity != Entity.Null && state.HasComponent<CowComponent>(caught.CowEntity))
            {
                ref var cow = ref state.GetComponent<CowComponent>(caught.CowEntity);
                cow.IsAttacking = false;
                cow.Horny = 0;
                cow.Exhaust = 0;
                cow.IsExhausted = false;
                cow.MilkClickCounter = 0;

                Vector2 resetPos = cow.SpawnPosition;
                if (cow.HouseId != Entity.Null && state.TryGetComponent<Transform2D>(cow.HouseId, out var htf))
                    resetPos = htf.Position;
                if (state.HasComponent<Transform2D>(caught.CowEntity))
                    state.GetComponent<Transform2D>(caught.CowEntity).Position = resetPos;
                if (state.HasComponent<CharacterBody2D>(caught.CowEntity))
                    state.GetComponent<CharacterBody2D>(caught.CowEntity).Velocity = Vector2.Zero;
                if (state.HasComponent<NavigationAgent2D>(caught.CowEntity))
                {
                    ref var nav = ref state.GetComponent<NavigationAgent2D>(caught.CowEntity);
                    nav.MaxSpeed = (Float)Balance.Cow.DefaultMaxSpeed;
                    nav.AvoidanceEnabled = true;
                    nav.IsNavigationFinished = true;
                }
                if (state.HasComponent<CowJumpComponent>(caught.CowEntity))
                    state.RemoveComponent<CowJumpComponent>(caught.CowEntity);
            }

            state.RemoveComponent<CaughtComponent>(playerEntity);
        }
    }
}
