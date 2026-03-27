using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using System;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct StatePhase : IEquatable<StatePhase>
{
    public byte Value;

    public static readonly StatePhase Enter = new() { Value = 0 };
    public static readonly StatePhase Active = new() { Value = 1 };
    public static readonly StatePhase Exit = new() { Value = 2 };

    public bool Equals(StatePhase other) => Value == other.Value;
    public override bool Equals(object obj) => obj is StatePhase other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(StatePhase left, StatePhase right) => left.Value == right.Value;
    public static bool operator !=(StatePhase left, StatePhase right) => left.Value != right.Value;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("a3b4c5d6-e7f8-4a9b-0c1d-2e3f4a5b6c7d")]
public struct StateComponent : IComponent
{
    public FixedString32 Key;
    public StatePhase Phase;
    public int CurrentTime;
    public int MaxTime; // 0 = indefinite (no auto-advance)
    public bool IsEnabled;
}
