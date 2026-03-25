using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("8f1d3c2b-5a6e-4d9f-8b7c-1e2a3f4b5c6d")]
public struct ScoreComponent : IComponent
{
    public int Value;
}
