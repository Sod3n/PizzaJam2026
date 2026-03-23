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
    // Cache the total weight per type for performance
    private Dictionary<string, int> _totalWeightByType = new();

    public override void Load(Dictionary<string, Skin> entries)
    {
        _skins = entries.Values.ToDictionary(e => e.Id);
        
        _skinsByType = entries.Values
            .GroupBy(s => s.Type)
            .ToDictionary(g => g.Key, g => 
            {
                var list = g.ToList();
                // Sorting by Id ensures the order is consistent before calculating cumulative weight
                list.Sort((a, b) => a.Id.CompareTo(b.Id)); 
                return list;
            });

        _totalWeightByType = _skinsByType.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Sum(s => s.Weight)
        );
    }

    public Skin? Get(int id) => _skins.GetValueOrDefault(id);

    public Dictionary<int, Skin> GetAll() => _skins;

    public SkinComponent GenerateRandomSkin(ref DeterministicRandom random)
    {
        var component = new SkinComponent
        {
            Skins = new Dictionary16<FixedString32, int>()
        };

        var sortedTypes = _skinsByType.Keys.ToList();
        sortedTypes.Sort();

        foreach (var type in sortedTypes)
        {
            var skins = _skinsByType[type];
            var totalWeight = _totalWeightByType[type];
            
            if (skins.Count == 0 || totalWeight <= 0) continue;
            
            // Get a random value between [0, totalWeight)
            int targetWeight = random.NextInt(totalWeight);
            int currentWeightSum = 0;
            Skin? selectedSkin = null;

            foreach (var skin in skins)
            {
                currentWeightSum += skin.Weight;
                if (targetWeight < currentWeightSum)
                {
                    selectedSkin = skin;
                    break;
                }
            }

            // Fallback to last skin if something goes wrong with floating point or rounding
            selectedSkin ??= skins.Last();
            
            component.Skins.Add(new FixedString32(type), selectedSkin.Id);
        }

        return component;
    }
}