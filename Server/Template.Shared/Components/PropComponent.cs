using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("c7d8e9f0-a1b2-4c3d-8e5f-6a7b8c9d0e1f")]
public struct PropComponent : IComponent
{
    /// <summary>
    /// The visual type of the prop (0=barrel, 1=bush1, 2=bush2, 3=flowers, 4=tree1).
    /// </summary>
    public int PropType;
}
