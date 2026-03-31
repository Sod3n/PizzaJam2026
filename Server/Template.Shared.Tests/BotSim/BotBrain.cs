using System;
using System.Collections.Generic;
using System.Linq;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared.Tests;

public class BotBrain
{
    private readonly Game _game;
    private readonly Entity _player;
    private readonly Guid _userId;
    private readonly int _botIndex;
    private readonly BotCoordinator _coord;
    private readonly bool _selectiveBreeding;

    private enum Phase { Deciding, Approaching, InState, Cooldown }
    private Phase _phase = Phase.Deciding;
    private int _phaseTimer;
    private bool _repeatInteract; // keep interacting with same target until done

    /// <summary>Set to true when the bot wants an InteractAction dispatched this tick.</summary>
    public bool WantsToInteract { get; private set; }
    /// <summary>The entity the bot is targeting for interaction (used to inject Area2D overlap).</summary>
    public Entity CurrentTarget { get; private set; }
    public Entity Player => _player;
    public Guid UserId => _userId;

    // Per-bot stats: ticks spent on each action
    public readonly Dictionary<string, int> ActionTicks = new();
    public string LastAction = "";       // decision-level action (set only by MakeDecision)
    private string _lastTrackLabel = ""; // tick-level tracking (for stats)
    public int TotalMilkClicks;
    public Action<string> DebugLog;

    private void TrackAction(string action)
    {
        _lastTrackLabel = action;
        if (!ActionTicks.ContainsKey(action)) ActionTicks[action] = 0;
        ActionTicks[action]++;
    }

    public string ActionStats()
    {
        var sorted = ActionTicks.OrderByDescending(kv => kv.Value);
        int total = ActionTicks.Values.Sum();
        return string.Join("  ", sorted.Select(kv =>
            $"{kv.Key}={kv.Value}({100 * kv.Value / Math.Max(1, total)}%)"));
    }

    public BotBrain(Game game, Entity player, Guid userId, int botIndex, BotCoordinator coordinator, bool selectiveBreeding = false)
    {
        _game = game;
        _player = player;
        _userId = userId;
        _botIndex = botIndex;
        _coord = coordinator;
        _selectiveBreeding = selectiveBreeding;
    }

    public void PreTick(int tick)
    {
        WantsToInteract = false;
        if (!_game.State.HasComponent<StateComponent>(_player)) return;
        if (!_game.State.HasComponent<PlayerStateComponent>(_player)) return;

        switch (_phase)
        {
            case Phase.Approaching:
                _phaseTimer--;
                if (_phaseTimer <= 0)
                {
                    WantsToInteract = true;
                    _phase = Phase.InState;
                }
                break;

            case Phase.InState:
                HandleInState();
                break;

            case Phase.Cooldown:
                _phaseTimer--;
                if (_phaseTimer <= 0)
                    _phase = Phase.Deciding;
                break;

            case Phase.Deciding:
                MakeDecision(tick);
                break;
        }

        // Track what the bot is ACTUALLY doing this tick
        string trackLabel = LastAction;
        if (_game.State.HasComponent<StateComponent>(_player))
        {
            var dbgSc = _game.State.GetComponent<StateComponent>(_player);
            if (dbgSc.IsEnabled)
                trackLabel = $"state:{dbgSc.Key}";
            else if (_phase == Phase.Approaching)
                trackLabel = $"travel";
            else if (_phase == Phase.Cooldown)
                trackLabel = "IDLE";
        }
        TrackAction(trackLabel);
    }

    private void HandleInState()
    {
        var sc = _game.State.GetComponent<StateComponent>(_player);

        if (!sc.IsEnabled)
        {
            // No game state active — check if we should repeat interact on same target
            // This keeps the bot at the target (e.g. sell point, land) until done
            if (_repeatInteract && CurrentTarget != Entity.Null && ShouldKeepRepeating())
            {
                WantsToInteract = true;
                return; // Stay in InState — don't go to Deciding (avoids urgent section interrupting)
            }
            _repeatInteract = false;
            _phase = Phase.Deciding;
            return;
        }

        // During milking Active: click every tick
        if (sc.Key == StateKeys.Milking && sc.Phase == StatePhase.Active)
        {
            WantsToInteract = true;
            TotalMilkClicks++;
        }
        // During breeding Active: click every tick
        else if (sc.Key == StateKeys.Breed && sc.Phase == StatePhase.Active)
        {
            WantsToInteract = true;
        }
        // Enter/Exit phases — just wait for state system to advance
    }

    /// <summary>Check if the current repeat interaction should continue.</summary>
    private bool ShouldKeepRepeating()
    {
        // Gathering food: keep going if food entity still exists (has durability left)
        if (_game.State.HasComponent<GrassComponent>(CurrentTarget))
            return true; // InteractActionService will delete it when durability hits 0

        // Selling: keep going if we still have milk products
        if (_game.State.HasComponent<SellPointComponent>(CurrentTarget))
        {
            var res = GetGlobalResources();
            return res.HasAnyMilkProduct();
        }

        // Building land: keep going if we have coins and land still exists
        if (_game.State.HasComponent<LandComponent>(CurrentTarget))
        {
            var res = GetGlobalResources();
            return res.Coins > 0;
        }

        return false;
    }

    // ─── Scoring ───

    private record struct ScoredOption(float Score, Entity Target, bool Repeat, string Action);

    private ScoredOption ScoreOption(Entity target, float value, Vector2 playerPos, int workTicks, bool repeat, string action)
    {
        if (target == Entity.Null) return default;
        int travel = EstimateTravel(playerPos, target);
        float score = value / (travel + workTicks);
        return new ScoredOption(score, target, repeat, action);
    }

    private static ScoredOption Best(ScoredOption a, ScoredOption b) => b.Score > a.Score ? b : a;

    // ─── Decision making ───

    private void MakeDecision(int tick)
    {
        var sc = _game.State.GetComponent<StateComponent>(_player);
        if (sc.IsEnabled)
        {
            _phase = Phase.InState;
            HandleInState();
            return;
        }

        var ps = _game.State.GetComponent<PlayerStateComponent>(_player);
        var globalRes = GetGlobalResources();
        int cowCount = Count<CowComponent>();
        int houseCount = Count<HouseComponent>();

        // ── URGENT: Tame wild cows when empty houses exist ──
        if (ps.FollowingCow == Entity.Null && FindEmptyUnclaimedHouse() != Entity.Null)
        {
            var wildCow = FindWildCow();
            if (wildCow != Entity.Null && _coord.TryClaim(_botIndex, wildCow))
            {
                LastAction = "tame_wild";
                ApproachAndInteract(wildCow);
                return;
            }
        }

        // ── URGENT: Assign following cows to houses ──
        if (ps.FollowingCow != Entity.Null)
        {
            // When fetching cows for breeding, collect both before going to love house
            if (LastAction == "breed_fetch")
            {
                var lh = FindLoveHouseWithEmptySlot();
                if (lh != Entity.Null)
                {
                    int emptySlots = CountLoveHouseEmptySlots(lh);
                    int followCount = CountFollowingCows(ps.FollowingCow);

                    if (followCount < emptySlots)
                    {
                        // Need more cows — fetch the next one before going to love house
                        var nextCow = FindCowForBreeding();
                        if (nextCow != Entity.Null)
                        {
                            ApproachAndInteract(nextCow);
                            return;
                        }
                    }

                    // Have enough cows (or can't find more) — go assign to love house
                    LastAction = "assign_lh";
                    ApproachAndInteract(lh);
                    return;
                }
            }

            // When clearing parents from love house, tame both before assigning to houses
            if (LastAction == "breed_clear")
            {
                var nextParent = FindParentInCompletedLoveHouse();
                if (nextParent != Entity.Null)
                {
                    // Still a parent in love house — tame it too
                    ApproachAndInteract(nextParent);
                    return;
                }
                // All parents tamed — fall through to assign to houses
            }

            // Assign to regular house
            var assignTarget = FindBestHouseForCow(ps.FollowingCow);
            if (assignTarget != Entity.Null)
            {
                var cow = _game.State.GetComponent<CowComponent>(ps.FollowingCow);
                ref var house = ref _game.State.GetComponent<HouseComponent>(assignTarget);
                house.SelectedFood = cow.PreferredFood;
                _coord.TryClaim(_botIndex, assignTarget);
                LastAction = "assign_house";
                ApproachAndInteract(assignTarget);
                return;
            }
        }

        // ── URGENT: Return parents from love house to milking houses when done breeding ──
        if (ps.FollowingCow == Entity.Null && cowCount >= houseCount)
        {
            var parentInLH = FindParentInCompletedLoveHouse();
            if (parentInLH != Entity.Null)
            {
                LastAction = "breed_clear";
                ApproachAndInteract(parentInLH);
                return;
            }
        }

        // ── WORKFLOW: pick best action based on current resources + travel cost ──
        var playerPos = _game.State.HasComponent<Transform2D>(_player)
            ? _game.State.GetComponent<Transform2D>(_player).Position : Vector2.Zero;
        int totalFood = globalRes.Grass + globalRes.Carrot + globalRes.Apple + globalRes.Mushroom;
        int totalMilk = globalRes.Milk + globalRes.VitaminShake + globalRes.AppleYogurt + globalRes.PurplePotion;

        // ── QUICK: Load builder if it's nearby and has room ──
        {
            var builder = FindMyBuilder();
            if (builder != Entity.Null && globalRes.Coins >= BotConfig.MinCoinsForBuilder)
            {
                var h = _game.State.GetComponent<HelperComponent>(builder);
                if (h.BagCoins < h.BagCapacity)
                {
                    float dist = _game.State.HasComponent<Transform2D>(builder)
                        ? (float)Vector2.Distance(playerPos, _game.State.GetComponent<Transform2D>(builder).Position)
                        : 999f;
                    if (dist < BotConfig.BuilderProximity)
                    {
                        ApproachAndInteract(builder);
                        return;
                    }
                }
            }
        }

        // ── Score each option: value / (travel_ticks + work_ticks) ──
        int foodNeeded = GetFoodNeededForMilking();
        bool needFood = totalFood < foodNeeded;
        var best = new ScoredOption(-1f, Entity.Null, false, "");

        // Option: Gather food
        if (needFood)
        {
            var food = FindNearestFood();
            if (food != Entity.Null && !_coord.IsClaimed(food))
            {
                int harvest = Math.Min(10, foodNeeded - totalFood);
                best = Best(best, ScoreOption(food, harvest * BotConfig.FoodValueMultiplier, playerPos, harvest, true, "gather"));
            }
        }

        // Option: Milk
        if (ps.FollowingCow == Entity.Null && totalFood > 0 && !needFood)
        {
            var milkable = FindMilkableUnclaimedHouse(ref globalRes);
            if (milkable != Entity.Null)
            {
                var house = _game.State.GetComponent<HouseComponent>(milkable);
                if (_game.State.HasComponent<CowComponent>(house.CowId))
                {
                    var cow = _game.State.GetComponent<CowComponent>(house.CowId);
                    int clicks = Math.Min(totalFood, cow.MaxExhaust - cow.Exhaust);
                    int milkPerClick = (house.SelectedFood == cow.PreferredFood) ? 3 : 1;
                    best = Best(best, ScoreOption(milkable, clicks * milkPerClick * BotConfig.MilkValueMultiplier, playerPos, BotConfig.MilkSetupTicks + clicks, false, "milk"));
                }
            }
        }

        // Option: Sell
        if (totalMilk > 0)
        {
            var sellPoint = FindFirst<SellPointComponent>();
            if (sellPoint != Entity.Null)
                best = Best(best, ScoreOption(sellPoint, totalMilk * BotConfig.SellValueMultiplier, playerPos, totalMilk, true, "sell"));
        }

        // Option: Build land
        if (globalRes.Coins >= BotConfig.MinCoinsForBuild)
        {
            var land = FindCheapestUnclaimedLand();
            if (land != Entity.Null)
            {
                var lc = _game.State.GetComponent<LandComponent>(land);
                int canSpend = Math.Min(globalRes.Coins, lc.Threshold - lc.CurrentCoins);
                best = Best(best, ScoreOption(land, canSpend * BotConfig.BuildValueMultiplier, playerPos, canSpend, true, "build"));
            }
        }

        // Option: Breed — re-breed same pair, swap for better cows, or fetch new pair
        // (breed_clear when cowCount >= houseCount is handled as urgent above)
        if (ps.FollowingCow == Entity.Null && cowCount < houseCount && _coord.TryClaimBreeder(_botIndex))
        {
            var breedable = FindBreedableLoveHouse();
            if (breedable != Entity.Null)
            {
                // Love house full — selective mode checks if a better cow exists outside
                if (_selectiveBreeding && ShouldSwapBreedingPair(breedable))
                {
                    var parentInLH = FindParentInCompletedLoveHouse();
                    if (parentInLH != Entity.Null)
                    {
                        DebugLog?.Invoke($"  BREED_CLEAR: swapping for better cow — taming {parentInLH.Id} out");
                        best = Best(best, ScoreOption(parentInLH, BotConfig.BreedValue, playerPos, BotConfig.TameWorkTicks, false, "breed_clear"));
                    }
                }

                if (best.Action != "breed_clear")
                {
                    DebugLog?.Invoke($"  BREED: love house {breedable.Id} is full (2 cows), scoring breed action");
                    best = Best(best, ScoreOption(breedable, BotConfig.BreedValue, playerPos, BotConfig.BreedWorkTicks, false, "breed"));
                }
            }
            else if (houseCount >= cowCount + 2)
            {
                var lh = FindLoveHouseWithEmptySlot();
                if (lh != Entity.Null && cowCount >= 2)
                {
                    var housedCow = FindCowForBreeding();
                    if (housedCow != Entity.Null)
                    {
                        DebugLog?.Invoke($"  BREED_FETCH: fetching cow {housedCow.Id} to love house {lh.Id}");
                        best = Best(best, ScoreOption(housedCow, BotConfig.BreedValue, playerPos, BotConfig.BreedFetchWorkTicks, false, "breed_fetch"));
                    }
                    else
                        DebugLog?.Invoke($"  BREED_FETCH: love house {lh.Id} has slot but FindCowForBreeding() returned null");
                }
                else if (lh == Entity.Null)
                    DebugLog?.Invoke($"  BREED: no love house with empty slot (all full or none exists)");
                else
                    DebugLog?.Invoke($"  BREED: cowCount={cowCount} < 2, can't breed yet");
            }
            else
                DebugLog?.Invoke($"  BREED: houseCount={houseCount} < cowCount+2={cowCount + 2}, not enough houses");

            if (best.Action != "breed" && best.Action != "breed_fetch" && best.Action != "breed_clear")
                _coord.ReleaseBreeder(_botIndex);
        }

        // Option: Tame wild cows
        if (cowCount < houseCount + 2)
        {
            var wildCow = FindWildCow();
            if (wildCow != Entity.Null && _coord.TryClaim(_botIndex, wildCow))
                best = Best(best, ScoreOption(wildCow, BotConfig.TameValue, playerPos, BotConfig.TameWorkTicks, false, "tame"));
        }

        // Fallback: gather food even if we have enough (nothing else to do)
        if (best.Target == Entity.Null)
        {
            var food = FindNearestFood();
            if (food != Entity.Null && !_coord.IsClaimed(food))
                best = new ScoredOption(0, food, true, "gather");
        }

        // Execute best action
        if (best.Target != Entity.Null)
        {
            _coord.TryClaim(_botIndex, best.Target);
            LastAction = best.Action;
            ApproachAndInteract(best.Target, best.Repeat);
            return;
        }

        // Nothing to do
        LastAction = "IDLE";
        _phase = Phase.Cooldown;
        _phaseTimer = BotConfig.IdleCooldownTicks;
    }

    private int EstimateTravel(Vector2 from, Entity target)
    {
        if (!_game.State.HasComponent<Transform2D>(target)) return BotConfig.MinApproachTicks;
        var to = _game.State.GetComponent<Transform2D>(target).Position;
        float dist = (float)Vector2.Distance(from, to);
        return Math.Max(BotConfig.MinApproachTicks, (int)(dist / BotConfig.PlayerSpeed * BotConfig.TickRate));
    }

    private void ApproachAndInteract(Entity target, bool repeat = false)
    {
        CurrentTarget = target;
        _repeatInteract = repeat;
        int travelTicks = BotConfig.MinApproachTicks;
        if (_game.State.HasComponent<Transform2D>(target) && _game.State.HasComponent<Transform2D>(_player))
        {
            var targetPos = _game.State.GetComponent<Transform2D>(target).Position;
            var playerPos = _game.State.GetComponent<Transform2D>(_player).Position;
            float dist = (float)Vector2.Distance(playerPos, targetPos);
            travelTicks = Math.Max(BotConfig.MinApproachTicks, (int)(dist / BotConfig.PlayerSpeed * BotConfig.TickRate));

            ref var pt = ref _game.State.GetComponent<Transform2D>(_player);
            pt.Position = targetPos + new Vector2(1, 0);
        }
        _phase = Phase.Approaching;
        _phaseTimer = travelTicks;
    }

    #region Cow queries

    private int CountFollowingCows(Entity head)
    {
        int count = 0;
        Entity cur = head;
        while (cur != Entity.Null && count < 10) // safety cap
        {
            count++;
            if (!_game.State.HasComponent<CowComponent>(cur)) break;
            var cow = _game.State.GetComponent<CowComponent>(cur);
            cur = Entity.Null;
            // Find next cow in chain (one whose FollowTarget == cur)
            foreach (var e in _game.State.Filter<CowComponent>())
            {
                var c = _game.State.GetComponent<CowComponent>(e);
                if (c.FollowTarget == head) { cur = e; break; }
            }
            head = cur; // advance for next iteration
        }
        return count;
    }

    private int CountLoveHouseEmptySlots(Entity loveHouse)
    {
        var lh = _game.State.GetComponent<LoveHouseComponent>(loveHouse);
        int empty = 0;
        if (lh.CowId1 == Entity.Null) empty++;
        if (lh.CowId2 == Entity.Null) empty++;
        return empty;
    }

    /// <summary>
    /// Check if there's a better cow in a house than the worst parent in the love house.
    /// Only meaningful after a breed completed (BreedProgress >= BreedCost).
    /// </summary>
    private bool ShouldSwapBreedingPair(Entity loveHouseEntity)
    {
        var lh = _game.State.GetComponent<LoveHouseComponent>(loveHouseEntity);
        if (lh.BreedCost <= 0 || lh.BreedProgress < lh.BreedCost) return false; // breed not done yet

        // Find the lowest tier parent in the love house
        int minParentTier = int.MaxValue;
        if (lh.CowId1 != Entity.Null && _game.State.HasComponent<CowComponent>(lh.CowId1))
            minParentTier = Math.Min(minParentTier, _game.State.GetComponent<CowComponent>(lh.CowId1).PreferredFood);
        if (lh.CowId2 != Entity.Null && _game.State.HasComponent<CowComponent>(lh.CowId2))
            minParentTier = Math.Min(minParentTier, _game.State.GetComponent<CowComponent>(lh.CowId2).PreferredFood);
        if (minParentTier == int.MaxValue) return false;

        // Check if any housed cow has a higher tier
        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            var house = _game.State.GetComponent<HouseComponent>(e);
            if (house.CowId == Entity.Null) continue;
            if (!_game.State.HasComponent<CowComponent>(house.CowId)) continue;
            var cow = _game.State.GetComponent<CowComponent>(house.CowId);
            if (cow.IsMilking) continue;
            if (cow.PreferredFood > minParentTier) return true;
        }
        return false;
    }

    private Entity FindWildCow()
    {
        foreach (var e in _game.State.Filter<CowComponent>())
        {
            var cow = _game.State.GetComponent<CowComponent>(e);
            if (cow.FollowingPlayer == Entity.Null && cow.HouseId == Entity.Null)
                return e;
        }
        return Entity.Null;
    }

    /// <summary>Find a parent cow in a love house after breeding completed. Can be tamed out.</summary>
    private Entity FindParentInCompletedLoveHouse()
    {
        foreach (var e in _game.State.Filter<LoveHouseComponent>())
        {
            var lh = _game.State.GetComponent<LoveHouseComponent>(e);
            // Only clear parents after breeding actually completed
            bool breedDone = lh.CowId1 != Entity.Null && lh.CowId2 != Entity.Null
                          && lh.BreedCost > 0 && lh.BreedProgress >= lh.BreedCost;
            if (!breedDone) continue;

            if (_game.State.HasComponent<CowComponent>(lh.CowId1))
            {
                var cow = _game.State.GetComponent<CowComponent>(lh.CowId1);
                if (!cow.IsMilking && cow.FollowingPlayer == Entity.Null) return lh.CowId1;
            }
            if (_game.State.HasComponent<CowComponent>(lh.CowId2))
            {
                var cow = _game.State.GetComponent<CowComponent>(lh.CowId2);
                if (!cow.IsMilking && cow.FollowingPlayer == Entity.Null) return lh.CowId2;
            }
        }
        return Entity.Null;
    }

    /// <summary>
    /// Find a cow to take from a house for breeding.
    /// Selective: picks highest tier cow (best pair for upgrade mutation).
    /// Random: picks first available.
    /// </summary>
    private Entity FindCowForBreeding()
    {
        // Find what's already in the love house
        int loveHouseTier = -1;
        foreach (var e in _game.State.Filter<LoveHouseComponent>())
        {
            var lh = _game.State.GetComponent<LoveHouseComponent>(e);
            Entity existing = lh.CowId1 != Entity.Null ? lh.CowId1 : lh.CowId2;
            if (existing != Entity.Null && _game.State.HasComponent<CowComponent>(existing))
                loveHouseTier = _game.State.GetComponent<CowComponent>(existing).PreferredFood;
            break;
        }

        Entity best = Entity.Null;
        int bestTier = -1;

        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            var house = _game.State.GetComponent<HouseComponent>(e);
            if (house.CowId == Entity.Null) continue;
            if (!_game.State.HasComponent<CowComponent>(house.CowId)) continue;
            var cow = _game.State.GetComponent<CowComponent>(house.CowId);
            if (cow.IsMilking) continue;

            if (!_selectiveBreeding)
                return house.CowId; // random: first available

            int tier = cow.PreferredFood;
            int pairValue = loveHouseTier < 0 ? tier : Math.Max(tier, loveHouseTier);

            if (pairValue > bestTier)
            {
                bestTier = pairValue;
                best = house.CowId;
            }
        }
        return best;
    }

    #endregion

    #region House queries

    /// <summary>
    /// Find the best house to assign a cow to.
    /// Selective mode: high-tier cows go to the closest empty house to the love house,
    /// low-tier cows go to the farthest empty house.
    /// Random/non-selective: any empty house.
    /// </summary>
    private Entity FindBestHouseForCow(Entity cowEntity)
    {
        if (!_selectiveBreeding || !_game.State.HasComponent<CowComponent>(cowEntity))
            return FindEmptyUnclaimedHouse(); // random: any empty

        int cowTier = _game.State.GetComponent<CowComponent>(cowEntity).PreferredFood;

        // Find love house position
        Vector2 loveHousePos = Vector2.Zero;
        foreach (var e in _game.State.Filter<LoveHouseComponent>())
        {
            if (_game.State.HasComponent<Transform2D>(e))
                loveHousePos = _game.State.GetComponent<Transform2D>(e).Position;
            break;
        }

        // High-tier (>= Carrot): closest empty house to love house
        // Low-tier (Grass): farthest empty house from love house
        bool wantClose = cowTier >= FoodType.Carrot;
        Entity best = Entity.Null;
        float bestDist = wantClose ? float.MaxValue : -1f;

        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            if (_coord.IsClaimed(e)) continue;
            if (!_game.State.HasComponent<Transform2D>(e)) continue;
            var house = _game.State.GetComponent<HouseComponent>(e);
            if (house.CowId != Entity.Null) continue;

            float dist = (float)Vector2.Distance(loveHousePos, _game.State.GetComponent<Transform2D>(e).Position);
            if (wantClose ? dist < bestDist : dist > bestDist)
            {
                bestDist = dist;
                best = e;
            }
        }

        return best != Entity.Null ? best : FindEmptyUnclaimedHouse();
    }

    private Entity FindEmptyHouse()
    {
        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            if (_game.State.GetComponent<HouseComponent>(e).CowId == Entity.Null)
                return e;
        }
        return Entity.Null;
    }

    private Entity FindEmptyUnclaimedHouse()
    {
        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            if (_game.State.GetComponent<HouseComponent>(e).CowId == Entity.Null && !_coord.IsClaimed(e))
                return e;
        }
        return Entity.Null;
    }

    private Entity FindLoveHouseWithEmptySlot()
    {
        foreach (var e in _game.State.Filter<LoveHouseComponent>())
        {
            var lh = _game.State.GetComponent<LoveHouseComponent>(e);
            if (lh.CowId1 == Entity.Null || lh.CowId2 == Entity.Null)
                return e;
        }
        return Entity.Null;
    }

    private Entity FindMilkableUnclaimedHouse(ref GlobalResourcesComponent globalRes)
    {
        return FindMilkableHouseImpl(ref globalRes, checkClaimed: true);
    }

    private Entity FindMilkableHouseImpl(ref GlobalResourcesComponent globalRes, bool checkClaimed)
    {
        if (!globalRes.HasAnyFood()) return Entity.Null;

        Entity best = Entity.Null;
        int bestCapacity = 0;

        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            if (checkClaimed && _coord.IsClaimed(e)) continue;

            var house = _game.State.GetComponent<HouseComponent>(e);
            if (house.CowId == Entity.Null) continue;
            if (!_game.State.HasComponent<CowComponent>(house.CowId)) continue;

            var cow = _game.State.GetComponent<CowComponent>(house.CowId);
            if (cow.IsMilking) continue;
            if (cow.Exhaust >= cow.MaxExhaust) continue;

            // Ensure food is available for this cow
            int bestFood = globalRes.FindBestFoodForCow(cow.PreferredFood);
            if (bestFood < 0) continue;

            // Prefer cows with more remaining milk capacity
            int remaining = cow.MaxExhaust - cow.Exhaust;
            if (remaining > bestCapacity)
            {
                bestCapacity = remaining;
                best = e;

                // Set house food to best available for this cow
                ref var h = ref _game.State.GetComponent<HouseComponent>(e);
                h.SelectedFood = bestFood;
            }
        }
        return best;
    }

    private Entity FindBreedableLoveHouse()
    {
        int cows = Count<CowComponent>();
        int houses = Count<HouseComponent>();
        if (cows >= houses) return Entity.Null;

        foreach (var e in _game.State.Filter<LoveHouseComponent>())
        {
            var lh = _game.State.GetComponent<LoveHouseComponent>(e);
            if (lh.CowId1 != Entity.Null && lh.CowId2 != Entity.Null)
                return e;
        }
        return Entity.Null;
    }

    #endregion

    #region Other queries

    private Entity FindMyBuilder()
    {
        foreach (var e in _game.State.Filter<HelperComponent>())
        {
            var h = _game.State.GetComponent<HelperComponent>(e);
            if (h.Type == HelperType.Builder && h.OwnerPlayer == _player)
                return e;
        }
        return Entity.Null;
    }

    private Entity FindFirst<T>() where T : unmanaged, IComponent
    {
        foreach (var e in _game.State.Filter<T>()) return e;
        return Entity.Null;
    }

    private Entity FindNearestFood()
    {
        if (!_game.State.HasComponent<Transform2D>(_player)) return Entity.Null;
        var myPos = _game.State.GetComponent<Transform2D>(_player).Position;
        Entity best = Entity.Null;
        Float bestDist = 999999f;
        foreach (var e in _game.State.Filter<GrassComponent>())
        {
            if (_coord.IsClaimed(e)) continue;
            if (!_game.State.HasComponent<Transform2D>(e)) continue;
            var dist = Vector2.DistanceSquared(myPos, _game.State.GetComponent<Transform2D>(e).Position);
            if (dist < bestDist) { bestDist = dist; best = e; }
        }
        return best;
    }

    private Entity FindCheapestUnclaimedLand()
    {
        return FindCheapestLandImpl(checkClaimed: true);
    }

    private Entity FindCheapestLandImpl(bool checkClaimed)
    {
        Entity best = Entity.Null;
        int bestScore = int.MaxValue;

        foreach (var e in _game.State.Filter<LandComponent>())
        {
            if (checkClaimed && _coord.IsClaimed(e)) continue;
            var land = _game.State.GetComponent<LandComponent>(e);
            if (land.Locked != 0) continue;
            int remaining = land.Threshold - land.CurrentCoins;
            if (remaining <= 0) continue;

            // A real player prioritizes farms once they have a running economy
            int score = remaining;
            bool isFarm = land.Type == LandType.CarrotFarm
                       || land.Type == LandType.AppleOrchard
                       || land.Type == LandType.MushroomCave;
            if (isFarm && Count<HouseComponent>() >= 3)
                score /= 3;

            if (score < bestScore)
            {
                bestScore = score;
                best = e;
            }
        }
        return best;
    }

    /// <summary>How much food to stockpile before starting a milking session.</summary>
    private int GetFoodNeededForMilking()
    {
        // Find the best milkable cow's remaining capacity
        int best = 5; // minimum batch
        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            var house = _game.State.GetComponent<HouseComponent>(e);
            if (house.CowId == Entity.Null) continue;
            if (!_game.State.HasComponent<CowComponent>(house.CowId)) continue;
            var cow = _game.State.GetComponent<CowComponent>(house.CowId);
            if (cow.IsMilking || cow.Exhaust >= cow.MaxExhaust) continue;
            int remaining = cow.MaxExhaust - cow.Exhaust;
            if (remaining > best) best = remaining;
        }
        return best;
    }

    private int Count<T>() where T : unmanaged, IComponent
    {
        int n = 0;
        foreach (var _ in _game.State.Filter<T>()) n++;
        return n;
    }

    public GlobalResourcesComponent GetGlobalResources()
    {
        foreach (var e in _game.State.Filter<GlobalResourcesComponent>())
            return _game.State.GetComponent<GlobalResourcesComponent>(e);
        return default;
    }

    #endregion
}
