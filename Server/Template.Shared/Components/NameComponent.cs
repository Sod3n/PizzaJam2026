using System.Runtime.InteropServices;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("b7a3e9d2-f5c1-4a8b-9d6e-3c2f1a0b4e5d")]
public struct NameComponent : IComponent
{
    public FixedString32 Name;

    private static readonly string[] CowNames =
    {
        "Bella", "Daisy", "Luna", "Bessie", "Clover", "Rosie", "Buttercup",
        "Penny", "Cookie", "Pepper", "Ginger", "Maple", "Cocoa", "Sunny",
        "Honey", "Patches", "Willow", "Pumpkin", "Peaches", "Sprout",
        "Pickles", "Nugget", "Muffin", "Waffles", "Cinnamon", "Truffle",
    };

    private static readonly string[] PetNames =
    {
        "Pip", "Bean", "Olive", "Hazel", "Fig", "Mochi", "Tofu",
        "Nori", "Sage", "Basil", "Mint", "Clove", "Thyme", "Dill",
        "Plum", "Berry", "Acorn", "Pebble", "Ember", "Coral",
    };

    public static NameComponent RandomCow(ref DeterministicRandom random)
    {
        return new NameComponent { Name = CowNames[random.NextInt(CowNames.Length)] };
    }

    public static NameComponent RandomPet(ref DeterministicRandom random)
    {
        return new NameComponent { Name = PetNames[random.NextInt(PetNames.Length)] };
    }
}
