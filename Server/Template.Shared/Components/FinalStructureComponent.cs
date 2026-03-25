using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("6fe9ac49-405b-439b-962f-06dd4715c7b9")]
public struct FinalStructureComponent : IComponent
{
    public int CurrentCoins;
    public int Threshold;
}
