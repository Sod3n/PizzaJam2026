using System.Collections.Generic;
using Deterministic.GameFramework.ECS;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Actions;

namespace Template.Shared.Systems;

// Shared static helpers used by the Cow* family of systems.
// `ref T` is never held across CreateEntity() / growing AddComponent() — the buffer
// can be reallocated and any held ref points to dead memory.
public static class CowSystemHelpers
{
    public const int MaxFollowChainLength = 256;

    public static void SetHelpersEnabled(EntityWorld state, bool enabled)
    {
        foreach (var e in state.Filter<GlobalResourcesComponent>())
        {
            state.GetComponent<GlobalResourcesComponent>(e).HelpersEnabled = enabled ? 1 : 0;
            return;
        }
    }

    public static bool GetHelpersEnabled(EntityWorld state)
    {
        foreach (var e in state.Filter<GlobalResourcesComponent>())
            return state.GetComponent<GlobalResourcesComponent>(e).HelpersEnabled != 0;
        return true;
    }

    public static bool TryGetGlobalResourcesEntity(EntityWorld state, out Entity entity)
    {
        foreach (var ge in state.Filter<GlobalResourcesComponent>())
        {
            entity = ge;
            return true;
        }
        entity = Entity.Null;
        return false;
    }

    public static ref SkinSpawnCountsComponent GetSpawnCounts(EntityWorld state)
    {
        foreach (var e in state.Filter<SkinSpawnCountsComponent>())
            return ref state.GetComponent<SkinSpawnCountsComponent>(e);
        throw new InvalidOperationException("SkinSpawnCountsComponent entity not found");
    }

    // Append a cow to the player's follow chain. Sets FollowingPlayer; sets FollowTarget to player
    // when chain is empty, else to the current tail.
    public static void AddCowToFollowChain(EntityWorld state, Entity playerEntity, Entity cowToAdd)
    {
        state.GetComponent<CowComponent>(cowToAdd).FollowingPlayer = playerEntity;

        Entity head = state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow;

        if (head == Entity.Null)
        {
            state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow = cowToAdd;
            state.GetComponent<CowComponent>(cowToAdd).FollowTarget = playerEntity;
        }
        else
        {
            Entity lastCow = FindLastCowInChain(state, head);
            state.GetComponent<CowComponent>(cowToAdd).FollowTarget = lastCow;
        }
    }

    public static Entity FindLastCowInChain(EntityWorld state, Entity firstCow)
    {
        var visited = new HashSet<Entity> { firstCow };
        var current = firstCow;

        for (int hops = 0; hops < MaxFollowChainLength; hops++)
        {
            Entity next = Entity.Null;
            foreach (var cowEntity in state.Filter<CowComponent>())
            {
                var c = state.GetComponent<CowComponent>(cowEntity);
                if (c.FollowTarget == current && c.FollowingPlayer != Entity.Null)
                {
                    next = cowEntity;
                    break;
                }
            }
            if (next == Entity.Null) return current;

            if (!visited.Add(next))
                throw new InvalidOperationException(
                    $"[CowSystemHelpers] Cycle in cow follow chain at entity {next.Id} starting from {firstCow.Id}");

            current = next;
        }
        throw new InvalidOperationException(
            $"[CowSystemHelpers] Follow chain exceeded {MaxFollowChainLength} hops starting from {firstCow.Id}");
    }

    public static Entity FindNextCowInChain(EntityWorld state, Entity cow)
    {
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            var c = state.GetComponent<CowComponent>(cowEntity);
            if (c.FollowTarget == cow && c.FollowingPlayer != Entity.Null)
                return cowEntity;
        }
        return Entity.Null;
    }

    // Detach a cow from whatever house it is in. For regular houses also saves PreviousHouseId
    // (so the cow can return after breeding/love) and despawns the food sign when no helper holds the slot.
    public static void DetachCowFromHouse(EntityWorld state, Entity cowEntity, Entity ctxPlayer)
    {
        Entity houseId = state.GetComponent<CowComponent>(cowEntity).HouseId;
        if (houseId == Entity.Null) return;

        if (state.TryResolve<HouseArchetype>(houseId, out var houseRef))
        {
            if (houseRef.CowSlot == cowEntity) houseRef.ClearCowSlot();
            bool houseHasNoHelper = houseRef.House.HelperId == Entity.Null;
            // agent-helpers-in-house: sign disappears when cow leaves (helper presence not affected)
            if (houseHasNoHelper)
                InteractActionService.DespawnSignsForHouse(state.Ctx(ctxPlayer), houseId);
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.PreviousHouseId = houseId;
            cow.HouseId = Entity.Null;
        }
        else if (state.TryResolve<LoveHouseArchetype>(houseId, out var loveHouseRef))
        {
            loveHouseRef.ClearCowSlot(cowEntity);
            state.GetComponent<CowComponent>(cowEntity).HouseId = Entity.Null;
        }
    }

    // Return a cow to its previous house after breeding. Falls back to any empty house.
    public static void ReturnCowToHouse(EntityWorld state, Entity cowEntity)
    {
        if (!state.HasComponent<CowComponent>(cowEntity)) return;

        Entity prevHouse;
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            prevHouse = cow.PreviousHouseId;
            cow.ClearFollowChain();
            cow.PreviousHouseId = Entity.Null;
        }

        Entity targetHouse = Entity.Null;
        if (state.TryGetComponent<HouseComponent>(prevHouse, out var prevHouseComp)
            && prevHouseComp.CowId == Entity.Null)
        {
            targetHouse = prevHouse;
        }
        else
        {
            foreach (var e in state.Filter<HouseComponent>())
            {
                if (state.GetComponent<HouseComponent>(e).CowId == Entity.Null)
                { targetHouse = e; break; }
            }
        }

        if (targetHouse != Entity.Null)
        {
            state.GetComponent<HouseComponent>(targetHouse).CowId = cowEntity;
            state.GetComponent<CowComponent>(cowEntity).HouseId = targetHouse;
            // agent-helpers-in-house: restore food sign for cow.SelectedFood
            InteractActionService.EnsureFoodSignForHouse(state.Ctx(Entity.Null), targetHouse, cowEntity);
        }
        else
        {
            state.GetComponent<CowComponent>(cowEntity).HouseId = Entity.Null;
        }
    }

    // Clears the player's interaction target and transitions to Idle via EnterStateComponent
    // (picked up by AnimationsSystem next tick).
    public static void ClearInteractionAndIdle(EntityWorld state, Entity playerEntity)
    {
        state.GetComponent<PlayerStateComponent>(playerEntity).InteractionTarget = Entity.Null;
        state.GetComponent<StateComponent>(playerEntity).ResetState();
        state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Idle, Age = 0 });
    }
}
