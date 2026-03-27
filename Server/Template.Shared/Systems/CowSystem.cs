using Deterministic.GameFramework.ECS;
using Template.Shared.Components;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Systems;

public class CowSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        // Handle state completions on players
        foreach (var playerEntity in state.Filter<ExitStateComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            var exit = state.GetComponent<ExitStateComponent>(playerEntity);
            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            ref var playerState = ref state.GetComponent<PlayerStateComponent>(playerEntity);

            if (exit.Key == StateKeys.Milking)
                HandleMilkingComplete(state, playerEntity, ref sc, ref playerState);
            else if (exit.Key == StateKeys.Taming)
                HandleTamingComplete(state, playerEntity, ref sc, ref playerState);
            else if (exit.Key == StateKeys.Release)
                HandleReleaseComplete(state, playerEntity, ref sc, ref playerState);
            else if (exit.Key == StateKeys.Assign)
                HandleAssignComplete(state, playerEntity, ref sc, ref playerState);
            else if (exit.Key == StateKeys.Breed)
                HandleBreedComplete(state, playerEntity, ref sc, ref playerState);
        }

        // Handle active milking states (ensure hidden during Active and Exit phases)
        foreach (var playerEntity in state.Filter<PlayerStateComponent>())
        {
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;

            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            if (!sc.IsEnabled) continue;

            ref var playerState = ref state.GetComponent<PlayerStateComponent>(playerEntity);

            if (sc.Key == StateKeys.Milking && (sc.Phase == StatePhase.Active || sc.Phase == StatePhase.Exit))
            {
                state.HideEntity(playerEntity);
                if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
                    state.HideEntity(playerState.InteractionTarget);
            }
        }

        // Handle Cow Exhaust & Visibility
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);

            if (cow is { Exhaust: > 0, IsMilking: false })
            {
                var gameTime = state.GetCustomData<IGameTime>();
                if (gameTime != null)
                {
                    var random = new DeterministicRandom((uint)(cowEntity.Id ^ gameTime.CurrentTick));
                    if (random.NextInt(0, 240) == 0)
                    {
                        cow.Exhaust--;
                    }
                }
            }
            else if (!cow.IsMilking)
            {
                if (state.HasComponent<HiddenComponent>(cowEntity))
                {
                    state.UnhideEntity(cowEntity);
                }
            }
        }
    }

    private void ResetState(EntityWorld state, Entity entity, ref StateComponent sc)
    {
        sc.Key = "";
        sc.CurrentTime = 0;
        sc.MaxTime = 0;
        state.AddComponent(entity, new EnterStateComponent { Key = StateKeys.Idle, Age = 0 });
    }

    private void HandleMilkingComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Milking complete for player {playerEntity.Id}");

        state.UnhideEntity(playerEntity);

        if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
        {
            ref var cow = ref state.GetComponent<CowComponent>(playerState.InteractionTarget);
            cow.IsMilking = false;
            state.UnhideEntity(playerState.InteractionTarget);
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);
    }

    private void HandleTamingComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Taming complete for player {playerEntity.Id}");

        var cowEntity = playerState.InteractionTarget;

        if (state.HasComponent<CowComponent>(cowEntity))
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.FollowingPlayer = playerEntity;
            playerState.FollowingCow = cowEntity;

            if (cow.HouseId != Entity.Null && state.HasComponent<HouseComponent>(cow.HouseId))
            {
                ref var house = ref state.GetComponent<HouseComponent>(cow.HouseId);
                house.CowId = Entity.Null;
            }
            cow.HouseId = Entity.Null;
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);

        ILogger.Log($"[CowSystem] Player {playerEntity.Id} now has cow {playerState.FollowingCow.Id} following.");
    }

    private void HandleReleaseComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Release complete for player {playerEntity.Id}");

        var cowEntity = playerState.FollowingCow;

        if (state.HasComponent<CowComponent>(cowEntity))
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.FollowingPlayer = Entity.Null;

            if (state.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowEntity))
            {
                ref var body = ref state.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowEntity);
                body.Velocity = Vector2.Zero;
            }
        }

        playerState.FollowingCow = Entity.Null;
        ResetState(state, playerEntity, ref sc);
    }

    private void HandleAssignComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Assign complete for player {playerEntity.Id}");

        var houseEntity = playerState.InteractionTarget;
        var cowEntity = playerState.FollowingCow;

        Entity oldCow = Entity.Null;

        if (state.HasComponent<HouseComponent>(houseEntity) && state.HasComponent<CowComponent>(cowEntity))
        {
            ref var house = ref state.GetComponent<HouseComponent>(houseEntity);

            if (house.CowId != Entity.Null && state.HasComponent<CowComponent>(house.CowId))
            {
                oldCow = house.CowId;
            }

            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.FollowingPlayer = Entity.Null;
            cow.HouseId = houseEntity;

            if (state.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowEntity))
            {
                ref var body = ref state.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowEntity);
                body.Velocity = Vector2.Zero;
            }

            if (state.HasComponent<Deterministic.GameFramework.TwoD.Transform2D>(houseEntity) &&
                state.HasComponent<Deterministic.GameFramework.TwoD.Transform2D>(cowEntity))
            {
                var housePos = state.GetComponent<Deterministic.GameFramework.TwoD.Transform2D>(houseEntity).Position;
                ref var cowTransform = ref state.GetComponent<Deterministic.GameFramework.TwoD.Transform2D>(cowEntity);
                cowTransform.Position = housePos + new Vector2(2, 2);
                cow.SpawnPosition = cowTransform.Position;
            }

            house.CowId = cowEntity;

            if (oldCow != Entity.Null)
            {
                ref var oldCowComp = ref state.GetComponent<CowComponent>(oldCow);
                oldCowComp.FollowingPlayer = playerEntity;
                oldCowComp.HouseId = Entity.Null;
                playerState.FollowingCow = oldCow;
            }
            else
            {
                playerState.FollowingCow = Entity.Null;
            }
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);

        ILogger.Log($"[CowSystem] Player {playerEntity.Id} assigned cow to house. Old cow following: {oldCow != Entity.Null}");
    }

    private void HandleBreedComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Breed complete for player {playerEntity.Id}");

        var targetCow = playerState.InteractionTarget;
        var followingCow = playerState.FollowingCow;

        if (state.HasComponent<CowComponent>(targetCow) && state.HasComponent<CowComponent>(followingCow)
            && state.HasComponent<SkinComponent>(targetCow) && state.HasComponent<SkinComponent>(followingCow))
        {
            var skinA = state.GetComponent<SkinComponent>(followingCow);
            var skinB = state.GetComponent<SkinComponent>(targetCow);

            var spawnPos = state.HasComponent<Deterministic.GameFramework.TwoD.Transform2D>(targetCow)
                ? state.GetComponent<Deterministic.GameFramework.TwoD.Transform2D>(targetCow).Position + new Vector2(2, 0)
                : new Vector2(0, 0);

            var context = new Context(state, playerEntity, null!);
            var newCow = Definitions.CowDefinition.Create(context, spawnPos);

            var gameTime = state.GetCustomData<IGameTime>();
            uint seed = (uint)(newCow.Id ^ (gameTime?.CurrentTick ?? 0));
            var random = new DeterministicRandom(seed);

            var crossbredSkin = GameData.GD.SkinsData.CrossbreedSkin(ref random, skinA, skinB);
            state.AddComponent(newCow, crossbredSkin);

            int totalExhaust = 0;
            foreach (var skinId in crossbredSkin.Skins.Values)
            {
                var skinDef = GameData.GD.SkinsData.Get(skinId);
                if (skinDef != null)
                    totalExhaust += skinDef.Exhaust;
            }
            if (totalExhaust <= 0) totalExhaust = 10;

            ref var newCowComp = ref state.GetComponent<CowComponent>(newCow);
            newCowComp.MaxExhaust = totalExhaust;

            ILogger.Log($"[CowSystem] Bred new cow {newCow.Id} with MaxExhaust: {totalExhaust}");
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);
    }
}
