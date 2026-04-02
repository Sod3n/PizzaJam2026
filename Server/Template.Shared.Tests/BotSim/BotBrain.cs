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
    private readonly int _breedLevel; // 0=never, 1=half houses, 2=fill houses
    private readonly bool _directionalExpansion;

    private readonly BtNode _root;
    private readonly BtBlackboard _bb = new();

    /// <summary>Set to true when the bot wants an InteractAction dispatched this tick.</summary>
    public bool WantsToInteract { get; private set; }
    /// <summary>The entity the bot is targeting for interaction (used to inject Area2D overlap).</summary>
    public Entity CurrentTarget { get; private set; }
    public Entity Player => _player;
    public Guid UserId => _userId;

    // Per-bot stats: ticks spent on each action
    public readonly Dictionary<string, int> ActionTicks = new();
    public string LastAction = "";       // decision-level action (set only when a new action is chosen)
    private string _lastTrackLabel = ""; // tick-level tracking (for stats)
    public int TotalMilkClicks;
    public int TotalBreedClicks;
    public int TotalGatherClicks;
    public int TotalClicks; // all interaction clicks
    public int TicksClicking; // ticks where bot was in click cooldown (actively working)
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

    public BotBrain(Game game, Entity player, Guid userId, int botIndex, BotCoordinator coordinator, bool selectiveBreeding = false, int breedLevel = 0, bool directionalExpansion = false)
    {
        _game = game;
        _player = player;
        _userId = userId;
        _botIndex = botIndex;
        _coord = coordinator;
        _selectiveBreeding = selectiveBreeding;
        _breedLevel = breedLevel;
        _directionalExpansion = directionalExpansion;
        _root = BuildTree();
    }

    public void PreTick(int tick)
    {
        WantsToInteract = false;
        if (!_game.State.HasComponent<StateComponent>(_player)) return;
        if (!_game.State.HasComponent<PlayerStateComponent>(_player)) return;

        _root.Tick(_bb);

        // Track what the bot is ACTUALLY doing this tick
        string trackLabel = LastAction;
        if (_game.State.HasComponent<StateComponent>(_player))
        {
            var dbgSc = _game.State.GetComponent<StateComponent>(_player);
            if (dbgSc.IsEnabled)
                trackLabel = $"state:{dbgSc.Key}";
            else if (_bb.Get<string>("phase") == "travel")
                trackLabel = "travel";
            else if (_bb.Get<string>("phase") == "idle")
                trackLabel = "IDLE";
        }
        TrackAction(trackLabel);
    }

    // ─── Blackboard helpers ───

    private void SetTarget(BtBlackboard bb, Entity target, Func<bool> repeat = null)
    {
        bb.Set("target", target);
        bb.Set("repeat", repeat);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BEHAVIOUR TREE
    // ═══════════════════════════════════════════════════════════════════

    private BtNode BuildTree() => new RepeatForever(new Selector(
        ActiveStateGuard(),
        UrgentTame(),
        ContinueBreeding(),
        AssignFollowing(),
        QuickLoadBuilder(),
        ScoreBestAndExecute(),
        FallbackGather(),
        IdleCooldown()
    ));

    /// <summary>Safety: if we somehow start a tick already in a game state, handle it.</summary>
    private BtNode ActiveStateGuard() => new Sequence(
        new If(_ => _game.State.GetComponent<StateComponent>(_player).IsEnabled),
        new WaitForStateNode(this)
    );

    /// <summary>URGENT: Tame wild cows when empty houses exist and not following a cow.</summary>
    private BtNode UrgentTame() => new Sequence(
        new If(_ =>
        {
            var ps = _game.State.GetComponent<PlayerStateComponent>(_player);
            return ps.FollowingCow == Entity.Null && FindEmptyUnclaimedHouse() != Entity.Null;
        }),
        new Do(bb =>
        {
            var wildCow = FindWildCow();
            if (wildCow == Entity.Null || !_coord.TryClaim(_botIndex, wildCow))
                return BtStatus.Failure;
            LastAction = "tame_wild";
            SetTarget(bb, wildCow);
            return BtStatus.Success;
        }),
        new ApproachNode(this),
        new InteractLoopNode(this)
    );

    /// <summary>URGENT: Continue breeding pipeline — fetch cows and assign to love house.</summary>
    private BtNode ContinueBreeding() => new Sequence(
        new If(bb =>
        {
            var ps = _game.State.GetComponent<PlayerStateComponent>(_player);
            return ps.FollowingCow != Entity.Null && bb.Get<bool>("breeding");
        }),
        new Do(bb =>
        {
            var lh = FindLoveHouseWithEmptySlot();
            if (lh == Entity.Null)
            {
                bb.Set("breeding", false);
                _coord.ReleaseBreeder(_botIndex);
                return BtStatus.Failure;
            }

            var ps = _game.State.GetComponent<PlayerStateComponent>(_player);
            int emptySlots = CountLoveHouseEmptySlots(lh);
            int followCount = CountFollowingCows(ps.FollowingCow);

            if (followCount < emptySlots)
            {
                // Need more cows — fetch the next one before going to love house
                var nextCow = FindCowForBreeding();
                if (nextCow != Entity.Null)
                {
                    SetTarget(bb, nextCow);
                    return BtStatus.Success;
                }
            }

            // Have enough cows (or can't find more) — go assign to love house
            LastAction = "assign_lh";
            SetTarget(bb, lh);
            return BtStatus.Success;
        }),
        new ApproachNode(this),
        new InteractLoopNode(this)
    );

    /// <summary>URGENT: Assign following cows to regular houses.</summary>
    private BtNode AssignFollowing() => new Sequence(
        new If(_ =>
        {
            var ps = _game.State.GetComponent<PlayerStateComponent>(_player);
            return ps.FollowingCow != Entity.Null;
        }),
        new Do(bb =>
        {
            var ps = _game.State.GetComponent<PlayerStateComponent>(_player);
            var assignTarget = FindBestHouseForCow(ps.FollowingCow);
            if (assignTarget == Entity.Null) return BtStatus.Failure;

            var cow = _game.State.GetComponent<CowComponent>(ps.FollowingCow);
            ref var house = ref _game.State.GetComponent<HouseComponent>(assignTarget);
            house.SelectedFood = cow.PreferredFood;
            _coord.TryClaim(_botIndex, assignTarget);
            LastAction = "assign_house";
            SetTarget(bb, assignTarget);
            return BtStatus.Success;
        }),
        new ApproachNode(this),
        new InteractLoopNode(this)
    );

    /// <summary>QUICK: Load builder if it's nearby and has room.</summary>
    private BtNode QuickLoadBuilder() => new Sequence(
        new Do(bb =>
        {
            var globalRes = GetGlobalResources();
            if (globalRes.Coins < BotConfig.MinCoinsForBuilder) return BtStatus.Failure;

            var builder = FindMyBuilder();
            if (builder == Entity.Null) return BtStatus.Failure;

            var h = _game.State.GetComponent<HelperComponent>(builder);
            if (h.BagCoins >= h.BagCapacity) return BtStatus.Failure;

            if (!_game.State.HasComponent<Transform2D>(builder) || !_game.State.HasComponent<Transform2D>(_player))
                return BtStatus.Failure;

            float dist = (float)Vector2.Distance(
                _game.State.GetComponent<Transform2D>(_player).Position,
                _game.State.GetComponent<Transform2D>(builder).Position);
            if (dist >= BotConfig.BuilderProximity) return BtStatus.Failure;

            SetTarget(bb, builder);
            return BtStatus.Success;
        }),
        new ApproachNode(this),
        new InteractLoopNode(this)
    );

    /// <summary>WORKFLOW: Score all actions and execute the best one.</summary>
    private BtNode ScoreBestAndExecute() => new Sequence(
        new Do(bb => ScoreAndPickBest(bb)),
        new ApproachNode(this),
        new InteractLoopNode(this)
    );

    /// <summary>Fallback: gather food even if we have enough (nothing else to do).</summary>
    private BtNode FallbackGather() => new Sequence(
        new Do(bb =>
        {
            var food = FindNearestFood();
            if (food == Entity.Null || _coord.IsClaimed(food)) return BtStatus.Failure;
            _coord.TryClaim(_botIndex, food);
            LastAction = "gather";
            Entity target = food;
            SetTarget(bb, food, () => _game.State.HasComponent<GrassComponent>(target));
            return BtStatus.Success;
        }),
        new ApproachNode(this),
        new InteractLoopNode(this)
    );

    /// <summary>Nothing to do — idle for a short cooldown.</summary>
    private BtNode IdleCooldown() => new Sequence(
        new Do(bb =>
        {
            LastAction = "IDLE";
            bb.Set("phase", "idle");
            return BtStatus.Success;
        }),
        new Wait(BotConfig.IdleCooldownTicks)
    );

    // ═══════════════════════════════════════════════════════════════════
    //  SCORING
    // ═══════════════════════════════════════════════════════════════════

    private record struct ScoredOption(float Score, Entity Target, bool Repeat, string Action);

    private ScoredOption ScoreOption(Entity target, float value, Vector2 playerPos, int workTicks, bool repeat, string action)
    {
        if (target == Entity.Null) return default;
        int travel = EstimateTravel(playerPos, target);
        float score = value / (travel + workTicks);
        return new ScoredOption(score, target, repeat, action);
    }

    private static ScoredOption Best(ScoredOption a, ScoredOption b) => b.Score > a.Score ? b : a;

    /// <summary>Coin value per unit of milk product by cow tier.</summary>
    private static float TierCoinValue(int preferredFood) => preferredFood switch
    {
        0 => 1f,    // Grass  → Milk
        1 => 5f,    // Carrot → VitaminShake
        2 => 25f,   // Apple  → AppleYogurt
        3 => 100f,  // Mushroom → PurplePotion
        _ => 1f,
    };

    /// <summary>
    /// Breed value = expected milk income of the baby cow, computed from actual parent tiers.
    /// With apple+carrot parents: 15% mushroom(100) + 42% apple(25) + 42% carrot(5) → ~28 coins/milk.
    /// This naturally makes breeding more attractive when the bot has higher-tier cows.
    /// dimFactor (occupancy) brakes breeding as houses fill.
    /// Era boost forces tier-up breeding when cow tier lags behind expansion frontier.
    /// </summary>
    private float EstimateBreedValue()
    {
        int cowCount = Count<CowComponent>();
        int houseCount = Count<HouseComponent>();
        float occupancy = houseCount > 0 ? (float)cowCount / houseCount : 1f;
        float dimFactor = occupancy >= 1f ? 0f : Math.Max(0.1f, 1f - occupancy * 0.8f);

        // Expected milk income from baby (based on actual best parents)
        float expectedMilkIncome = GetExpectedBreedOutputValue();

        // Era-aware boost: breed urgently when cow tier lags behind frontier
        int bestBreedable = GetBestBreedableTier();
        int eraDiff = Math.Max(0, GetFrontierEra() - bestBreedable);
        float eraBoost = eraDiff * 500f;

        return expectedMilkIncome * dimFactor + eraBoost;
    }

    /// <summary>
    /// Expected coin value of a bred baby × ~20 milks per session.
    /// Uses the two highest-tier cows as parents and applies mutation probabilities.
    /// </summary>
    private float GetExpectedBreedOutputValue()
    {
        int best1 = -1, best2 = -1;
        foreach (var e in _game.State.Filter<CowComponent>())
        {
            int tier = _game.State.GetComponent<CowComponent>(e).PreferredFood;
            if (tier > best1) { best2 = best1; best1 = tier; }
            else if (tier > best2) best2 = tier;
        }
        if (best2 < 0) return 0;

        int maxParent = Math.Max(best1, best2);
        int minParent = Math.Min(best1, best2);
        // Mutation: 15% upgrade, 42% parentA, 42% parentB, 1% downgrade
        float expectedCoinPerMilk =
            0.15f * TierCoinValue(Math.Min(maxParent + 1, 3)) +
            0.42f * TierCoinValue(best1) +
            0.42f * TierCoinValue(best2) +
            0.01f * TierCoinValue(Math.Max(minParent - 1, 0));

        return expectedCoinPerMilk * 20f;
    }

    /// <summary>
    /// Highest cow tier where the player has at least 2 cows (can form a breeding pair).
    /// Returns 0 (grass) as fallback even with fewer than 2 grass cows.
    /// </summary>
    private int GetBestBreedableTier()
    {
        int[] tierCount = new int[4]; // 0=grass, 1=carrot, 2=apple, 3=mushroom
        foreach (var e in _game.State.Filter<CowComponent>())
        {
            int tier = _game.State.GetComponent<CowComponent>(e).PreferredFood;
            if (tier >= 0 && tier < 4) tierCount[tier]++;
        }
        for (int t = 3; t >= 0; t--)
        {
            if (tierCount[t] >= 2) return t;
        }
        return 0;
    }

    /// <summary>Current expansion era based on house count as proxy for grid distance.</summary>
    private int GetFrontierEra()
    {
        int houses = Count<HouseComponent>();
        if (houses >= 15) return 3; // mushroom era (dist 6+)
        if (houses >= 9) return 2;  // apple era (dist 4)
        if (houses >= 6) return 1;  // carrot era (dist 3)
        return 0;                    // grass era
    }

    private BtStatus ScoreAndPickBest(BtBlackboard bb)
    {
        // Clear breeding pipeline — reaching scoring means no continue-breeding matched
        bb.Set("breeding", false);
        _coord.ReleaseBreeder(_botIndex);

        var ps = _game.State.GetComponent<PlayerStateComponent>(_player);
        var globalRes = GetGlobalResources();
        int cowCount = Count<CowComponent>();
        int houseCount = Count<HouseComponent>();

        var playerPos = _game.State.HasComponent<Transform2D>(_player)
            ? _game.State.GetComponent<Transform2D>(_player).Position : Vector2.Zero;
        int totalFood = globalRes.Grass + globalRes.Carrot + globalRes.Apple + globalRes.Mushroom;
        int totalMilk = globalRes.Milk + globalRes.VitaminShake + globalRes.AppleYogurt + globalRes.PurplePotion;

        // ── Score each option: value / (travel_ticks + work_ticks) ──
        int assistMult = System.Math.Max(1, ps.ClickMultiplier);

        int foodNeeded = GetFoodNeededForMilking();
        bool needFood = totalFood < foodNeeded;
        var best = new ScoredOption(-1f, Entity.Null, false, "");

        // When we have enough coins to finish 2+ land plots right now, skip milk/sell and just build
        GetRemainingLandCosts(out int remainingLandCount, out int cheapest2Cost);
        bool buildMode = remainingLandCount >= 2
            ? globalRes.Coins >= cheapest2Cost && cheapest2Cost > 0
            : remainingLandCount == 1 && globalRes.Coins >= cheapest2Cost;

        // Option: Gather food — skip in build mode
        if (needFood && !buildMode)
        {
            var food = FindNearestFood();
            if (food != Entity.Null && !_coord.IsClaimed(food))
            {
                int harvest = Math.Min(10, foodNeeded - totalFood);
                best = Best(best, ScoreOption(food, harvest * BotConfig.FoodValueMultiplier, playerPos, harvest, true, "gather"));
            }
        }

        // Option: Milk — skip in build mode
        if (ps.FollowingCow == Entity.Null && totalFood > 0 && !needFood && !buildMode)
        {
            var milkable = FindMilkableUnclaimedHouse(ref globalRes);
            if (milkable != Entity.Null)
            {
                var house = _game.State.GetComponent<HouseComponent>(milkable);
                if (_game.State.HasComponent<CowComponent>(house.CowId))
                {
                    var cow = _game.State.GetComponent<CowComponent>(house.CowId);
                    int remaining = cow.MaxExhaust - cow.Exhaust;
                    int exhaustPerClick = assistMult;
                    int clicks = Math.Min(totalFood, (remaining + exhaustPerClick - 1) / exhaustPerClick); // ceil
                    int totalExhaust = Math.Min(remaining, clicks * exhaustPerClick);
                    int milkPerExhaust = (house.SelectedFood == cow.PreferredFood) ? 5 : 1;
                    int[] _milkCoinVal = { 1, 3, 10, 100 };
                    int coinPerMilk = (house.SelectedFood >= 0 && house.SelectedFood < 4) ? _milkCoinVal[house.SelectedFood] : 1;
                    float milkValue = totalExhaust * milkPerExhaust * coinPerMilk;
                    best = Best(best, ScoreOption(milkable, milkValue * BotConfig.MilkValueMultiplier, playerPos, BotConfig.MilkSetupTicks + clicks, false, "milk"));
                }
            }
        }

        // Option: Sell — skip in build mode
        if (totalMilk > 0 && !buildMode)
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
                int remaining = lc.Threshold - lc.CurrentCoins;
                int canSpend = Math.Min(globalRes.Coins, remaining);
                int buildClicks = (canSpend + assistMult - 1) / assistMult; // ceil(canSpend / assistMult)
                float buildValue = canSpend * BotConfig.BuildValueMultiplier;

                // Game-winning action: if we can complete the FinalStructure, override everything
                if (lc.Type == LandType.FinalStructure && globalRes.Coins >= remaining)
                    buildValue = 100000f;

                best = Best(best, ScoreOption(land, buildValue, playerPos, buildClicks, true, "build"));
            }
        }

        // Option: Breed — simple cap based on breedLevel
        // Level 0: never breed, Level 1: breed up to houseCount/2, Level 2: breed up to houseCount
        int breedCap = _breedLevel == 0 ? 0 : (_breedLevel == 1 ? houseCount / 2 : houseCount);
        bool wantsToBreed = _breedLevel > 0 && cowCount < breedCap;

        bool needCoins = globalRes.Coins <= 0 && (totalMilk > 0 || (totalFood > 0 && cowCount > 0));
        if (ps.FollowingCow == Entity.Null && wantsToBreed && !needCoins && _coord.TryClaimBreeder(_botIndex))
        {
            // breedLevel already gates whether to breed — when it says breed, use high value so it wins
            float breedValue = 5000f;
            var breedable = FindBreedableLoveHouse();
            if (breedable != Entity.Null)
            {
                DebugLog?.Invoke($"  BREED: love house {breedable.Id} is full (2 cows), scoring breed action (value={breedValue:F0})");
                best = Best(best, ScoreOption(breedable, breedValue, playerPos, BotConfig.BreedWorkTicks, false, "breed"));
            }
            else if (houseCount >= cowCount + 2)
            {
                var lh = FindLoveHouseWithEmptySlot();
                if (lh != Entity.Null && cowCount >= 2)
                {
                    var housedCow = FindCowForBreeding();
                    if (housedCow != Entity.Null)
                    {
                        DebugLog?.Invoke($"  BREED_FETCH: fetching cow {housedCow.Id} to love house {lh.Id} (value={breedValue:F0})");
                        best = Best(best, ScoreOption(housedCow, breedValue, playerPos, BotConfig.BreedFetchWorkTicks, false, "breed_fetch"));
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

            if (best.Action != "breed" && best.Action != "breed_fetch")
                _coord.ReleaseBreeder(_botIndex);
        }

        // Option: Tame wild cows
        if (cowCount < houseCount + 2)
        {
            var wildCow = FindWildCow();
            if (wildCow != Entity.Null && _coord.TryClaim(_botIndex, wildCow))
                best = Best(best, ScoreOption(wildCow, BotConfig.TameValue, playerPos, BotConfig.TameWorkTicks, false, "tame"));
        }

        if (best.Target == Entity.Null) return BtStatus.Failure;

        _coord.TryClaim(_botIndex, best.Target);
        LastAction = best.Action;

        // Set up repeat condition based on action type (must check target still exists)
        Entity target = best.Target;
        Func<bool> repeat = best.Repeat ? best.Action switch
        {
            "gather" => () => _game.State.HasComponent<GrassComponent>(target),
            "sell" => () => _game.State.HasComponent<SellPointComponent>(target) && GetGlobalResources().HasAnyMilkProduct(),
            "build" => () => _game.State.HasComponent<LandComponent>(target) && GetGlobalResources().Coins > 0,
            _ => null
        } : null;
        SetTarget(bb, best.Target, repeat);

        if (best.Action == "breed_fetch")
            bb.Set("breeding", true);

        return BtStatus.Success;
    }

    private int EstimateTravel(Vector2 from, Entity target)
    {
        if (!_game.State.HasComponent<Transform2D>(target)) return BotConfig.MinApproachTicks;
        var to = _game.State.GetComponent<Transform2D>(target).Position;
        float dist = (float)Vector2.Distance(from, to);
        return Math.Max(BotConfig.MinApproachTicks, (int)(dist / BotConfig.PlayerSpeed * BotConfig.TickRate));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BT NODE CLASSES
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Travels to the entity stored in bb["target"]. Sets bb["phase"]="travel" during movement.</summary>
    private class ApproachNode : BtNode
    {
        private readonly BotBrain _b;
        private int _remaining = -1;

        public ApproachNode(BotBrain brain) => _b = brain;

        public override BtStatus Tick(BtBlackboard bb)
        {
            if (_remaining < 0)
            {
                var target = bb.Get<Entity>("target");
                if (target == Entity.Null) return BtStatus.Failure;

                _remaining = BotConfig.MinApproachTicks;
                if (_b._game.State.HasComponent<Transform2D>(target) &&
                    _b._game.State.HasComponent<Transform2D>(_b._player))
                {
                    var targetPos = _b._game.State.GetComponent<Transform2D>(target).Position;
                    var playerPos = _b._game.State.GetComponent<Transform2D>(_b._player).Position;
                    float dist = (float)Vector2.Distance(playerPos, targetPos);
                    _remaining = Math.Max(BotConfig.MinApproachTicks,
                        (int)(dist / BotConfig.PlayerSpeed * BotConfig.TickRate));

                    ref var pt = ref _b._game.State.GetComponent<Transform2D>(_b._player);
                    pt.Position = targetPos + new Vector2(1, 0);
                }

                bb.Set("phase", "travel");
            }

            if (--_remaining <= 0)
            {
                _remaining = -1;
                bb.Set("phase", null);
                return BtStatus.Success;
            }
            return BtStatus.Running;
        }

        public override void Reset() => _remaining = -1;
    }

    /// <summary>Triggers interaction with bb["target"], handles game state clicks (milking/breeding),
    /// and optionally repeats using the bb["repeat"] function.</summary>
    private class InteractLoopNode : BtNode
    {
        private readonly BotBrain _b;
        private bool _interacted;
        private int _clickCooldown;

        public InteractLoopNode(BotBrain brain) => _b = brain;

        public override BtStatus Tick(BtBlackboard bb)
        {
            var target = bb.Get<Entity>("target");

            // First tick: trigger the interaction
            if (!_interacted)
            {
                _interacted = true;
                _clickCooldown = 0;
                _b.WantsToInteract = true;
                _b.CurrentTarget = target;
                return BtStatus.Running;
            }

            // Click cooldown — simulate real player click speed
            if (_clickCooldown > 0) { _clickCooldown--; _b.TicksClicking++; return BtStatus.Running; }

            // Handle active game state (milking clicks, breeding clicks, etc.)
            var sc = _b._game.State.GetComponent<StateComponent>(_b._player);
            if (sc.IsEnabled)
            {
                if (sc.Key == StateKeys.Milking && sc.Phase == StatePhase.Active)
                {
                    _b.WantsToInteract = true;
                    _b.TotalMilkClicks++;
                    _b.TotalClicks++;
                    _clickCooldown = BotConfig.ClickCooldownTicks;
                }
                else if (sc.Key == StateKeys.Breed && sc.Phase == StatePhase.Active)
                {
                    _b.WantsToInteract = true;
                    _b.TotalBreedClicks++;
                    _b.TotalClicks++;
                    _clickCooldown = BotConfig.ClickCooldownTicks;
                }
                return BtStatus.Running;
            }

            // State ended or was instant — check repeat condition
            var shouldRepeat = bb.Get<Func<bool>>("repeat");
            if (shouldRepeat != null && shouldRepeat())
            {
                _b.WantsToInteract = true;
                _b.CurrentTarget = target;
                _b.TotalGatherClicks++;
                _b.TotalClicks++;
                _clickCooldown = BotConfig.ClickCooldownTicks;
                return BtStatus.Running;
            }

            return BtStatus.Success;
        }

        public override void Reset() { _interacted = false; _clickCooldown = 0; }
    }

    /// <summary>Waits for the player's active game state to end. Handles milking/breeding clicks.
    /// Used by ActiveStateGuard when the player is already in a state.</summary>
    private class WaitForStateNode : BtNode
    {
        private readonly BotBrain _b;
        private int _clickCooldown;
        public WaitForStateNode(BotBrain brain) => _b = brain;

        public override BtStatus Tick(BtBlackboard bb)
        {
            var sc = _b._game.State.GetComponent<StateComponent>(_b._player);
            if (!sc.IsEnabled) return BtStatus.Success;

            if (_clickCooldown > 0) { _clickCooldown--; return BtStatus.Running; }

            if (sc.Key == StateKeys.Milking && sc.Phase == StatePhase.Active)
            {
                _b.WantsToInteract = true;
                _b.TotalMilkClicks++;
                _clickCooldown = BotConfig.ClickCooldownTicks;
            }
            else if (sc.Key == StateKeys.Breed && sc.Phase == StatePhase.Active)
            {
                _b.WantsToInteract = true;
                _clickCooldown = BotConfig.ClickCooldownTicks;
            }

            return BtStatus.Running;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  QUERY HELPERS (unchanged)
    // ═══════════════════════════════════════════════════════════════════

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

    /// <summary>
    /// Find a cow to take from a house for breeding.
    /// Selective: picks highest tier cow (best pair for upgrade mutation).
    /// Random: picks first available.
    /// </summary>
    private Entity FindCowForBreeding()
    {
        // Find what's already in the love house + love house position for distance check
        int loveHouseTier = -1;
        Vector2 lhPos = Vector2.Zero;
        foreach (var e in _game.State.Filter<LoveHouseComponent>())
        {
            var lh = _game.State.GetComponent<LoveHouseComponent>(e);
            Entity existing = lh.CowId1 != Entity.Null ? lh.CowId1 : lh.CowId2;
            if (existing != Entity.Null && _game.State.HasComponent<CowComponent>(existing))
                loveHouseTier = _game.State.GetComponent<CowComponent>(existing).PreferredFood;
            if (_game.State.HasComponent<Transform2D>(e))
                lhPos = _game.State.GetComponent<Transform2D>(e).Position;
            break;
        }

        Entity best = Entity.Null;
        int bestTier = -1;
        float bestDist = float.MaxValue;

        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            var house = _game.State.GetComponent<HouseComponent>(e);
            if (house.CowId == Entity.Null) continue;
            if (!_game.State.HasComponent<CowComponent>(house.CowId)) continue;
            var cow = _game.State.GetComponent<CowComponent>(house.CowId);
            if (cow.IsMilking || cow.IsDepressed) continue;

            float dist = _game.State.HasComponent<Transform2D>(e)
                ? (float)Vector2.Distance(lhPos, _game.State.GetComponent<Transform2D>(e).Position)
                : float.MaxValue;

            if (!_selectiveBreeding)
            {
                // Random: pick nearest cow to love house
                if (dist < bestDist) { bestDist = dist; best = house.CowId; }
                continue;
            }

            int tier = cow.PreferredFood;

            // Selective: strongly prefer same-pref as love house occupant (avoids breed fail)
            // When no cow in love house yet, prefer highest tier
            if (loveHouseTier >= 0)
            {
                bool isMatch = tier == loveHouseTier;
                bool bestIsMatch = bestTier == loveHouseTier;
                // Same-pref always wins over different-pref
                if (isMatch && !bestIsMatch)
                    { bestTier = tier; bestDist = dist; best = house.CowId; }
                else if (isMatch == bestIsMatch)
                {
                    // Among same category, prefer higher tier then closer
                    if (tier > bestTier || (tier == bestTier && dist < bestDist))
                        { bestTier = tier; bestDist = dist; best = house.CowId; }
                }
            }
            else
            {
                // No cow in love house yet — pick highest tier
                if (tier > bestTier || (tier == bestTier && dist < bestDist))
                    { bestTier = tier; bestDist = dist; best = house.CowId; }
            }
        }
        return best;
    }

    #endregion

    #region House queries

    /// <summary>
    /// Find the best house to assign a cow to.
    /// Selective mode: if the new cow would improve the breeding pair near the love house,
    /// swap it with the worst of the 2 nearest cows. Otherwise use any empty house.
    /// Random/non-selective: any empty house.
    /// </summary>
    private Entity FindBestHouseForCow(Entity cowEntity)
    {
        if (!_selectiveBreeding || !_game.State.HasComponent<CowComponent>(cowEntity))
            return FindEmptyUnclaimedHouse();

        int cowTier = _game.State.GetComponent<CowComponent>(cowEntity).PreferredFood;

        // Find love house position
        Vector2 loveHousePos = Vector2.Zero;
        foreach (var e in _game.State.Filter<LoveHouseComponent>())
        {
            if (_game.State.HasComponent<Transform2D>(e))
                loveHousePos = _game.State.GetComponent<Transform2D>(e).Position;
            break;
        }

        // Find 2 closest occupied houses to love house
        Entity near1 = Entity.Null, near2 = Entity.Null;
        float dist1 = float.MaxValue, dist2 = float.MaxValue;
        int tier1 = -1, tier2 = -1;

        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            if (!_game.State.HasComponent<Transform2D>(e)) continue;
            var house = _game.State.GetComponent<HouseComponent>(e);
            if (house.CowId == Entity.Null) continue;
            if (!_game.State.HasComponent<CowComponent>(house.CowId)) continue;

            float dist = (float)Vector2.Distance(loveHousePos, _game.State.GetComponent<Transform2D>(e).Position);
            var cow = _game.State.GetComponent<CowComponent>(house.CowId);

            if (dist < dist1)
            {
                near2 = near1; dist2 = dist1; tier2 = tier1;
                near1 = e; dist1 = dist; tier1 = cow.PreferredFood;
            }
            else if (dist < dist2)
            {
                near2 = e; dist2 = dist; tier2 = cow.PreferredFood;
            }
        }

        // If new cow is better than the worst of the 2 nearest, swap
        Entity worstNear = Entity.Null;
        int worstTier = int.MaxValue;
        if (near1 != Entity.Null && near2 != Entity.Null)
        {
            if (tier1 <= tier2) { worstNear = near1; worstTier = tier1; }
            else { worstNear = near2; worstTier = tier2; }
        }
        else if (near1 != Entity.Null)
        {
            worstNear = near1; worstTier = tier1;
        }

        if (worstNear != Entity.Null && cowTier > worstTier)
        {
            var emptyHouse = FindEmptyUnclaimedHouse();
            if (emptyHouse != Entity.Null)
            {
                // Move worse cow to the empty house
                var worstHouse = _game.State.GetComponent<HouseComponent>(worstNear);
                Entity worstCow = worstHouse.CowId;

                ref var emptyH = ref _game.State.GetComponent<HouseComponent>(emptyHouse);
                emptyH.CowId = worstCow;
                emptyH.SelectedFood = _game.State.GetComponent<CowComponent>(worstCow).PreferredFood;

                ref var worstC = ref _game.State.GetComponent<CowComponent>(worstCow);
                worstC.HouseId = emptyHouse;

                // Clear the near house for the new cow
                ref var nearH = ref _game.State.GetComponent<HouseComponent>(worstNear);
                nearH.CowId = Entity.Null;

                DebugLog?.Invoke($"  SWAP: moved tier {worstTier} cow to far house, placing tier {cowTier} baby near love house");
                return worstNear;
            }
        }

        // Default: closest empty house to love house
        Entity bestHouse = Entity.Null;
        float bestDist = float.MaxValue;
        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            if (_coord.IsClaimed(e)) continue;
            if (!_game.State.HasComponent<Transform2D>(e)) continue;
            var house = _game.State.GetComponent<HouseComponent>(e);
            if (house.CowId != Entity.Null) continue;

            float dist = (float)Vector2.Distance(loveHousePos, _game.State.GetComponent<Transform2D>(e).Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestHouse = e;
            }
        }

        return bestHouse != Entity.Null ? bestHouse : FindEmptyUnclaimedHouse();
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
        float bestScore = -1f;

        // Milk product coin values by type
        int[] milkCoinValue = { 1, 3, 10, 100 }; // Milk, VitaminShake, AppleYogurt, PurplePotion

        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            if (checkClaimed && _coord.IsClaimed(e)) continue;

            var house = _game.State.GetComponent<HouseComponent>(e);
            if (house.CowId == Entity.Null) continue;
            if (!_game.State.HasComponent<CowComponent>(house.CowId)) continue;

            var cow = _game.State.GetComponent<CowComponent>(house.CowId);
            if (cow.IsMilking || cow.IsDepressed) continue;
            if (cow.Exhaust >= cow.MaxExhaust) continue;

            // Ensure food is available for this cow
            int bestFood = globalRes.FindBestFoodForCow(cow.PreferredFood);
            if (bestFood < 0) continue;

            // Score by total coin value: remaining exhaust × milkPerExhaust × coinValue
            int remaining = cow.MaxExhaust - cow.Exhaust;
            int milkPerExhaust = (bestFood == cow.PreferredFood) ? 5 : 1;
            int coinValue = (bestFood >= 0 && bestFood < milkCoinValue.Length) ? milkCoinValue[bestFood] : 1;
            float score = remaining * milkPerExhaust * coinValue;

            if (score > bestScore)
            {
                bestScore = score;
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

    /// <summary>
    /// Get remaining land: count and cost of the 2 cheapest plots (or all if fewer than 2).
    /// Used to decide if bot should switch to build-first mode.
    /// </summary>
    private void GetRemainingLandCosts(out int count, out int cheapest2Cost)
    {
        count = 0;
        int cost1 = int.MaxValue, cost2 = int.MaxValue; // two cheapest

        foreach (var e in _game.State.Filter<LandComponent>())
        {
            var land = _game.State.GetComponent<LandComponent>(e);
            if (land.Locked != 0) continue;
            int remaining = land.Threshold - land.CurrentCoins;
            if (remaining <= 0) continue;

            count++;
            if (remaining < cost1) { cost2 = cost1; cost1 = remaining; }
            else if (remaining < cost2) { cost2 = remaining; }
        }

        // Cost to finish min(count, 2) cheapest plots
        if (count == 0) cheapest2Cost = 0;
        else if (count == 1) cheapest2Cost = cost1;
        else cheapest2Cost = cost1 + cost2;
    }

    private Entity FindCheapestUnclaimedLand()
    {
        return FindCheapestLandImpl(checkClaimed: true);
    }

    private Entity FindCheapestLandImpl(bool checkClaimed)
    {
        Entity best = Entity.Null;
        int bestScore = int.MaxValue;
        int houseCount = Count<HouseComponent>();

        // Check if FinalStructure is revealed — if not, prioritize path toward (0, -5)
        bool fsRevealed = false;
        foreach (var e in _game.State.Filter<LandComponent>())
        {
            if (_game.State.GetComponent<LandComponent>(e).Type == LandType.FinalStructure)
            { fsRevealed = true; break; }
        }
        if (!fsRevealed)
            foreach (var _ in _game.State.Filter<FinalStructureComponent>())
            { fsRevealed = true; break; }

        foreach (var e in _game.State.Filter<LandComponent>())
        {
            if (checkClaimed && _coord.IsClaimed(e)) continue;
            var land = _game.State.GetComponent<LandComponent>(e);
            if (land.Locked != 0) continue;
            int remaining = land.Threshold - land.CurrentCoins;
            if (remaining <= 0) continue;

            int score = remaining;

            // Quadrant-aware expansion: boost lands in a different quadrant than the last farm.
            // This triggers angular separation faster by deliberately spreading in multiple directions.
            // Also penalize lands that are NOT on the expansion frontier (low grid distance).
            if (_directionalExpansion)
            {
                int gridDist = System.Math.Max(1, System.Math.Abs(land.Arm) + System.Math.Abs(land.Ring));
                var gr = GetGlobalResources();
                int lastFarmQ = StarGrid.GetQuadrant(gr.LastFarmGX, gr.LastFarmGY);
                int landQ = StarGrid.GetQuadrant(land.Arm, land.Ring);
                bool hasFarms = gr.LastFarmGX != 0 || gr.LastFarmGY != 0;

                // Strong boost for lands in a different quadrant than the last farm
                if (hasFarms && landQ != lastFarmQ)
                    score /= 3;

                // Prefer frontier lands (higher distance) — but only mildly
                if (gridDist >= 3)
                    score = score * 2 / (gridDist + 1);
            }

            // Priority: farms once economy is running — higher-tier farms get stronger priority
            // to push the bot toward mushroom caves (highest food value) faster
            if (houseCount >= 3)
            {
                if (land.Type == LandType.MushroomCave)
                    score /= 10;       // PurplePotion = 18 coins — rush this
                else if (land.Type == LandType.AppleOrchard)
                    score /= 6;        // AppleYogurt = 6 coins — on path to mushroom
                else if (land.Type == LandType.CarrotFarm)
                    score /= 4;        // VitaminShake = 2 coins
            }

            // Priority: Assistant building — doubles click output, very valuable
            if (land.Type == LandType.HelperAssistant && houseCount >= 3)
                score /= 5;

            // Priority: path to FinalStructure — strongly prefer plots along (0, -y) axis
            // when FinalStructure hasn't been revealed yet (needs building through (0,-4))
            if (!fsRevealed && land.Arm == 0 && land.Ring < 0 && land.Ring >= -4 && houseCount >= 8)
                score /= 5;

            // Priority: FinalStructure — rush it once revealed
            if (land.Type == LandType.FinalStructure)
                score /= 20;

            // Priority: upgrade buildings — x2 helper boost, but only if helper exists and not yet upgraded
            if (land.Type == LandType.UpgradeGatherer || land.Type == LandType.UpgradeBuilder || land.Type == LandType.UpgradeSeller)
            {
                int upgradeType = land.Type switch
                {
                    LandType.UpgradeGatherer => HelperType.Gatherer,
                    LandType.UpgradeBuilder => HelperType.Builder,
                    LandType.UpgradeSeller => HelperType.Seller,
                    _ => -1
                };
                if (upgradeType >= 0 && HasHelper(upgradeType) && !HasUpgradePetForType(upgradeType))
                    score /= 4;
                else
                    continue; // skip — helper not unlocked yet or already upgraded
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = e;
            }
        }
        return best;
    }

    private bool HasHelper(int helperType)
    {
        foreach (var e in _game.State.Filter<HelperComponent>())
        {
            var h = _game.State.GetComponent<HelperComponent>(e);
            if (h.OwnerPlayer == _player && h.Type == helperType) return true;
        }
        return false;
    }

    private bool HasUpgradePetForType(int helperType)
    {
        foreach (var e in _game.State.Filter<HelperPetComponent>())
        {
            var pet = _game.State.GetComponent<HelperPetComponent>(e);
            if (pet.HelperType == helperType) return true;
        }
        return false;
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
            if (cow.IsMilking || cow.IsDepressed || cow.Exhaust >= cow.MaxExhaust) continue;
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
