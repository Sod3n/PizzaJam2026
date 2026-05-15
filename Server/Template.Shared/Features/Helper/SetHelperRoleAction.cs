using System;
using System.Runtime.InteropServices;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;

namespace Template.Shared.Actions;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("c7e1d2a3-4b5c-4d6e-8f90-a1b2c3d4e5f6")]
public struct SetHelperRoleAction : IAction
{
    public Guid UserId;
    public int HelperEntityId;
}
