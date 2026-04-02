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
    public Entity PreviousHouseId; // Saved when entering love house, restored after breeding
    public Vector2 SpawnPosition;
    public Entity FollowingPlayer;
    public Entity FollowTarget; // Entity this cow actually follows (player or previous cow in chain)
    public int PreferredFood; // FoodType constant — this cow's preferred food gives 3x milk output
    public bool IsDepressed;  // Depressed after failed breed — hides in house, can't interact until exhaust recovers to 0
}
