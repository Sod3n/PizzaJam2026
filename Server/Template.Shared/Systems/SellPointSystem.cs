using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Click on the sell point. Cow days (every Nth day) sell the front cow in the follow chain;
// helper-players can't sell cows so they fall through to the milk path. Other days sell one
// milk per click.
public class SellPointSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var sellPointEntity = req.Target;
            if (!state.HasComponent<SellPointComponent>(sellPointEntity)) continue;
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            HandleSell(state, playerEntity, sellPointEntity);
        }
    }

    private static void HandleSell(EntityWorld state, Entity playerEntity, Entity sellPointEntity)
    {
        var ctx = new Context(state, playerEntity, null!);
        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return;

        bool cowDay;
        {
            var globalRes = state.GetComponent<GlobalResourcesComponent>(grEntity);
            cowDay = (globalRes.DayCounter % Balance.Sell.DayCycle) == Balance.Sell.CowDayRemainder;
        }

        if (cowDay)
        {
            if (state.HasComponent<HelperPlayerComponent>(playerEntity))
            {
                InteractFeedback.MissingResource(ctx, playerEntity, sellPointEntity, StateKeys.Cows);
                return;
            }

            Entity cowEntity;
            {
                var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
                cowEntity = ps.FollowingCow;
            }
            if (cowEntity == Entity.Null || !state.HasComponent<CowComponent>(cowEntity))
            {
                InteractFeedback.MissingResource(ctx, playerEntity, sellPointEntity, StateKeys.Cows);
                return;
            }

            {
                var cow = state.GetComponent<CowComponent>(cowEntity);
                if (cow.IsMilking || cow.IsDepressed) return;
            }

            int activeCowCount = 0;
            foreach (var ce in state.Filter<CowComponent>())
            {
                if (state.HasComponent<CowForSaleComponent>(ce)) continue;
                activeCowCount++;
                if (activeCowCount > 1) break;
            }
            if (activeCowCount <= 1)
            {
                InteractFeedback.MissingResource(ctx, playerEntity, sellPointEntity, StateKeys.Cows);
                return;
            }

            int rested, preferredFood, price;
            {
                var cow = state.GetComponent<CowComponent>(cowEntity);
                rested = System.Math.Max(0, cow.MaxExhaust - cow.Exhaust);
                preferredFood = cow.PreferredFood;
                int tierBonus = (cow.PreferredFood + 1) * Balance.Sell.CowTierPrice;
                price = Balance.Sell.CowBasePrice + tierBonus + rested * Balance.Sell.CowRestedPrice;
            }

            {
                ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                globalRes.Coins += price;
            }

            Entity nextCow = Entity.Null;
            foreach (var ce in state.Filter<CowComponent>())
            {
                if (ce == cowEntity) continue;
                var c = state.GetComponent<CowComponent>(ce);
                if (c.FollowTarget == cowEntity && c.FollowingPlayer == playerEntity)
                { nextCow = ce; break; }
            }

            {
                ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
                cow.FollowingPlayer = Entity.Null;
                cow.FollowTarget = Entity.Null;
                cow.HouseId = Entity.Null;
            }

            state.AddComponent(cowEntity, new CowForSaleComponent());

            if (state.HasComponent<Transform2D>(sellPointEntity) && state.HasComponent<Transform2D>(cowEntity))
            {
                var sellPos = state.GetComponent<Transform2D>(sellPointEntity).Position;
                var gameTime = state.GetCustomData<IGameTime>();
                uint seed = (uint)(cowEntity.Id * 7919 + (gameTime?.CurrentTick ?? 0));
                var rng = new DeterministicRandom(seed);
                Float angle = rng.NextFloat((Float)0, (Float)6.2831853f);
                Float radius = rng.NextFloat((Float)2.5f, (Float)5.5f);
                var offset = new Vector2(Float.Cos(angle) * radius, Float.Sin(angle) * radius);
                {
                    ref var ct = ref state.GetComponent<Transform2D>(cowEntity);
                    ct.Position = sellPos + offset;
                }
                if (state.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowEntity))
                {
                    ref var body = ref state.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowEntity);
                    body.Velocity = Vector2.Zero;
                }
            }

            if (nextCow != Entity.Null)
            {
                {
                    ref var nc = ref state.GetComponent<CowComponent>(nextCow);
                    nc.FollowTarget = playerEntity;
                }
                {
                    ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
                    ps.FollowingCow = nextCow;
                }
            }
            else
            {
                ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
                ps.FollowingCow = Entity.Null;
            }

            ILogger.Log($"[SellPointSystem] Sold cow {cowEntity.Id} for {price} coins (rested={rested}, tier={preferredFood})");
            InteractFeedback.Success(ctx, playerEntity, sellPointEntity, StateKeys.Coins);
            return;
        }

        int coinsEarned = InteractionLogic.SellFromGlobal(state, 1);
        if (coinsEarned > 0)
        {
            InteractFeedback.Success(ctx, playerEntity, sellPointEntity, StateKeys.Coins);
            return;
        }
        InteractFeedback.MissingResource(ctx, playerEntity, sellPointEntity, StateKeys.Milk);
    }
}
