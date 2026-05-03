using System.Collections.Generic;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Template.Shared.Components;
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
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(e);
            gr.HelpersEnabled = enabled ? 1 : 0;
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
        throw new System.InvalidOperationException("SkinSpawnCountsComponent entity not found");
    }

    // Append a cow to the player's follow chain. Sets FollowingPlayer; sets FollowTarget to player
    // when chain is empty, else to the current tail.
    public static void AddCowToFollowChain(EntityWorld state, Entity playerEntity, Entity cowToAdd)
    {
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowToAdd);
            cow.FollowingPlayer = playerEntity;
        }

        Entity head = state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow;

        if (head == Entity.Null)
        {
            {
                ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
                ps.FollowingCow = cowToAdd;
            }
            {
                ref var cow = ref state.GetComponent<CowComponent>(cowToAdd);
                cow.FollowTarget = playerEntity;
            }
        }
        else
        {
            Entity lastCow = FindLastCowInChain(state, head);
            ref var cow = ref state.GetComponent<CowComponent>(cowToAdd);
            cow.FollowTarget = lastCow;
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
                throw new System.InvalidOperationException(
                    $"[CowSystemHelpers] Cycle in cow follow chain at entity {next.Id} starting from {firstCow.Id}");

            current = next;
        }
        throw new System.InvalidOperationException(
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

        if (state.HasComponent<HouseComponent>(houseId))
        {
            bool houseHasNoHelper;
            {
                ref var house = ref state.GetComponent<HouseComponent>(houseId);
                if (house.CowId == cowEntity) house.CowId = Entity.Null;
                houseHasNoHelper = house.HelperId == Entity.Null;
            }
            // agent-helpers-in-house: sign disappears when cow leaves (helper presence not affected)
            if (houseHasNoHelper)
            {
                var ctx = new Context(state, ctxPlayer, null!);
                InteractActionService.DespawnSignsForHouse(ctx, houseId);
            }
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.PreviousHouseId = houseId;
            cow.HouseId = Entity.Null;
        }
        else if (state.HasComponent<LoveHouseComponent>(houseId))
        {
            ref var loveHouse = ref state.GetComponent<LoveHouseComponent>(houseId);
            if (loveHouse.CowId1 == cowEntity) loveHouse.CowId1 = Entity.Null;
            if (loveHouse.CowId2 == cowEntity) loveHouse.CowId2 = Entity.Null;
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.HouseId = Entity.Null;
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
            cow.FollowingPlayer = Entity.Null;
            cow.FollowTarget = Entity.Null;
            cow.PreviousHouseId = Entity.Null;
        }

        Entity targetHouse = Entity.Null;
        if (prevHouse != Entity.Null && state.HasComponent<HouseComponent>(prevHouse)
            && state.GetComponent<HouseComponent>(prevHouse).CowId == Entity.Null)
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
            {
                ref var house = ref state.GetComponent<HouseComponent>(targetHouse);
                house.CowId = cowEntity;
            }
            {
                ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
                cow.HouseId = targetHouse;
            }
            // agent-helpers-in-house: restore food sign for cow.SelectedFood
            var ctxRet = new Context(state, Entity.Null, null!);
            InteractActionService.EnsureFoodSignForHouse(ctxRet, targetHouse, cowEntity);
        }
        else
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.HouseId = Entity.Null;
        }
    }

    // Clears the player's interaction target and transitions to Idle via EnterStateComponent
    // (picked up by AnimationsSystem next tick).
    public static void ClearInteractionAndIdle(EntityWorld state, Entity playerEntity)
    {
        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.InteractionTarget = Entity.Null;
        }
        {
            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            sc.Key = "";
            sc.CurrentTime = 0;
            sc.MaxTime = 0;
            sc.IsEnabled = false;
        }
        state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Idle, Age = 0 });
    }
}
