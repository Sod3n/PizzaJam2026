using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("7c651bc1-6b34-4e75-b5a3-25ec4d824d80")]
public struct PlayerEntity : IComponent
{
    public System.Guid UserId;
    public Entity Id;
    public FixedString32 Name;
}
