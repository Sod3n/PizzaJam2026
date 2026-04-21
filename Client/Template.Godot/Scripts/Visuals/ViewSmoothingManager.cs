// ViewSmoothingManager - Godot bridge for ViewSmoother.
//
// Provides a singleton ViewSmoother that:
//   - Binds to GameManager.Instance.Game.State when the game is running.
//   - Ticks every render frame via _Process(double delta).
//   - Resets all trackers when the GameClient applies a FullState, so visuals
//     snap to the authoritative value instead of drifting from a stale one.
//
// Views use ViewHelpers.SetupPositionSmoothing(vm, node) to register; the
// tracker is disposed when the ViewModel's Disposables are disposed.

using System;
using Godot;
using Template.Godot.Core;

namespace Template.Godot.Visuals;

/// <summary>
/// Autoload-friendly singleton that owns the frame-driven ViewSmoother.
/// Add as an AutoLoad in project.godot, or instance in the main scene.
///
/// If no instance exists at runtime, one is created lazily on first access
/// and parented to the active SceneTree root, so existing scenes keep working.
/// </summary>
public partial class ViewSmoothingManager : Node
{
    private static ViewSmoothingManager _instance;
    public static ViewSmoothingManager Instance => _instance;

    /// <summary>
    /// Active smoother. Null until the game loop has started and state is bound.
    /// Views should null-check before registering (early-spawned views).
    /// </summary>
    public static ViewSmoother Smoother { get; private set; }

    public override void _EnterTree()
    {
        if (_instance != null && _instance != this)
        {
            QueueFree();
            return;
        }
        _instance = this;

        // Ensure we are always processed last relative to ViewModel tick-driven
        // updates, so the smoother reads the freshest ECS value each frame.
        ProcessPriority = 100;
    }

    public override void _Ready()
    {
        // Eagerly attach the Smoother when the game state is ready. For offline
        // / lobby flows, GameManager invokes OnGameStarted after Game.State is
        // live, giving us a deterministic moment to bind.
        var gm = GameManager.Instance;
        if (gm != null)
        {
            // If the game is already running (e.g. this manager was spawned
            // after start), create immediately.
            if (gm.Game != null && gm.Game.State != null)
                EnsureSmoother();
            gm.OnGameStarted += EnsureSmoother;
        }
    }

    public override void _ExitTree()
    {
        var gm = GameManager.Instance;
        if (gm != null)
            gm.OnGameStarted -= EnsureSmoother;

        if (_instance == this)
        {
            _instance = null;
            Smoother?.Dispose();
            Smoother = null;
        }
    }

    public override void _Process(double delta)
    {
        // Belt-and-braces: if something created the manager before the game
        // was ready, keep polling until state is bound.
        if (Smoother == null) EnsureSmoother();
        Smoother?.Update((float)delta);
    }

    private bool _resetHookInstalled;

    private void EnsureSmoother()
    {
        if (Smoother != null)
        {
            InstallResetHook();
            return;
        }

        var gm = GameManager.Instance;
        if (gm == null) return;
        var game = gm.Game;
        if (game == null || game.State == null) return;

        Smoother = new ViewSmoother(game.State);
        InstallResetHook();
    }

    private void InstallResetHook()
    {
        if (_resetHookInstalled || Smoother == null) return;
        var gm = GameManager.Instance;
        if (gm == null || gm.GameClient == null) return;

        // When the reactive system is unpaused (right after a FullState catch-up
        // completes — see GameClient.UnpauseReactiveAfterCatchUp), snap visuals
        // to the authoritative value. Without this the exponential lerp would
        // slowly drift toward the corrected position, producing a visible "crawl".
        //
        // We can't easily hook "after FullState apply" directly, so we piggyback
        // on a per-tick check: if the reactive system just unpaused, reset.
        gm.Game.Loop.OnTick += TryResetAfterUnpause;
        _resetHookInstalled = true;
    }

    private bool _wasPaused;

    private void TryResetAfterUnpause()
    {
        var reactive = GameManager.Instance?.GameClient?.Reactive;
        if (reactive == null) return;

        bool paused = reactive.IsPaused;
        if (_wasPaused && !paused)
        {
            // Transitioned from paused -> running: FullState catch-up finished.
            Smoother?.ResetAll();
        }
        _wasPaused = paused;
    }

    /// <summary>
    /// Helper: ensure the manager exists in the current scene tree.
    /// Call from GameManager if you don't want to configure an AutoLoad.
    /// </summary>
    public static ViewSmoothingManager EnsureExists(SceneTree tree)
    {
        if (_instance != null) return _instance;
        if (tree?.Root == null) return null;

        var manager = new ViewSmoothingManager { Name = "ViewSmoothingManager" };
        tree.Root.CallDeferred("add_child", manager);
        return manager;
    }
}
