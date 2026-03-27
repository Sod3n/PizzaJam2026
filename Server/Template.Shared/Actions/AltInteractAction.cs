using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using System;
using System.Runtime.InteropServices;

namespace Template.Shared.Actions;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("71a2b3c4-d5e6-4f78-9a0b-1c2d3e4f5a6b")]
public struct AltInteractAction : IAction
{
    public Guid UserId;
}
