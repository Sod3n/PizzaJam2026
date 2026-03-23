using Deterministic.GameFramework.ECS;

namespace Template.Shared.Components;

[StableId("7babed6d-282d-4298-89cd-9f45bcf8a756")]
public struct InteractedComponent : IComponent
{
    public int Lifetime = 0;

    public InteractedComponent()
    {
    }
}