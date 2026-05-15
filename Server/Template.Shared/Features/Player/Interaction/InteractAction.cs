using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using System;
using System.Runtime.InteropServices;

namespace Template.Shared.Actions;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("45d1256b-8cf7-5e53-cd19-1882c57de34f")]
public struct InteractAction : IAction
{
    public Guid UserId;
    public bool IsHoldRepeat;
}
