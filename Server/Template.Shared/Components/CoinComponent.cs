using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("5d2c8e1a-4b9f-4f8a-9c7d-2e3f1a0b5c6d")]
public struct CoinComponent : IComponent
{
    public int Value;
}
