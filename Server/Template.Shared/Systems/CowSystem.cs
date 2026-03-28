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
            else if (sc.Key == StateKeys.Breed)
            {
                state.HideEntity(playerEntity);
                // Hide both cows in the love house
                if (state.HasComponent<LoveHouseComponent>(playerState.InteractionTarget))
                {
                    var lh = state.GetComponent<LoveHouseComponent>(playerState.InteractionTarget);
                    if (lh.CowId1 != Entity.Null) state.HideEntity(lh.CowId1);
                    if (lh.CowId2 != Entity.Null) state.HideEntity(lh.CowId2);
                }
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
                // Don't unhide cows that are in a love house during breeding
                if (state.HasComponent<HiddenComponent>(cowEntity) && !IsCowInActiveBreeding(state, cowEntity))
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

            // Save previous house so cow can return after breeding
            var houseId = cow.HouseId;
            cow.PreviousHouseId = houseId;

            // Remove from house or love house if it was in one
            if (houseId != Entity.Null && state.HasComponent<HouseComponent>(houseId))
            {
                ref var house = ref state.GetComponent<HouseComponent>(houseId);
                house.CowId = Entity.Null;
            }
            else if (houseId != Entity.Null && state.HasComponent<LoveHouseComponent>(houseId))
            {
                ref var loveHouse = ref state.GetComponent<LoveHouseComponent>(houseId);
                if (loveHouse.CowId1 == cowEntity) loveHouse.CowId1 = Entity.Null;
                if (loveHouse.CowId2 == cowEntity) loveHouse.CowId2 = Entity.Null;
            }

            // Re-get cow ref after touching house components
            cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.HouseId = Entity.Null;

            // Add to end of follow chain
            if (playerState.FollowingCow == Entity.Null)
            {
                playerState.FollowingCow = cowEntity;
                // Re-get cow ref after touching PlayerStateComponent
                cow = ref state.GetComponent<CowComponent>(cowEntity);
                cow.FollowTarget = playerEntity;
            }
            else
            {
                // FindLastCowInChain iterates CowComponent filter, invalidating our ref
                Entity lastCow = FindLastCowInChain(state, playerState.FollowingCow);
                // Re-get cow ref after chain walk
                ref var cow2 = ref state.GetComponent<CowComponent>(cowEntity);
                cow2.FollowTarget = lastCow;
            }
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);

        ILogger.Log($"[CowSystem] Player {playerEntity.Id} now has cow {cowEntity.Id} in follow chain.");
    }

    private void HandleAssignComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Assign complete for player {playerEntity.Id}");

        var houseEntity = playerState.InteractionTarget;
        var cowEntity = playerState.FollowingCow; // Always assign the first cow in chain

        Entity oldCow = Entity.Null;

        if (state.HasComponent<HouseComponent>(houseEntity) && state.HasComponent<CowComponent>(cowEntity))
        {
            ref var house = ref state.GetComponent<HouseComponent>(houseEntity);

            if (house.CowId != Entity.Null && state.HasComponent<CowComponent>(house.CowId))
            {
                oldCow = house.CowId;
            }

            // Find the second cow in chain before modifying (invalidates refs)
            Entity nextCow = FindNextCowInChain(state, cowEntity);

            // Re-get refs after filter iteration
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            ref var house2 = ref state.GetComponent<HouseComponent>(houseEntity);

            // Remove first cow from chain and assign to house
            cow.FollowingPlayer = Entity.Null;
            cow.FollowTarget = Entity.Null;
            cow.HouseId = houseEntity;

            // Cow will walk to house via CowFollowSystem navigation

            // Re-get house ref and set cow
            house2 = ref state.GetComponent<HouseComponent>(houseEntity);
            house2.CowId = cowEntity;

            // Promote next cow in chain to be the new first
            if (nextCow != Entity.Null)
            {
                ref var nextCowComp = ref state.GetComponent<CowComponent>(nextCow);
                nextCowComp.FollowTarget = playerEntity;
                playerState.FollowingCow = nextCow;
            }
            else
            {
                playerState.FollowingCow = Entity.Null;
            }

            // If house had an old cow, add it to end of chain
            if (oldCow != Entity.Null)
            {
                if (playerState.FollowingCow == Entity.Null)
                {
                    ref var oldCowComp = ref state.GetComponent<CowComponent>(oldCow);
                    oldCowComp.FollowingPlayer = playerEntity;
                    oldCowComp.HouseId = Entity.Null;
                    playerState.FollowingCow = oldCow;
                    // Re-get after touching playerState
                    oldCowComp = ref state.GetComponent<CowComponent>(oldCow);
                    oldCowComp.FollowTarget = playerEntity;
                }
                else
                {
                    Entity lastCow = FindLastCowInChain(state, playerState.FollowingCow);
                    // Re-get after chain walk
                    ref var oldCowComp = ref state.GetComponent<CowComponent>(oldCow);
                    oldCowComp.FollowingPlayer = playerEntity;
                    oldCowComp.HouseId = Entity.Null;
                    oldCowComp.FollowTarget = lastCow;
                }
            }
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);

        ILogger.Log($"[CowSystem] Player {playerEntity.Id} assigned cow to house. Old cow following: {oldCow != Entity.Null}");
    }

    private void HandleBreedComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Breed complete for player {playerEntity.Id}");

        var target = playerState.InteractionTarget;

        // Love house breed: target is the love house entity
        if (state.HasComponent<LoveHouseComponent>(target))
        {
            HandleLoveHouseBreedComplete(state, playerEntity, target, ref sc, ref playerState);
            return;
        }

        // Regular crossbreed: target is the other cow
        var targetCow = target;
        var followingCow = playerState.FollowingCow;

        if (state.HasComponent<CowComponent>(targetCow) && state.HasComponent<CowComponent>(followingCow)
            && state.HasComponent<SkinComponent>(targetCow) && state.HasComponent<SkinComponent>(followingCow))
        {
            SpawnCrossbredCow(state, playerEntity, followingCow, targetCow);
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);
    }

    private void HandleLoveHouseBreedComplete(EntityWorld state, Entity playerEntity, Entity loveHouseEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Love house breed complete for player {playerEntity.Id}");

        state.UnhideEntity(playerEntity);

        ref var loveHouse = ref state.GetComponent<LoveHouseComponent>(loveHouseEntity);
        var cow1 = loveHouse.CowId1;
        var cow2 = loveHouse.CowId2;

        // Unhide cows
        if (cow1 != Entity.Null) state.UnhideEntity(cow1);
        if (cow2 != Entity.Null) state.UnhideEntity(cow2);

        Entity babyCow = Entity.Null;
        if (state.HasComponent<CowComponent>(cow1) && state.HasComponent<CowComponent>(cow2)
            && state.HasComponent<SkinComponent>(cow1) && state.HasComponent<SkinComponent>(cow2))
        {
            babyCow = SpawnCrossbredCow(state, playerEntity, cow1, cow2);
        }

        // Clear love house slots
        loveHouse = ref state.GetComponent<LoveHouseComponent>(loveHouseEntity);
        loveHouse.CowId1 = Entity.Null;
        loveHouse.CowId2 = Entity.Null;

        // Return parent cows to their previous houses
        ReturnCowToHouse(state, cow1);
        ReturnCowToHouse(state, cow2);

        // Baby cow follows the player
        if (babyCow != Entity.Null && state.HasComponent<CowComponent>(babyCow))
        {
            ref var baby = ref state.GetComponent<CowComponent>(babyCow);
            baby.FollowingPlayer = playerEntity;

            if (playerState.FollowingCow == Entity.Null)
            {
                playerState.FollowingCow = babyCow;
                baby = ref state.GetComponent<CowComponent>(babyCow);
                baby.FollowTarget = playerEntity;
            }
            else
            {
                Entity lastCow = FindLastCowInChain(state, playerState.FollowingCow);
                baby = ref state.GetComponent<CowComponent>(babyCow);
                baby.FollowTarget = lastCow;
            }
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);

        ILogger.Log($"[CowSystem] Love house breed complete. Released cows {cow1.Id} and {cow2.Id} back to player {playerEntity.Id}");
    }

    private Entity SpawnCrossbredCow(EntityWorld state, Entity playerEntity, Entity parentA, Entity parentB)
    {
        var skinA = state.GetComponent<SkinComponent>(parentA);
        var skinB = state.GetComponent<SkinComponent>(parentB);

        var spawnPos = state.HasComponent<Deterministic.GameFramework.TwoD.Transform2D>(parentB)
            ? state.GetComponent<Deterministic.GameFramework.TwoD.Transform2D>(parentB).Position + new Vector2(2, 0)
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

        // Inherit food preference from parents (50/50 chance, with 10% mutation)
        var parentACow = state.GetComponent<CowComponent>(parentA);
        var parentBCow = state.GetComponent<CowComponent>(parentB);
        int prefRoll = random.NextInt(100);
        if (prefRoll < 10)
            newCowComp.PreferredFood = random.NextInt(0, 4); // mutation
        else if (prefRoll < 55)
            newCowComp.PreferredFood = parentACow.PreferredFood;
        else
            newCowComp.PreferredFood = parentBCow.PreferredFood;

        ILogger.Log($"[CowSystem] Bred new cow {newCow.Id} with MaxExhaust: {totalExhaust}, PreferredFood: {newCowComp.PreferredFood}");
        return newCow;
    }

    /// <summary>Find the last cow in the follow chain starting from firstCow.</summary>
    private Entity FindLastCowInChain(EntityWorld state, Entity firstCow)
    {
        var current = firstCow;
        int safety = 0;
        while (safety < 100)
        {
            // Find if any cow has FollowTarget == current
            Entity next = Entity.Null;
            foreach (var cowEntity in state.Filter<CowComponent>())
            {
                ref var c = ref state.GetComponent<CowComponent>(cowEntity);
                if (c.FollowTarget == current && c.FollowingPlayer != Entity.Null)
                {
                    next = cowEntity;
                    break;
                }
            }
            if (next == Entity.Null) return current;
            current = next;
            safety++;
        }
        return current;
    }

    /// <summary>Find the cow that follows the given cow (next in chain).</summary>
    private Entity FindNextCowInChain(EntityWorld state, Entity cow)
    {
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var c = ref state.GetComponent<CowComponent>(cowEntity);
            if (c.FollowTarget == cow && c.FollowingPlayer != Entity.Null)
            {
                return cowEntity;
            }
        }
        return Entity.Null;
    }

    /// <summary>Check if a cow is in a love house that's currently being bred.</summary>
    private bool IsCowInActiveBreeding(EntityWorld state, Entity cowEntity)
    {
        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
        var houseId = cow.HouseId;
        if (houseId == Entity.Null || !state.HasComponent<LoveHouseComponent>(houseId)) return false;

        // Check if any player is in breed state targeting this love house
        foreach (var playerEntity in state.Filter<PlayerStateComponent>())
        {
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;
            var sc = state.GetComponent<StateComponent>(playerEntity);
            if (!sc.IsEnabled || sc.Key != StateKeys.Breed) continue;
            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
            if (ps.InteractionTarget == houseId) return true;
        }
        return false;
    }

    /// <summary>Return a cow to its previous house after breeding. If no previous house, cow becomes free.</summary>
    private void ReturnCowToHouse(EntityWorld state, Entity cowEntity)
    {
        if (!state.HasComponent<CowComponent>(cowEntity)) return;

        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
        var prevHouse = cow.PreviousHouseId;
        cow.FollowingPlayer = Entity.Null;
        cow.FollowTarget = Entity.Null;
        cow.PreviousHouseId = Entity.Null;

        if (prevHouse != Entity.Null && state.HasComponent<HouseComponent>(prevHouse))
        {
            ref var house = ref state.GetComponent<HouseComponent>(prevHouse);
            house.CowId = cowEntity;
            cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.HouseId = prevHouse;

            // Cow will walk back to house via CowFollowSystem navigation
        }
        else
        {
            // No previous house — cow becomes free at its current position
            cow.HouseId = Entity.Null;
        }
    }
}
