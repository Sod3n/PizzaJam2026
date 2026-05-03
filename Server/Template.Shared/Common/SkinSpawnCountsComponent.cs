using System.Runtime.InteropServices;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Components;

/// <summary>
/// Tracks how many times each skin ID has been spawned globally.
/// Used by SkinsData to decay weights of frequently-spawned skins.
/// Stored in ECS state so it survives rollback and stays deterministic
/// across server/client.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("f3a7b2c1-9d4e-4f5a-8b6c-1e2d3f4a5b6c")]
public struct SkinSpawnCountsComponent : IComponent
{
    public Dictionary64<int, int> Counts;

    public int GetCount(int skinId)
    {
        Counts.TryGetValue(skinId, out int count);
        return count;
    }

    public void RecordSpawn(int skinId)
    {
        if (Counts.TryGetValue(skinId, out int count))
            Counts[skinId] = count + 1;
        else
            Counts.Add(skinId, 1);
    }
}
