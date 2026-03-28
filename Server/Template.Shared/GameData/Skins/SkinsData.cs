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
            Skins = new Dictionary16<FixedString32, int>(),
            Colors = new Dictionary16<FixedString32, int>()
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

            var key = new FixedString32(type);
            component.Skins.Add(key, selectedSkin.Id);

            var palette = GetColorPalette(type);
            if (palette != null)
                component.Colors.Add(key, PickWeightedColor(ref random, palette));
        }

        // Body isn't in skins data but exists as a sprite node — give it a skin tone
        // Top shares the same skin tone (its sprites contain visible skin)
        var skinTone = PickWeightedColor(ref random, SkinToneColors);
        component.Colors.Add(new FixedString32("Body"), skinTone);
        if (component.Skins.ContainsKey(new FixedString32("Top")))
            component.Colors.Add(new FixedString32("Top"), skinTone);

        return component;
    }

    public SkinComponent CrossbreedSkin(ref DeterministicRandom random, SkinComponent parentA, SkinComponent parentB)
    {
        var component = new SkinComponent
        {
            Skins = new Dictionary16<FixedString32, int>(),
            Colors = new Dictionary16<FixedString32, int>()
        };

        var sortedTypes = _skinsByType.Keys.ToList();
        sortedTypes.Sort();

        foreach (var type in sortedTypes)
        {
            var key = new FixedString32(type);
            bool aHas = parentA.Skins.ContainsKey(key);
            bool bHas = parentB.Skins.ContainsKey(key);
            var palette = GetColorPalette(type);

            int roll = random.NextInt(100);

            if (roll < 40 && aHas)
            {
                // Inherit from parent A
                component.Skins.Add(key, parentA.Skins[key]);
                if (palette != null)
                    component.Colors.Add(key, parentA.Colors.ContainsKey(key)
                        ? parentA.Colors[key]
                        : PickWeightedColor(ref random, palette));
            }
            else if (roll < 80 && bHas)
            {
                // Inherit from parent B
                component.Skins.Add(key, parentB.Skins[key]);
                if (palette != null)
                    component.Colors.Add(key, parentB.Colors.ContainsKey(key)
                        ? parentB.Colors[key]
                        : PickWeightedColor(ref random, palette));
            }
            else
            {
                // Random mutation
                var skins = _skinsByType[type];
                var totalWeight = _totalWeightByType[type];

                if (skins.Count == 0 || totalWeight <= 0) continue;

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

                selectedSkin ??= skins.Last();
                component.Skins.Add(key, selectedSkin.Id);
                if (palette != null)
                    component.Colors.Add(key, PickWeightedColor(ref random, palette));
            }
        }

        // Crossbreed skin tone for Body + Top
        var bodyKey = new FixedString32("Body");
        var topKey = new FixedString32("Top");
        int skinToneRoll = random.NextInt(100);
        int skinTone;
        if (skinToneRoll < 40 && parentA.Colors.ContainsKey(bodyKey))
            skinTone = parentA.Colors[bodyKey];
        else if (skinToneRoll < 80 && parentB.Colors.ContainsKey(bodyKey))
            skinTone = parentB.Colors[bodyKey];
        else
            skinTone = PickWeightedColor(ref random, SkinToneColors);

        component.Colors.Add(bodyKey, skinTone);
        if (component.Skins.ContainsKey(topKey))
            component.Colors.Add(topKey, skinTone);

        return component;
    }

    // (packed 0xRRGGBB, weight)
    private static readonly (int Color, int Weight)[] HairColors =
    {
        (0x1A1A1A, 25), // Black
        (0x3B2218, 25), // Dark brown
        (0x6B3A2A, 20), // Brown
        (0x8B6914, 15), // Light brown
        (0xD4A340, 10), // Blonde
        (0xA13D2D, 8),  // Ginger
        (0xE8D5B0, 5),  // Platinum
        (0xFF69B4, 2),  // Pink
        (0x3366FF, 1),  // Blue
        (0x33CC33, 1),  // Green
    };

    private static readonly (int Color, int Weight)[] EyeColors =
    {
        (0x634E34, 30), // Brown
        (0x3B2218, 25), // Dark brown
        (0x4488CC, 15), // Blue
        (0x3D8B37, 12), // Green
        (0x8E7618, 10), // Hazel
        (0x8899AA, 5),  // Gray
        (0xCCAA44, 5),  // Amber
        (0xCC3333, 1),  // Red
    };

    private static readonly (int Color, int Weight)[] BottomColors =
    {
        (0x2B3A67, 20), // Navy
        (0x1A1A2E, 18), // Dark indigo
        (0x4A4A4A, 15), // Dark gray
        (0x6B4226, 12), // Brown
        (0x8B7355, 10), // Khaki
        (0x2E5E3E, 8),  // Dark green
        (0x8B2252, 5),  // Maroon
        (0xCC4444, 3),  // Red
        (0xDD8822, 2),  // Orange
        (0x6633CC, 2),  // Purple
    };

    private static readonly (int Color, int Weight)[] SkinToneColors =
    {
        (0xFFDBC4, 20), // Light
        (0xF5C5A3, 20), // Fair
        (0xE8AD84, 18), // Medium light
        (0xD49A6A, 15), // Medium
        (0xC08050, 12), // Tan
        (0x8D5524, 10), // Brown
        (0x6B3E26, 8),  // Dark brown
        (0x4A2912, 5),  // Deep brown
    };

    // Returns palette for types handled inside the main loop.
    // Body and Top are handled separately (shared skin tone).
    private static (int Color, int Weight)[]? GetColorPalette(string type) => type switch
    {
        "Hair" => HairColors,
        "Eyes" => EyeColors,
        "Bottom1" => BottomColors,
        _ => null
    };

    private static int PickWeightedColor(ref DeterministicRandom random, (int Color, int Weight)[] palette)
    {
        int totalWeight = 0;
        foreach (var entry in palette)
            totalWeight += entry.Weight;

        int target = random.NextInt(totalWeight);
        int sum = 0;
        foreach (var entry in palette)
        {
            sum += entry.Weight;
            if (target < sum)
                return entry.Color;
        }
        return palette[palette.Length - 1].Color;
    }
}