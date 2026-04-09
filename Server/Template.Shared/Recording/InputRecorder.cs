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
    private readonly List<RecordedCheckpoint> _checkpoints = new();
    private bool _recording;

    /// <summary>How often to capture a state hash checkpoint (in ticks). Default: 60 (1/sec at 60hz).</summary>
    public int CheckpointInterval { get; set; } = 60;

    public int ActionCount => _actions.Count;
    public int CheckpointCount => _checkpoints.Count;

    public InputRecorder(Game game)
    {
        _game = game;
    }

    /// <summary>
    /// Serialized initial state at the tick when recording started.
    /// When replaying, load this state before replaying actions to match
    /// the exact starting conditions (e.g. post full-state-sync).
    /// </summary>
    public byte[]? InitialState { get; private set; }

    /// <summary>Tick at which recording started.</summary>
    public long StartTick { get; private set; }

    /// <summary>
    /// Start recording. Hooks action events immediately and captures initial state.
    /// Call BEFORE WaitForSyncAsync to capture all actions including those from the
    /// initial sync. The initial state will be captured when CaptureInitialState()
    /// is called (typically after sync completes).
    /// </summary>
    public void Start()
    {
        if (_recording) return;
        _recording = true;
        _actions.Clear();
        _checkpoints.Clear();
        _game.Scheduler.OnActionScheduled += OnAction;
        _game.Loop.OnTick += OnTick;
        // Capture initial state now — caller can override via CaptureInitialState() later
        StartTick = _game.Loop.CurrentTick;
        InitialState = StateSerializer.Serialize(_game.State);
    }

    /// <summary>
    /// Re-capture the initial state at the current tick. Call this AFTER the full
    /// state sync completes so the recording starts from the authoritative state.
    /// </summary>
    public void CaptureInitialState()
    {
        StartTick = _game.Loop.CurrentTick;
        InitialState = StateSerializer.Serialize(_game.State);
    }

    public void Stop()
    {
        if (!_recording) return;
        _recording = false;
        _game.Scheduler.OnActionScheduled -= OnAction;
        _game.Loop.OnTick -= OnTick;
    }

    /// <summary>
    /// Save the recording to a binary file. Can be called while still recording.
    /// </summary>
    public void Save(string path)
    {
        long totalTicks = _game.Loop.CurrentTick;
        // Serialize once, hash from the same bytes
        byte[] finalState = StateSerializer.Serialize(_game.State);
        var finalHash = StateHasher.Hash(finalState);
        InputRecording.Save(path, totalTicks, _actions, finalHash, _checkpoints,
            InitialState, StartTick);
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>Save serialized state at every checkpoint (for byte-level diff debugging). Default: false.</summary>
    public bool CaptureStateAtCheckpoints { get; set; }

    private void OnTick()
    {
        long tick = _game.Loop.CurrentTick;
        if (tick > 0 && tick % CheckpointInterval == 0)
        {
            // During rollback resimulation, OnTick fires again for ticks we already
            // captured. Replace the old checkpoint with the new (post-rollback) state
            // since that's the authoritative result.
            byte[] serialized = StateSerializer.Serialize(_game.State);
            var hash = StateHasher.Hash(serialized);

            var checkpoint = new RecordedCheckpoint
            {
                Tick = tick,
                Hash = hash,
                StateData = CaptureStateAtCheckpoints ? serialized : null
            };

            // Replace existing checkpoint at same tick, or append
            for (int i = 0; i < _checkpoints.Count; i++)
            {
                if (_checkpoints[i].Tick == tick)
                {
                    _checkpoints[i] = checkpoint;
                    return;
                }
            }
            _checkpoints.Add(checkpoint);
        }
    }

    private void OnAction(DenseComponentId id, ReadOnlySpan<byte> data, int targetEntityId, long executeTick, long originalExecuteTick)
    {
        if (!_recording) return;

        // Convert DenseId -> StableId for portability
        if (!ComponentId.TryGetStable(id, out var stableId))
            return; // Unknown component, skip

        // Deduplicate: on the client, each action fires twice — once as prediction,
        // once as server confirmation (TickSnapshot). Only keep the first occurrence
        // for a given (tick, entity, componentType) triple to avoid double-execution
        // during replay.
        for (int i = _actions.Count - 1; i >= 0 && i >= _actions.Count - 20; i--)
        {
            var existing = _actions[i];
            if (existing.Tick == executeTick
                && existing.TargetEntityId == targetEntityId
                && existing.StableComponentId == stableId.Value)
            {
                return; // Duplicate — skip
            }
        }

        _actions.Add(new RecordedAction
        {
            Tick = executeTick,
            TargetEntityId = targetEntityId,
            StableComponentId = stableId.Value,
            Data = data.ToArray()
        });
    }
}
