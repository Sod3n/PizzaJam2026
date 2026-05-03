// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("7677ab14-04a9-965c-960f-2d4db849f226")]
public struct PlayerEntity : IComponent
{
    public System.Guid UserId;
    public Entity Id;
    public FixedString32 Name;
}
