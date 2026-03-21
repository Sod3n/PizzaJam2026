using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using System;
using System.Runtime.InteropServices;

namespace Template.Shared.Actions;

[StructLayout(LayoutKind.Sequential)]
[StableId("45d1256b-8cf7-5e53-cd19-1882c57de34f")]
public struct InteractAction : IAction
{
    public Guid UserId;
    
    // We don't necessarily need a target ID if we just interact with "nearest" thing, 
    // but explicit target is usually better for determinism and clarity. 
    // However, the prompt says "If we near grass", implying proximity check.
    // I'll stick to proximity check in the system for simplicity with the prompt's logic,
    // or passing the target entity ID is safer if the client determines it.
    // Given "If we near grass", the server should validate proximity.
    // Let's just pass UserId, and the system finds the nearest interactable.
}
