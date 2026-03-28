using Deterministic.GameFramework.ECS;

namespace Template.Godot.Framework.Editor;

/// <summary>
/// Static bridge allowing the debug visualizer to access game state
/// without directly referencing a project-specific GameManager.
///
/// Register in your GameManager / bootstrap code:
///   FrameworkDebugBridge.GetState = () => myGame.State;
///   FrameworkDebugBridge.IsRunning = () => IsGameRunning;
/// </summary>
public static class FrameworkDebugBridge
{
    public static System.Func<EntityWorld> GetState;
    public static System.Func<bool> IsRunning;
}
