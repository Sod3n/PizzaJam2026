using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("e1f2a3b4-c5d6-4e7f-8901-2a3b4c5d6e7f")]
public struct PlayerHouseComponent : IComponent
{
    // Cooldown lives on a sibling CooldownComponent (Unit=Ticks).

    /// <summary>Forwards to <see cref="GameData.Balance.PlayerHouse.SleepCooldownTicks"/> for back-compat.</summary>
    public const int SleepCooldownTicks = GameData.Balance.PlayerHouse.SleepCooldownTicks;
}
