using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Navigation2D.Components;

namespace Template.Shared.Tests;

/// <summary>
/// Replaces the real NavigationSystem + PhysicsSystem.
/// Moves entities with NavigationAgent2D straight toward their target (no navmesh).
/// Also applies CharacterBody2D velocity to Transform2D (what physics would do).
/// </summary>
public class MockNavigationSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        // 1. Compute nav agent velocity (replaces NavigationSystem)
        foreach (var entity in state.Filter<NavigationAgent2D>())
        {
            ref var nav = ref state.GetComponent<NavigationAgent2D>(entity);
            if (nav.IsNavigationFinished) continue;
            if (!state.HasComponent<Transform2D>(entity)) continue;

            var pos = state.GetComponent<Transform2D>(entity).Position;
            var delta = nav.TargetPosition - pos;
            var distSq = (float)delta.SqrMagnitude;
            float desired = (float)nav.TargetDesiredDistance;

            if (distSq <= desired * desired + 0.1f)
            {
                nav.Velocity = Vector2.Zero;
                nav.IsNavigationFinished = true;
                continue;
            }

            float dist = (float)System.Math.Sqrt(distSq);
            float speed = (float)nav.MaxSpeed;
            nav.Velocity = delta * ((Float)(speed / dist));
        }

        // 2. Move entities by their CharacterBody2D velocity (replaces PhysicsSystem)
        Float dt = (Float)(1f / 60f);
        foreach (var entity in state.Filter<CharacterBody2D>())
        {
            if (!state.HasComponent<Transform2D>(entity)) continue;
            var vel = state.GetComponent<CharacterBody2D>(entity).Velocity;
            if ((float)vel.SqrMagnitude < 0.001f) continue;

            ref var t = ref state.GetComponent<Transform2D>(entity);
            t.Position = t.Position + vel * dt;
        }
    }
}
