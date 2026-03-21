using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using System.Runtime.InteropServices;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential)]
[StableId("c3d4e5f6-a7b8-4c9d-0e1f-2a3b4c5d6e7f")]
public struct CowComponent : IComponent
{
    public int Exhaust;
    public int MaxExhaust;
    public bool IsMilking;
    public Entity HouseId;
    public Vector2 SpawnPosition;
}
