using System;
using System.Collections.Generic;
using System.IO;

namespace Template.Shared.Recording;

/// <summary>
/// A recorded action: everything needed to replay a single player input.
/// Uses StableId (Guid) for the component type so recordings are portable
/// across builds where DenseId ordering may change.
/// </summary>
public struct RecordedAction
{
    public long Tick;
    public int TargetEntityId;
    public Guid StableComponentId;
    public byte[] Data;
}

/// <summary>
/// A state hash checkpoint captured at a specific tick during recording.
/// </summary>
public struct RecordedCheckpoint
{
    public long Tick;
    public Guid Hash;
    /// <summary>Serialized state at this tick. Only populated for the first checkpoint (for debugging).</summary>
    public byte[]? StateData;
}

/// <summary>
/// Binary format for recorded game inputs.
///
/// v1 Header:  [int32 version=1][int64 totalTicks][int32 actionCount]
///    Actions: [int64 tick][int32 entityId][guid componentId][int32 dataLen][byte[] data] * N
///    Footer:  [guid finalStateHash]
///
/// v2 Header:  [int32 version=2][int64 totalTicks][int32 actionCount]
///    Actions: (same as v1)
///    Footer:  [guid finalStateHash]
///    Checkpoints: [int32 checkpointCount][int64 tick + guid hash] * N
///
/// To re-record: play the game with InputRecorder enabled, it writes this file.
/// To test: load file, replay all actions, compare final hash and checkpoints.
/// </summary>
public static class InputRecording
{
    public const int FormatVersion = 3;

    public static void Save(string path, long totalTicks, List<RecordedAction> actions,
        Guid finalStateHash, List<RecordedCheckpoint>? checkpoints = null,
        byte[]? initialState = null, long startTick = 0)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        // Header
        w.Write(FormatVersion);
        w.Write(totalTicks);
        w.Write(actions.Count);

        // Actions
        foreach (var a in actions)
        {
            w.Write(a.Tick);
            w.Write(a.TargetEntityId);
            w.Write(a.StableComponentId.ToByteArray());
            w.Write(a.Data.Length);
            w.Write(a.Data);
        }

        // Footer
        w.Write(finalStateHash.ToByteArray());

        // Checkpoints (v2)
        var cps = checkpoints ?? new List<RecordedCheckpoint>();
        w.Write(cps.Count);
        foreach (var cp in cps)
        {
            w.Write(cp.Tick);
            w.Write(cp.Hash.ToByteArray());
            // State data: write length + bytes (0 length = no data)
            int stateLen = cp.StateData?.Length ?? 0;
            w.Write(stateLen);
            if (stateLen > 0)
                w.Write(cp.StateData!);
        }

        // Initial state (v3) — the serialized EntityWorld at the tick recording started
        int initialLen = initialState?.Length ?? 0;
        w.Write(startTick);
        w.Write(initialLen);
        if (initialLen > 0)
            w.Write(initialState!);
    }

    public static (long totalTicks, List<RecordedAction> actions, Guid finalStateHash,
        List<RecordedCheckpoint> checkpoints, byte[]? initialState, long startTick) Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var r = new BinaryReader(fs);

        // Header
        int version = r.ReadInt32();
        if (version < 1 || version > FormatVersion)
            throw new InvalidDataException($"Unknown recording version {version}, expected 1-{FormatVersion}");

        long totalTicks = r.ReadInt64();
        int actionCount = r.ReadInt32();

        // Actions
        var actions = new List<RecordedAction>(actionCount);
        for (int i = 0; i < actionCount; i++)
        {
            var a = new RecordedAction
            {
                Tick = r.ReadInt64(),
                TargetEntityId = r.ReadInt32(),
                StableComponentId = new Guid(r.ReadBytes(16)),
            };
            int dataLen = r.ReadInt32();
            a.Data = r.ReadBytes(dataLen);
            actions.Add(a);
        }

        // Footer
        var finalStateHash = new Guid(r.ReadBytes(16));

        // Checkpoints (v2+)
        var checkpoints = new List<RecordedCheckpoint>();
        if (version >= 2 && r.BaseStream.Position < r.BaseStream.Length)
        {
            int cpCount = r.ReadInt32();
            for (int i = 0; i < cpCount; i++)
            {
                var cpTick = r.ReadInt64();
                var cpHash = new Guid(r.ReadBytes(16));
                byte[]? stateData = null;
                int stateLen = r.ReadInt32();
                if (stateLen > 0)
                    stateData = r.ReadBytes(stateLen);
                checkpoints.Add(new RecordedCheckpoint
                {
                    Tick = cpTick,
                    Hash = cpHash,
                    StateData = stateData
                });
            }
        }

        // Initial state (v3+)
        byte[]? initialState = null;
        long startTick = 0;
        if (version >= 3 && r.BaseStream.Position < r.BaseStream.Length)
        {
            startTick = r.ReadInt64();
            int initialLen = r.ReadInt32();
            if (initialLen > 0)
                initialState = r.ReadBytes(initialLen);
        }

        return (totalTicks, actions, finalStateHash, checkpoints, initialState, startTick);
    }
}
