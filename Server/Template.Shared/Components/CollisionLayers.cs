using System;

namespace Template.Shared.Components;

[Flags]
public enum CollisionLayer : uint
{
    None = 0,
    Physics = 1,        // Player, Cow, Walls — physical collisions
    Coins = 2,          // Coin pickups
    Interactable = 4,   // Grass, SellPoint, Land, FinalStructure
    Zone = 8,           // Player interaction zone (Area2D)
}
