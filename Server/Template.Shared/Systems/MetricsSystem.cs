using System;
using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class MetricsSystem : ISystem
{
    private const int SampleInterval = 60; // Every 1 second at 60fps

    public void Update(EntityWorld state)
    {
        foreach (var metricsEntity in state.Filter<MetricsComponent>())
        {
            ref var m = ref state.GetComponent<MetricsComponent>(metricsEntity);
            m.ElapsedTicks++;

            if (m.ElapsedTicks % SampleInterval != 0) return;

            // Read current resources from GlobalResourcesComponent
            int curFood = 0, curMilk = 0, curCoins = 0;
            foreach (var e in state.Filter<GlobalResourcesComponent>())
            {
                var r = state.GetComponent<GlobalResourcesComponent>(e);
                curFood = r.Grass + r.Carrot + r.Apple + r.Mushroom;
                curMilk = r.Milk + r.VitaminShake + r.AppleYogurt + r.PurplePotion;
                curCoins = r.Coins;
                break;
            }

            // Cumulative deltas: any increase from previous sample = new production
            if (curFood > m.PrevFood) m.CumFood += curFood - m.PrevFood;
            if (curMilk > m.PrevMilk) m.CumMilk += curMilk - m.PrevMilk;
            if (curCoins > m.PrevCoins) m.CumCoins += curCoins - m.PrevCoins;
            m.PrevFood = curFood;
            m.PrevMilk = curMilk;
            m.PrevCoins = curCoins;

            // Peak tracking
            if (curCoins > m.PeakCoins) m.PeakCoins = curCoins;
            if (curFood > m.PeakFood) m.PeakFood = curFood;
            if (curMilk > m.PeakMilk) m.PeakMilk = curMilk;

            // Bottleneck tracking
            m.SampleCount++;
            if (curFood == 0) m.FoodZeroTicks++;
            if (curMilk == 0) m.MilkZeroTicks++;
            if (curCoins == 0) m.CoinZeroTicks++;

            // Entity counts — reset and recount
            m.Houses = 0;
            m.LoveHouses = 0;
            m.SellPoints = 0;
            m.FoodFarms = 0;
            m.Helpers = 0;
            m.Pets = 0;
            m.Cows = 0;
            m.HousedCows = 0;
            m.WildCows = 0;
            m.LandPlots = 0;
            m.TotalLandCost = 0;
            m.FinalStructureBuilt = 0;

            foreach (var _ in state.Filter<HouseComponent>()) m.Houses++;
            foreach (var _ in state.Filter<LoveHouseComponent>()) m.LoveHouses++;
            foreach (var _ in state.Filter<SellPointComponent>()) m.SellPoints++;
            foreach (var _ in state.Filter<CarrotFarmComponent>()) m.FoodFarms++;
            foreach (var _ in state.Filter<AppleOrchardComponent>()) m.FoodFarms++;
            foreach (var _ in state.Filter<MushroomCaveComponent>()) m.FoodFarms++;

            foreach (var he in state.Filter<HelperComponent>())
            {
                if (state.GetComponent<HelperComponent>(he).OwnerPlayer != Entity.Null)
                    m.Helpers++;
            }

            foreach (var _ in state.Filter<HelperPetComponent>()) m.Pets++;
            foreach (var _ in state.Filter<FinalStructureComponent>()) m.FinalStructureBuilt = 1;

            foreach (var e in state.Filter<CowComponent>())
            {
                m.Cows++;
                var cow = state.GetComponent<CowComponent>(e);
                if (cow.HouseId != Entity.Null) m.HousedCows++;
                else if (cow.FollowingPlayer != Entity.Null) { /* following — not wild */ }
                else m.WildCows++;
            }

            if (m.Cows > m.PeakCows) m.PeakCows = m.Cows;
            if (m.Houses > m.PeakHouses) m.PeakHouses = m.Houses;

            foreach (var e in state.Filter<LandComponent>())
            {
                var land = state.GetComponent<LandComponent>(e);
                if (land.Locked == 0)
                {
                    m.LandPlots++;
                    m.TotalLandCost += Math.Max(0, land.Threshold - land.CurrentCoins);
                }
            }
        }
    }
}
