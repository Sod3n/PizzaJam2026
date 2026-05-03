// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("ea7a3602-1b4e-d355-8a64-c0f7a1c6836b")]
public struct TestEntityComponent : IComponent
{
    public int Health;
    public int MaxHealth;
    public Float Speed;
    public bool IsAlive;
    public Entity Target;
}
