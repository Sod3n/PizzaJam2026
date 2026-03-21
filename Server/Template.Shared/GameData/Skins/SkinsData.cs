using Template.Shared.GameData.Models;
using Template.Shared.GameData.Core;
using System.Collections.Generic;
using System.Linq;
using Template.Shared.Components;
using Deterministic.GameFramework.Types;

namespace Template.Shared.GameData.Skins;

public class SkinsData : GameData<Skin>
{
    public override string Path => "Skins.json";

    private Dictionary<int, Skin> _skins = new();
    private Dictionary<string, List<Skin>> _skinsByType = new();

    public override void Load(Dictionary<string, Skin> entries)
    {
        _skins = entries.Values.ToDictionary(e => e.Id);
        
        _skinsByType = entries.Values
            .GroupBy(s => s.Type)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public Skin? Get(int id) => _skins.GetValueOrDefault(id);

    public Dictionary<int, Skin> GetAll() => _skins;

    public SkinComponent GenerateRandomSkin(ref DeterministicRandom random)
    {
        var component = new SkinComponent
        {
            Skins = new Dictionary16<FixedString32, int>()
        };

        foreach (var (type, skins) in _skinsByType)
        {
            if (skins.Count == 0) continue;
            
            var index = random.NextInt(skins.Count);
            var skin = skins[index];
            
            // Map the Skin Type (e.g. "Hair") to the Slot Name in the component
            component.Skins.Add(new FixedString32(type), skin.Id);
        }

        return component;
    }
}
