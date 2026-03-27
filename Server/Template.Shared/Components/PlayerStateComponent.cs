using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("e2f3a4b5-c6d7-e8f9-a0b1-c2d3e4f5a6b7")]
public struct PlayerStateComponent : IComponent
{
    public Entity InteractionTarget;
    public Vector2 ReturnPosition;
    public Entity InteractionZone;
    public Entity FollowingCow;
}
