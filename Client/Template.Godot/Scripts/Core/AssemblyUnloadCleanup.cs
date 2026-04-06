#if TOOLS
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class AssemblyUnloadCleanup
{
    private static readonly string LogPath = System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
        "unload_cleanup.log");

    [ModuleInitializer]
    public static void Initialize()
    {
        AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly())!
            .Unloading += _ =>
        {
            Log("=== Assembly unload cleanup started ===");

            Safe("FrameworkDebugBridge", () =>
            {
                Template.Godot.Framework.Editor.FrameworkDebugBridge.GetState = null;
                Template.Godot.Framework.Editor.FrameworkDebugBridge.IsRunning = null;
            });

            Safe("EntityViewModels", () =>
            {
                Template.Godot.Visuals.EntityViewModel.EntityViewModels.Clear();
                Template.Godot.Visuals.EntityViewModel.EntityVisualNodes.Clear();
            });

            Safe("ComponentId.ClearMappings", () =>
            {
                Deterministic.GameFramework.ECS.ComponentId.ClearMappings();
            });

            Safe("ServiceLocator.Reset", () =>
            {
                Deterministic.GameFramework.Common.ServiceLocator.Reset();
            });

            Log("=== Assembly unload cleanup finished ===");
        };
    }

    private static void Safe(string name, Action action)
    {
        try
        {
            action();
            Log($"  OK: {name}");
        }
        catch (Exception ex)
        {
            Log($"  FAIL: {name} — {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "godot_unload_cleanup.log");
            System.IO.File.AppendAllText(logPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }
}
#endif
