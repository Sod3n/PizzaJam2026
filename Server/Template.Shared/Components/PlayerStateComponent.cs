// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("870d8ebd-fec0-6a51-ae7f-a00f2712a1ce")]
public struct PlayerStateComponent : IComponent
{
    public Entity InteractionTarget;
    public Entity InteractionTarge10;
    public Vector2 ReturnPosition;
    public Entity InteractionZone;
    public Entity FollowingCow;
    public Entity AssistantHelper;
}
