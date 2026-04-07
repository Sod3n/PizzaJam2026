using System;
using System.Collections.Generic;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;

namespace Template.Shared.Recording;

/// <summary>
/// Records all actions scheduled during gameplay to a file.
///
/// Usage (in Godot client or server):
///   var recorder = new InputRecorder(game);
///   recorder.Start();
///   // ... play the game ...
///   recorder.Stop();
///   recorder.Save("recording.bin");
///
/// The recording captures every action that flows through the ActionScheduler,
/// including player inputs, server-initiated actions, etc.
/// </summary>
public class InputRecorder : IDisposable
{
    private readonly Game _game;
    private readonly List<RecordedAction> _actions = new();
    private bool _recording;

    public int ActionCount => _actions.Count;

    public InputRecorder(Game game)
    {
        _game = game;
    }

    public void Start()
    {
        if (_recording) return;
        _recording = true;
        _actions.Clear();
        _game.Scheduler.OnActionScheduled += OnAction;
    }

    public void Stop()
    {
        if (!_recording) return;
        _recording = false;
        _game.Scheduler.OnActionScheduled -= OnAction;
    }

    /// <summary>
    /// Save the recording to a binary file. Call after Stop().
    /// </summary>
    public void Save(string path)
    {
        long totalTicks = _game.Loop.CurrentTick;
        var finalHash = StateHasher.Hash(_game.State);
        InputRecording.Save(path, totalTicks, _actions, finalHash);
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnAction(DenseComponentId id, ReadOnlySpan<byte> data, int targetEntityId, long executeTick)
    {
        if (!_recording) return;

        // Convert DenseId -> StableId for portability
        if (!ComponentId.TryGetStable(id, out var stableId))
            return; // Unknown component, skip

        _actions.Add(new RecordedAction
        {
            Tick = executeTick,
            TargetEntityId = targetEntityId,
            StableComponentId = stableId.Value,
            Data = data.ToArray()
        });
    }
}
