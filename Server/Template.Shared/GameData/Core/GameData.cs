using System.Collections.Generic;

namespace Template.Shared.GameData.Core;

public abstract class GameData<TEntry>
{
    public abstract string Path { get; }
    public abstract void Load(Dictionary<string, TEntry> entries);
}

public abstract class GameData<TKey, TEntry> : GameData<TEntry>
{
    // Optional overload for specific key types if needed, 
    // but the base Load takes string keys from JSON object properties.
}
