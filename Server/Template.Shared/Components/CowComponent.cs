// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("fcf83639-f988-e35a-8fcc-1f0ebc71fb9e")]
public struct CowComponent : IComponent
{
    public int Exhaust;
    public int MaxExhaust;
    public bool IsMilking;
    public Entity HouseId;
    public Vector2 SpawnPosition;
    public Entity FollowingPlayer;
}
