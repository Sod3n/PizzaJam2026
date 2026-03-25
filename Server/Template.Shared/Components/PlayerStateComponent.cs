using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using System.Runtime.InteropServices;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Components;

public enum PlayerState
{
    Idle,
    EnteringMilking,
    Milking,
    ExitingMilking
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("e2f3a4b5-c6d7-e8f9-a0b1-c2d3e4f5a6b7")]
public struct PlayerStateComponent : IComponent
{
    public int State; // Cast to PlayerState enum
    public Entity InteractionTarget; // The cow we are milking, etc.
    public int MilkingTimer; 
    public Vector2 ReturnPosition;
}
