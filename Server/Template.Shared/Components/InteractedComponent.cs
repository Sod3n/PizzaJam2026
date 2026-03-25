using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("7babed6d-282d-4298-89cd-9f45bcf8a756")]
public struct InteractedComponent : IComponent
{
    public int Lifetime = 0;

    public InteractedComponent()
    {
    }
}