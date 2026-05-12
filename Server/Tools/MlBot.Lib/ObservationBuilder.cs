using System.Collections.Generic;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace MlBot.Lib;

public static class ObservationBuilder
{
    public static WorldObservation Build(Game game, Entity player, int tick)
    {
        var obs = new WorldObservation { Tick = tick };

        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        {
            var r = game.State.GetComponent<GlobalResourcesComponent>(e);
            obs.Globals = new Globals
            {
                Coins = r.Coins,
                Milk = r.Milk,
                CarrotMilkshake = r.CarrotMilkshake,
                VitaminMix = r.VitaminMix,
                PurplePotion = r.PurplePotion,
                Grass = r.Grass,
                Carrot = r.Carrot,
                Apple = r.Apple,
                Mushroom = r.Mushroom,
                DayCounter = r.DayCounter,
                TotalBreedCount = r.TotalBreedCount,
                HelpersSpawned = r.HelpersSpawned,
                HelpersEnabled = r.HelpersEnabled,
            };
            break;
        }

        if (player != Entity.Null)
        {
            if (game.State.HasComponent<Transform2D>(player))
            {
                var pos = game.State.GetComponent<Transform2D>(player).Position;
                obs.Player.X = (float)pos.X;
                obs.Player.Y = (float)pos.Y;
            }
            if (game.State.HasComponent<PlayerStateComponent>(player))
            {
                obs.Player.PetCount = game.State.GetComponent<PlayerStateComponent>(player).PetCount;
            }
            if (game.State.HasComponent<StateComponent>(player))
            {
                var sc = game.State.GetComponent<StateComponent>(player);
                obs.Player.Active = sc.IsEnabled && sc.Phase == StatePhase.Active;
                obs.Player.StateKey = sc.Key.ToString();
            }
        }

        var lands = new List<LandInfo>();
        foreach (var e in game.State.Filter<LandComponent>())
        {
            var l = game.State.GetComponent<LandComponent>(e);
            var p = game.State.HasComponent<Transform2D>(e)
                ? game.State.GetComponent<Transform2D>(e).Position
                : Vector2.Zero;
            lands.Add(new LandInfo
            {
                Id = e.Id, X = (float)p.X, Y = (float)p.Y,
                Type = (int)l.Type, Locked = l.Locked,
                Threshold = l.Threshold, CurrentCoins = l.CurrentCoins, Ring = l.Ring,
            });
        }
        obs.Land = lands.ToArray();

        var buildings = new List<BuildingInfo>();
        AddBuildings<HouseComponent>(game, buildings, "House",
            (s, e) => s.GetComponent<HouseComponent>(e).CowId.Id);
        AddBuildings<LoveHouseComponent>(game, buildings, "LoveHouse", (_, _) => 0);
        AddBuildings<SellPointComponent>(game, buildings, "SellPoint", (_, _) => 0);
        AddBuildings<CarrotFarmComponent>(game, buildings, "CarrotFarm", (_, _) => 0);
        AddBuildings<AppleOrchardComponent>(game, buildings, "AppleOrchard", (_, _) => 0);
        AddBuildings<MushroomCaveComponent>(game, buildings, "MushroomCave", (_, _) => 0);
        AddBuildings<HelperAssistantComponent>(game, buildings, "HelperAssistant", (_, _) => 0);
        AddBuildings<WarehouseComponent>(game, buildings, "Warehouse", (_, _) => 0);
        AddBuildings<FinalStructureComponent>(game, buildings, "FinalStructure", (_, _) => 0);
        obs.Buildings = buildings.ToArray();

        var cows = new List<CowInfo>();
        foreach (var e in game.State.Filter<CowComponent>())
        {
            var c = game.State.GetComponent<CowComponent>(e);
            var p = game.State.HasComponent<Transform2D>(e)
                ? game.State.GetComponent<Transform2D>(e).Position
                : Vector2.Zero;
            cows.Add(new CowInfo
            {
                Id = e.Id, X = (float)p.X, Y = (float)p.Y,
                PreferredFood = c.PreferredFood,
                SecondaryPreferredFood = c.SecondaryPreferredFood,
                Exhaust = c.Exhaust, MaxExhaust = c.MaxExhaust,
                Depressed = c.IsDepressed, Milking = c.IsMilking,
                HouseId = c.HouseId.Id, FollowingPlayer = c.FollowingPlayer.Id,
            });
        }
        obs.Cows = cows.ToArray();

        var helpers = new List<HelperInfo>();
        foreach (var e in game.State.Filter<HelperComponent>())
        {
            var h = game.State.GetComponent<HelperComponent>(e);
            var p = game.State.HasComponent<Transform2D>(e)
                ? game.State.GetComponent<Transform2D>(e).Position
                : Vector2.Zero;
            helpers.Add(new HelperInfo
            {
                Id = e.Id, X = (float)p.X, Y = (float)p.Y,
                Type = h.Type, State = h.State,
                OwnerPlayer = h.OwnerPlayer.Id,
                WantedFoodType = h.WantedFoodType,
                BagTotal = h.GetBagTotal(), PetCount = h.PetCount,
            });
        }
        obs.Helpers = helpers.ToArray();

        var foods = new List<FoodInfo>();
        foreach (var e in game.State.Filter<GrassComponent>())
        {
            var p = game.State.HasComponent<Transform2D>(e)
                ? game.State.GetComponent<Transform2D>(e).Position
                : Vector2.Zero;
            foods.Add(new FoodInfo { Id = e.Id, X = (float)p.X, Y = (float)p.Y, Type = FoodType.Grass });
        }
        obs.Food = foods.ToArray();

        return obs;
    }

    private static void AddBuildings<T>(Game game, List<BuildingInfo> list, string type,
        System.Func<EntityWorld, Entity, int> getCow)
        where T : unmanaged, IComponent
    {
        foreach (var e in game.State.Filter<T>())
        {
            var p = game.State.HasComponent<Transform2D>(e)
                ? game.State.GetComponent<Transform2D>(e).Position
                : Vector2.Zero;
            list.Add(new BuildingInfo
            {
                Id = e.Id, X = (float)p.X, Y = (float)p.Y, Type = type,
                OccupantCowId = getCow(game.State, e),
            });
        }
    }
}
