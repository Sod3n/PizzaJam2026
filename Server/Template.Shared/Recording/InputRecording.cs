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
/// Binary format for recorded game inputs. Dead simple:
///
/// Header:  [int32 version][int64 totalTicks][int32 actionCount]
/// Actions: [int64 tick][int32 entityId][guid componentId][int32 dataLen][byte[] data] * N
/// Footer:  [guid finalStateHash]
///
/// To re-record: play the game with InputRecorder enabled, it writes this file.
/// To test: load file, replay all actions, compare final hash.
/// </summary>
public static class InputRecording
{
    public const int FormatVersion = 1;

    public static void Save(string path, long totalTicks, List<RecordedAction> actions, Guid finalStateHash)
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
    }

    public static (long totalTicks, List<RecordedAction> actions, Guid finalStateHash) Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var r = new BinaryReader(fs);

        // Header
        int version = r.ReadInt32();
        if (version != FormatVersion)
            throw new InvalidDataException($"Unknown recording version {version}, expected {FormatVersion}");

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

        return (totalTicks, actions, finalStateHash);
    }
}
