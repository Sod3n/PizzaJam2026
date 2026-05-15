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
    public Entity HighlightTarget;
    public Entity HoldLockedTarget;
    public Vector2 ReturnPosition;
    public Entity InteractionZone;
    public Entity FollowingCow;
    public Entity AssistantHelper;
    /// <summary>
    /// The single thing the player is physically holding — a pet (cat) or a hammer.
    /// Mutually exclusive: you can't carry both. Disambiguate by component type on the
    /// entity (HelperPetComponent, HammerComponent) at the use site.
    /// </summary>
    public Entity CarriedEntity;
    /// <summary>
    /// Helper trailing the player like a cow in a follow chain — not carried, just queued
    /// for the next house assignment. Cleared when the player drops it or assigns it to a house.
    /// </summary>
    public Entity FollowingHelper;
    public int PetCount;
    public Entity CameraTarget;
}
