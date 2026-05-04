using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared.Systems;

public class HelperSystem : ISystem
{
    internal static readonly Float TargetReachedDistSq = (Float)Template.Shared.GameData.Balance.Helper.TargetReachedDistSq;
    internal static readonly Float PlayerReturnDistSq = (Float)Template.Shared.GameData.Balance.Helper.PlayerReturnDistSq;
    internal static readonly Float GatherReachedDistSq = (Float)Template.Shared.GameData.Balance.Helper.GatherReachedDistSq;
    internal const int GatherWorkDuration = Template.Shared.GameData.Balance.Helper.GatherWorkDuration;
    internal const int SellWorkDuration = Template.Shared.GameData.Balance.Helper.SellWorkDuration;
    internal const int BuildWorkDuration = Template.Shared.GameData.Balance.Helper.BuildWorkDuration;
    internal const int MilkWorkDuration = Template.Shared.GameData.Balance.Helper.MilkWorkDuration;

    /// <summary>
    /// Helpers do 1 unit of work per cycle — pets shrink the cycle instead of
    /// bulk-multiplying per cycle. Mirrors the player-side cadence model.
    /// </summary>
    internal static int ApplyPetSpeedBoost(int baseDuration, int petCount)
    {
        int boost = Template.Shared.GameData.Balance.Pets.AdditiveBoostBase
                  + Template.Shared.GameData.Balance.Pets.BoostPerPet * petCount;
        if (boost < 1) boost = 1;
        int duration = baseDuration / boost;
        return duration < 1 ? 1 : duration;
    }

    public void Update(EntityWorld state)
    {
        RecomputePetCounts(state);

        foreach (var helperRef in state.Filter<HelperArchetype>())
        {
            helperRef.Helper.SuppressTickUpdate = false;

            Entity carrier = FindHelperCarrier(state, helperRef.Entity);
            if (carrier != Entity.Null)
            {
                helperRef.Helper.OwnerPlayer = carrier;
                SwarmFollow.Follow(state, helperRef.Entity, carrier);
                helperRef.Helper.SuppressTickUpdate = true;
                continue;
            }

            UpdateOwnerPlayer(state, helperRef);

            if (state.HasComponent<HiddenComponent>(helperRef.Helper.OwnerPlayer) && helperRef.Helper.Type != HelperType.Gatherer)
            {
                ref var body = ref helperRef.CharacterBody2D;
                body.Velocity *= (Float)0.8f;
                if (body.Velocity.SqrMagnitude < (Float)0.05f)
                    body.Velocity = Vector2.Zero;
                helperRef.Helper.SuppressTickUpdate = true;
                continue;
            }

            int petCount = helperRef.Helper.PetCount;
            int boostMul = Template.Shared.GameData.Balance.Pets.AdditiveBoostBase + Template.Shared.GameData.Balance.Pets.BoostPerPet * petCount;
            var config = HelperConfig.GetByType(helperRef.Helper.Type);
            helperRef.Helper.BagCapacity = config.BaseCapacity * boostMul;
            helperRef.NavigationAgent2D.MaxSpeed = (Float)config.BaseSpeed * (Float)boostMul;

            if (helperRef.Helper.Type == HelperType.Assistant)
                UpdateAssistant(state, helperRef);
        }

        foreach (var petEntity in state.Filter<HelperPetComponent>())
        {
            if (!state.HasComponent<Transform2D>(petEntity)) continue;
            ref var pet = ref state.GetComponent<HelperPetComponent>(petEntity);

            if (pet.AssignedTo != Entity.Null && !state.HasComponent<Transform2D>(pet.AssignedTo))
            {
                pet.State = PetState.Idle;
                pet.FollowTarget = Entity.Null;
                pet.AssignedTo = Entity.Null;
                ref var t = ref state.GetComponent<Transform2D>(petEntity);
                t.Position = new Vector2((Float)pet.IdleSpawnX, (Float)pet.IdleSpawnY);
                continue;
            }

            if (pet.FollowTarget == Entity.Null || !state.HasComponent<Transform2D>(pet.FollowTarget)) continue;
            SwarmFollow.Follow(state, petEntity, pet.FollowTarget);
        }
    }

    private static void UpdateOwnerPlayer(EntityWorld state, HelperRef helperRef)
    {
        var closestPlayer = FindClosestPlayer(state, helperRef.Entity);
        if (closestPlayer != Entity.Null && closestPlayer != helperRef.Helper.OwnerPlayer)
        {
            Float switchThresholdSq = (Float)Template.Shared.GameData.Balance.Helper.OwnerSwitchThresholdSq;
            var myPos = helperRef.Transform2D.Position;
            var newDist = Vector2.DistanceSquared(myPos, state.GetComponent<Transform2D>(closestPlayer).Position);
            var oldDist = state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer)
                ? Vector2.DistanceSquared(myPos, state.GetComponent<Transform2D>(helperRef.Helper.OwnerPlayer).Position)
                : (Float)999999f;
            if ((oldDist - newDist) > switchThresholdSq)
                helperRef.Helper.OwnerPlayer = closestPlayer;
        }
        else if (helperRef.Helper.OwnerPlayer == Entity.Null)
        {
            helperRef.Helper.OwnerPlayer = closestPlayer;
        }
    }

    private static void RecomputePetCounts(EntityWorld state)
    {
        foreach (var he in state.Filter<HelperComponent>())
        {
            ref var h = ref state.GetComponent<HelperComponent>(he);
            h.PetCount = 0;
        }
        foreach (var ce in state.Filter<CowComponent>())
        {
            ref var c = ref state.GetComponent<CowComponent>(ce);
            c.PetCount = 0;
        }
        foreach (var pe in state.Filter<PlayerEntity>())
        {
            if (!state.HasComponent<PlayerStateComponent>(pe)) continue;
            ref var ps = ref state.GetComponent<PlayerStateComponent>(pe);
            ps.PetCount = 0;
        }

        foreach (var pe in state.Filter<HelperPetComponent>())
        {
            var pet = state.GetComponent<HelperPetComponent>(pe);
            if (pet.State != PetState.Assigned) continue;
            var target = pet.AssignedTo;
            if (target == Entity.Null) continue;

            if (state.HasComponent<HelperComponent>(target))
            {
                ref var h = ref state.GetComponent<HelperComponent>(target);
                h.PetCount++;
            }
            else if (state.HasComponent<CowComponent>(target))
            {
                ref var c = ref state.GetComponent<CowComponent>(target);
                c.PetCount++;
            }
            else if (state.HasComponent<PlayerEntity>(target) && state.HasComponent<PlayerStateComponent>(target))
            {
                ref var ps = ref state.GetComponent<PlayerStateComponent>(target);
                ps.PetCount++;
            }
        }
    }

    // ─── Assistant: follow player closely ───

    private void UpdateAssistant(EntityWorld state, HelperRef helperRef)
    {
        if (!state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer)) return;
        SwarmFollow.Follow(state, helperRef.Entity, helperRef.Helper.OwnerPlayer);
    }

    // ─── Navigation helper ───

    private static Entity FindHelperCarrier(EntityWorld state, Entity helperEntity)
    {
        foreach (var pe in state.Filter<PlayerStateComponent>())
        {
            if (state.GetComponent<PlayerStateComponent>(pe).FollowingHelper == helperEntity)
                return pe;
        }
        return Entity.Null;
    }

    // ─── Player finding ───

    private static Entity FindClosestPlayer(EntityWorld state, Entity helper)
    {
        if (!state.HasComponent<Transform2D>(helper)) return Entity.Null;
        var myPos = state.GetComponent<Transform2D>(helper).Position;
        Entity nearest = Entity.Null;
        Float minDistSq = 999999f;

        foreach (var player in state.Filter<PlayerEntity>())
        {
            if (!state.HasComponent<Transform2D>(player)) continue;
            var pos = state.GetComponent<Transform2D>(player).Position;
            var distSq = Vector2.DistanceSquared(myPos, pos);
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearest = player;
            }
        }
        return nearest;
    }

}
