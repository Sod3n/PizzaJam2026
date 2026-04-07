using System;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Navigation2D.Components;
using Template.Shared.Components;

namespace Template.Shared.Systems;

/// <summary>
/// Swarm following using nav agent pathfinding + DtCrowd avoidance + velocity lerp.
/// DtCrowd handles inter-agent avoidance (no more manual ORCA).
/// </summary>
public static class SwarmFollow
{
    private const float StopDist = 3f;
    private const float StartDist = 5f;
    private const float IdleRangeDist = 10f;
    private const float PlayerMovingThresholdSq = 0.5f;
    private const float LerpFactor = 0.2f;
    private const float SeparationDist = 1.8f;

    public static void Follow(EntityWorld state, Entity entity, Entity targetPlayer)
    {
        if (!state.HasComponent<Transform2D>(entity)) return;
        if (!state.HasComponent<CharacterBody2D>(entity)) return;
        if (!state.HasComponent<NavigationAgent2D>(entity)) return;
        if (!state.HasComponent<Transform2D>(targetPlayer)) return;

        var playerPos = state.GetComponent<Transform2D>(targetPlayer).Position;
        var myPos = state.GetComponent<Transform2D>(entity).Position;
        var toPlayer = playerPos - myPos;
        var dist = (float)Float.Sqrt(toPlayer.SqrMagnitude);

        ref var body = ref state.GetComponent<CharacterBody2D>(entity);
        ref var navAgent = ref state.GetComponent<NavigationAgent2D>(entity);

        bool wasMoving = (float)body.Velocity.SqrMagnitude > 0.5f;

        bool playerMoving = false;
        if (state.HasComponent<CharacterBody2D>(targetPlayer))
            playerMoving = (float)state.GetComponent<CharacterBody2D>(targetPlayer).Velocity.SqrMagnitude > PlayerMovingThresholdSq;

        bool alreadyNearby = dist < IdleRangeDist && !wasMoving;
        bool inIdleRange = !playerMoving && alreadyNearby;
        float threshold = wasMoving ? StopDist : StartDist;

        // ─── IDLE: within threshold, or player stationary and in range ───
        if (dist < threshold || inIdleRange)
        {
            var sep = GetSeparation(state, entity, myPos, SeparationDist);

            if ((float)sep.SqrMagnitude > 0.01f)
            {
                body.Velocity = sep * (Float)3f;
            }
            else
            {
                body.Velocity = body.Velocity * (Float)0.8f;
                if ((float)body.Velocity.SqrMagnitude < 0.05f)
                    body.Velocity = Vector2.Zero;
            }

            navAgent.IsNavigationFinished = true;
            return;
        }

        // ─── MOVING: use nav agent (DtCrowd handles avoidance) ───

        var drift = (playerPos - navAgent.TargetPosition).SqrMagnitude;
        if ((float)drift > 2f || navAgent.IsNavigationFinished)
        {
            navAgent.TargetPosition = playerPos;
            navAgent.IsNavigationFinished = false;
        }

        // DtCrowd computes velocity with inter-agent avoidance built in
        var desiredVel = navAgent.Velocity;

        // Velocity lerp for smooth movement
        body.Velocity = body.Velocity + (desiredVel - body.Velocity) * (Float)LerpFactor;

        // Face direction
        if ((float)body.Velocity.SqrMagnitude > 0.1f)
        {
            ref var transform = ref state.GetComponent<Transform2D>(entity);
            transform.Rotation = body.Velocity.ToAngle();
        }
    }

    // ─── Separation: gentle push from overlapping neighbors ───

    private static Vector2 GetSeparation(EntityWorld state, Entity self, Vector2 myPos, float radius)
    {
        var sep = Vector2.Zero;
        float radiusSq = radius * radius;

        void Check(Entity other)
        {
            if (other == self) return;
            if (!state.HasComponent<Transform2D>(other)) return;
            var diff = myPos - state.GetComponent<Transform2D>(other).Position;
            var dSq = (float)diff.SqrMagnitude;
            if (dSq >= radiusSq) return;
            if (dSq < 0.01f)
            {
                int d = self.Id - other.Id;
                float a = d * 1.2566f;
                sep = sep + new Vector2((Float)Math.Cos(a), (Float)Math.Sin(a)) * (Float)0.5f;
            }
            else
            {
                float strength = 1f - (float)Math.Sqrt(dSq) / radius;
                sep = sep + diff.Normalized * (Float)(strength * 0.5f);
            }
        }

        foreach (var e in state.Filter<HelperComponent>()) Check(e);
        foreach (var e in state.Filter<CowComponent>())
        {
            if (!state.HasComponent<CowComponent>(e)) continue;
            var c = state.GetComponent<CowComponent>(e);
            if (c.FollowingPlayer != Entity.Null) Check(e);
        }

        return sep;
    }

    /// <summary>
    /// Apply avoidance for autonomous navigation.
    /// With DtCrowd, the velocity already includes avoidance — just return it.
    /// Kept for API compatibility with HelperSystem.
    /// </summary>
    public static Vector2 ApplyOrcaForNav(EntityWorld state, Entity self, Vector2 myPos, Vector2 vel)
    {
        return vel; // DtCrowd handles avoidance
    }

    public static void Reset() { }
}
