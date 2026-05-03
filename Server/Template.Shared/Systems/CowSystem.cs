using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
using Template.Shared.Actions;
using Template.Shared.GameData;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Systems;

public class CowSystem : ISystem
{
    /// <summary>Set HelpersEnabled on the GlobalResourcesComponent in the game world.</summary>
    public static void SetHelpersEnabled(EntityWorld state, bool enabled)
    {
        foreach (var e in state.Filter<GlobalResourcesComponent>())
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(e);
            gr.HelpersEnabled = enabled ? 1 : 0;
            return;
        }
    }

    private static bool GetHelpersEnabled(EntityWorld state)
    {
        foreach (var e in state.Filter<GlobalResourcesComponent>())
            return state.GetComponent<GlobalResourcesComponent>(e).HelpersEnabled != 0;
        return true; // default on
    }
    public void Update(EntityWorld state)
    {
        // Tick down love event timer — when it reaches 0, fire the deferred love event.
        // Skipped entirely when love events are disabled in Balance.
        if (Balance.Love.Enabled)
        {
            foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
            {
                ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                if (gr.LoveEventTimer > 0)
                {
                    gr.LoveEventTimer--;
                    if (gr.LoveEventTimer <= 0)
                    {
                        var targetPlayer = gr.LoveEventCowTarget;
                        var breedCountForLove = gr.LoveEventBreedCount;
                        gr.LoveEventCowTarget = Entity.Null;
                        gr.LoveEventBreedCount = 0;
                        if (targetPlayer != Entity.Null && state.HasComponent<PlayerStateComponent>(targetPlayer))
                        {
                            ILogger.Log($"[CowSystem] Love event timer expired — triggering deferred love event for player {targetPlayer.Id}");
                            TriggerLoveEvent(state, targetPlayer, breedCountForLove);
                        }
                    }
                }
            }
        }

        // Handle state completions on players.
        // Only process Age==0 (the tick Complete() fired). Age>0 means AnimationsSystem
        // already aged it and will remove it next tick — processing again would double-fire.
        foreach (var playerEntity in state.Filter<ExitStateComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            var exit = state.GetComponent<ExitStateComponent>(playerEntity);
            if (exit.Age > 0) continue;
            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            ref var playerState = ref state.GetComponent<PlayerStateComponent>(playerEntity);

            if (exit.Key == StateKeys.Milking)
                HandleMilkingComplete(state, playerEntity, ref sc, ref playerState);
            else if (exit.Key == StateKeys.Taming)
                HandleTamingComplete(state, playerEntity, ref sc, ref playerState);
            else if (exit.Key == StateKeys.Assign)
                HandleAssignComplete(state, playerEntity, ref sc, ref playerState);
            else if (exit.Key == StateKeys.Breed)
                HandleBreedComplete(state, playerEntity, ref sc, ref playerState);
        }

        // Handle active milking states (ensure hidden during Active and Exit phases)
        foreach (var playerEntity in state.Filter<PlayerStateComponent>())
        {
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;

            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            if (!sc.IsEnabled) continue;

            ref var playerState = ref state.GetComponent<PlayerStateComponent>(playerEntity);

            if (sc.Key == StateKeys.Milking && (sc.Phase == StatePhase.Active || sc.Phase == StatePhase.Exit))
            {
                state.HideEntity(playerEntity);
                if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
                    state.HideEntity(playerState.InteractionTarget);
            }
            else if (sc.Key == StateKeys.Breed)
            {
                state.HideEntity(playerEntity);
                // Hide both cows in the love house
                if (state.HasComponent<LoveHouseComponent>(playerState.InteractionTarget))
                {
                    var lh = state.GetComponent<LoveHouseComponent>(playerState.InteractionTarget);
                    if (lh.CowId1 != Entity.Null) state.HideEntity(lh.CowId1);
                    if (lh.CowId2 != Entity.Null) state.HideEntity(lh.CowId2);
                }
            }
        }

        // Handle Cow Exhaust & Depression & Visibility
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);

            // Depression timer: fixed 30-second countdown, independent of exhaust
            if (cow.IsDepressed)
            {
                // Depressed cows stay visible (but non-interactable) — unhide if still hidden from breeding
                if (state.HasComponent<HiddenComponent>(cowEntity))
                {
                    state.UnhideEntity(cowEntity);
                }

                if (cow.DepressionTicksRemaining > 0)
                {
                    cow.DepressionTicksRemaining--;
                }

                if (cow.DepressionTicksRemaining <= 0)
                {
                    cow.IsDepressed = false;
                    cow.DepressionTicksRemaining = 0;
                    ILogger.Log($"[CowSystem] Cow {cowEntity.Id} recovered from depression (30s timer expired)");
                }
            }
            else if (!cow.IsMilking)
            {
                if (state.HasComponent<HiddenComponent>(cowEntity) && !IsCowInActiveBreeding(state, cowEntity))
                {
                    state.UnhideEntity(cowEntity);
                }
            }
        }
    }

    private void ResetState(EntityWorld state, Entity entity, ref StateComponent sc)
    {
        sc.Key = "";
        sc.CurrentTime = 0;
        sc.MaxTime = 0;
        sc.IsEnabled = false;
        state.AddComponent(entity, new EnterStateComponent { Key = StateKeys.Idle, Age = 0 });
    }

    private void HandleMilkingComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Milking complete for player {playerEntity.Id}");

        state.UnhideEntity(playerEntity);

        if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
        {
            ref var cow = ref state.GetComponent<CowComponent>(playerState.InteractionTarget);
            cow.IsMilking = false;
            // Drop any in-progress milk cycle so the next session starts fresh.
            cow.MilkClickCounter = 0;
            state.UnhideEntity(playerState.InteractionTarget);
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);
    }

    private void HandleTamingComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Taming complete for player {playerEntity.Id}");

        var cowEntity = playerState.InteractionTarget;

        if (state.HasComponent<CowComponent>(cowEntity))
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.FollowingPlayer = playerEntity;

            // Save previous house so cow can return after breeding
            // Only save if it's a milking house, not a love house
            var houseId = cow.HouseId;
            if (houseId != Entity.Null && state.HasComponent<HouseComponent>(houseId))
                cow.PreviousHouseId = houseId;

            // Remove from house or love house if it was in one
            if (houseId != Entity.Null && state.HasComponent<HouseComponent>(houseId))
            {
                ref var house = ref state.GetComponent<HouseComponent>(houseId);
                house.CowId = Entity.Null;
                // agent-helpers-in-house: sign disappears when cow leaves (helper presence not affected)
                if (house.HelperId == Entity.Null)
                {
                    var ctxTame = new Context(state, playerEntity, null!);
                    InteractActionService.DespawnSignsForHouse(ctxTame, houseId);
                }
            }
            else if (houseId != Entity.Null && state.HasComponent<LoveHouseComponent>(houseId))
            {
                ref var loveHouse = ref state.GetComponent<LoveHouseComponent>(houseId);
                if (loveHouse.CowId1 == cowEntity) loveHouse.CowId1 = Entity.Null;
                if (loveHouse.CowId2 == cowEntity) loveHouse.CowId2 = Entity.Null;
            }

            // Re-get cow ref after touching house components
            cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.HouseId = Entity.Null;

            // Add to end of follow chain
            if (playerState.FollowingCow == Entity.Null)
            {
                playerState.FollowingCow = cowEntity;
                // Re-get cow ref after touching PlayerStateComponent
                cow = ref state.GetComponent<CowComponent>(cowEntity);
                cow.FollowTarget = playerEntity;
            }
            else
            {
                // FindLastCowInChain iterates CowComponent filter, invalidating our ref
                Entity lastCow = FindLastCowInChain(state, playerState.FollowingCow);
                // Re-get cow ref after chain walk
                ref var cow2 = ref state.GetComponent<CowComponent>(cowEntity);
                cow2.FollowTarget = lastCow;
            }
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);

        ILogger.Log($"[CowSystem] Player {playerEntity.Id} now has cow {cowEntity.Id} in follow chain.");
    }

    private void HandleAssignComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Assign complete for player {playerEntity.Id}");

        var houseEntity = playerState.InteractionTarget;
        var cowEntity = playerState.FollowingCow; // Always assign the first cow in chain

        Entity oldCow = Entity.Null;

        if (state.HasComponent<HouseComponent>(houseEntity) && state.HasComponent<CowComponent>(cowEntity))
        {
            ref var house = ref state.GetComponent<HouseComponent>(houseEntity);

            if (house.CowId != Entity.Null && state.HasComponent<CowComponent>(house.CowId))
            {
                oldCow = house.CowId;
            }

            // Find the second cow in chain before modifying (invalidates refs)
            Entity nextCow = FindNextCowInChain(state, cowEntity);

            // Re-get refs after filter iteration
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            ref var house2 = ref state.GetComponent<HouseComponent>(houseEntity);

            // Remove first cow from chain and assign to house
            cow.FollowingPlayer = Entity.Null;
            cow.FollowTarget = Entity.Null;
            cow.HouseId = houseEntity;

            // Cow will walk to house via CowFollowSystem navigation

            // Re-get house ref and set cow
            house2 = ref state.GetComponent<HouseComponent>(houseEntity);
            house2.CowId = cowEntity;

            // agent-helpers-in-house: spawn food sign reflecting the cow's persistent SelectedFood.
            // House.SelectedFood cache is updated to match. Despawns role sign if any.
            {
                var ctxAssign = new Context(state, playerEntity, null!);
                InteractActionService.EnsureFoodSignForHouse(ctxAssign, houseEntity, cowEntity);
            }

            // Promote next cow in chain to be the new first
            if (nextCow != Entity.Null)
            {
                ref var nextCowComp = ref state.GetComponent<CowComponent>(nextCow);
                nextCowComp.FollowTarget = playerEntity;
                playerState.FollowingCow = nextCow;
            }
            else
            {
                playerState.FollowingCow = Entity.Null;
            }

            // If house had an old cow, add it to end of chain
            if (oldCow != Entity.Null)
            {
                if (playerState.FollowingCow == Entity.Null)
                {
                    ref var oldCowComp = ref state.GetComponent<CowComponent>(oldCow);
                    oldCowComp.FollowingPlayer = playerEntity;
                    oldCowComp.HouseId = Entity.Null;
                    playerState.FollowingCow = oldCow;
                    // Re-get after touching playerState
                    oldCowComp = ref state.GetComponent<CowComponent>(oldCow);
                    oldCowComp.FollowTarget = playerEntity;
                }
                else
                {
                    Entity lastCow = FindLastCowInChain(state, playerState.FollowingCow);
                    // Re-get after chain walk
                    ref var oldCowComp = ref state.GetComponent<CowComponent>(oldCow);
                    oldCowComp.FollowingPlayer = playerEntity;
                    oldCowComp.HouseId = Entity.Null;
                    oldCowComp.FollowTarget = lastCow;
                }
            }
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);

        ILogger.Log($"[CowSystem] Player {playerEntity.Id} assigned cow to house. Old cow following: {oldCow != Entity.Null}");
    }

    private void HandleBreedComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Breed complete for player {playerEntity.Id}");

        var target = playerState.InteractionTarget;

        // Love house breed: target is the love house entity
        if (state.HasComponent<LoveHouseComponent>(target))
        {
            HandleLoveHouseBreedComplete(state, playerEntity, target, ref sc, ref playerState);
            return;
        }

        // Regular crossbreed: target is the other cow
        var targetCow = target;
        var followingCow = playerState.FollowingCow;

        if (state.HasComponent<CowComponent>(targetCow) && state.HasComponent<CowComponent>(followingCow)
            && state.HasComponent<SkinComponent>(targetCow) && state.HasComponent<SkinComponent>(followingCow))
        {
            SpawnCrossbredCow(state, playerEntity, followingCow, targetCow);
            // Re-obtain refs — entity creation may have resized component stores
            playerState = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            sc = ref state.GetComponent<StateComponent>(playerEntity);
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);
    }

    private void HandleLoveHouseBreedComplete(EntityWorld state, Entity playerEntity, Entity loveHouseEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] Love house breed complete for player {playerEntity.Id}");

        state.UnhideEntity(playerEntity);

        ref var loveHouse = ref state.GetComponent<LoveHouseComponent>(loveHouseEntity);
        var cow1 = loveHouse.CowId1;
        var cow2 = loveHouse.CowId2;

        // Unhide cows
        if (cow1 != Entity.Null) state.UnhideEntity(cow1);
        if (cow2 != Entity.Null) state.UnhideEntity(cow2);

        Entity babyCow = Entity.Null;
        Entity babyHelper = Entity.Null;
        int breedCount = 0;

        if (state.HasComponent<CowComponent>(cow1) && state.HasComponent<CowComponent>(cow2)
            && state.HasComponent<SkinComponent>(cow1) && state.HasComponent<SkinComponent>(cow2))
        {
            // Check if this is a love pair for guaranteed upgrade
            var parentACow = state.GetComponent<CowComponent>(cow1);
            var parentBCow = state.GetComponent<CowComponent>(cow2);
            bool sameTier = parentACow.PreferredFood == parentBCow.PreferredFood;
            bool guaranteedUpgrade = false;

            // Love pair: if either cow has the other as LoveTarget, it's a guaranteed upgrade
            if (parentACow.LoveTarget == cow2 || parentBCow.LoveTarget == cow1)
            {
                guaranteedUpgrade = true;
                ILogger.Log($"[CowSystem] Love pair bred! Guaranteed tier upgrade for cows {cow1.Id} and {cow2.Id}");
                // Clear love targets after breeding
                if (state.HasComponent<CowComponent>(cow1))
                {
                    ref var c1Love = ref state.GetComponent<CowComponent>(cow1);
                    c1Love.LoveTarget = Entity.Null;
                }
                if (state.HasComponent<CowComponent>(cow2))
                {
                    ref var c2Love = ref state.GetComponent<CowComponent>(cow2);
                    c2Love.LoveTarget = Entity.Null;
                }
            }

            var gameTime = state.GetCustomData<IGameTime>();
            uint breedSeed = (uint)((cow1.Id * 7919 + cow2.Id * 104729) ^ (gameTime?.CurrentTick ?? 0));
            var breedRandom = new DeterministicRandom(breedSeed);

            // Different-pref breeding: chance of failure based on tier gap
            // Love pairs never fail — the whole point is a guaranteed upgrade
            bool breedFailed = false;
            if (Balance.Cow.DepressionEnabled && !sameTier && !guaranteedUpgrade)
            {
                int tierGap = System.Math.Abs(parentACow.PreferredFood - parentBCow.PreferredFood);
                int failChance = tierGap switch
                {
                    1 => Balance.Breed.FailChanceTier1,
                    2 => Balance.Breed.FailChanceTier2,
                    _ => Balance.Breed.FailChanceTier3Plus,
                };
                breedFailed = breedRandom.NextInt(100) < failChance;
            }

            if (breedFailed)
            {
                // Both cows enter depression: fixed 30s timer, visible but non-interactable until recovery
                ILogger.Log($"[CowSystem] Breed FAILED! Cows {cow1.Id} and {cow2.Id} are depressed (tier gap: {System.Math.Abs(parentACow.PreferredFood - parentBCow.PreferredFood)})");
                const int DepressionDurationTicks = Balance.Cow.DepressionTicks;
                if (state.HasComponent<CowComponent>(cow1))
                {
                    ref var c1 = ref state.GetComponent<CowComponent>(cow1);
                    c1.IsDepressed = true;
                    c1.DepressionTicksRemaining = DepressionDurationTicks;
                }
                if (state.HasComponent<CowComponent>(cow2))
                {
                    ref var c2 = ref state.GetComponent<CowComponent>(cow2);
                    c2.IsDepressed = true;
                    c2.DepressionTicksRemaining = DepressionDurationTicks;
                }
            }
            else
            {
            // Increment global breed counter
            Entity globalResEntity = Entity.Null;
            foreach (var ge in state.Filter<GlobalResourcesComponent>())
            { globalResEntity = ge; break; }
            if (globalResEntity != Entity.Null)
            {
                ref var gr = ref state.GetComponent<GlobalResourcesComponent>(globalResEntity);
                gr.TotalBreedCount++;
                breedCount = gr.TotalBreedCount;
                ILogger.Log($"[CowSystem] Breed #{breedCount} succeeded. NextLoveBreedCount={gr.NextLoveBreedCount}");

                // Love system: initialize threshold on first breed. Skipped when disabled.
                if (Balance.Love.Enabled && gr.NextLoveBreedCount == 0)
                {
                    var loveSeed = new DeterministicRandom((uint)(breedCount ^ 0xBEEF));
                    gr.NextLoveBreedCount = breedCount + loveSeed.NextInt(Balance.Love.NextEventBreedsMin, Balance.Love.NextEventBreedsMax);
                    ILogger.Log($"[CowSystem] Love threshold initialized to {gr.NextLoveBreedCount}");
                }
            }

            // Helper unlock: deterministic at breed count threshold.
            // HelpersSpawned is pre-bumped in AddPlayerAction for each helper-player that joins,
            // so the first N breed-unlocks (one per helper-player) are silently skipped here.
            int neededHelper = GetNextNeededHelper(state, playerEntity);
            bool spawnHelper = false;

            if (neededHelper >= 0 && !guaranteedUpgrade)
            {
                int spawnedCount = state.GetComponent<GlobalResourcesComponent>(globalResEntity).HelpersSpawned;
                if (spawnedCount < Balance.Helper.HelperUnlockBreeds.Length)
                    spawnHelper = breedCount >= Balance.Helper.HelperUnlockBreeds[spawnedCount];
            }

            if (spawnHelper && GetHelpersEnabled(state))
            {
                {
                    var spawnPos = state.HasComponent<Transform2D>(loveHouseEntity)
                        ? state.GetComponent<Transform2D>(loveHouseEntity).Position + new Vector2(2, 0)
                        : new Vector2(0, 0);
                    var ctx = new Context(state, playerEntity, null!);

                    babyHelper = Definitions.HelperDefinition.Create(ctx, spawnPos, neededHelper, playerEntity);
                    ref var helperComp = ref state.GetComponent<HelperComponent>(babyHelper);
                    helperComp.ParentA = cow1;
                    helperComp.ParentB = cow2;
                    state.AddComponent(babyHelper, new BreedBornComponent());
                    ref var gr2 = ref state.GetComponent<GlobalResourcesComponent>(globalResEntity);
                    gr2.HelpersSpawned++;
                    var gt = state.GetCustomData<IGameTime>();
                    float hMin = gt != null ? gt.CurrentTick / 60f / 60f : -1;
                    ILogger.Log($"[CowSystem] Helper unlocked: #{gr2.HelpersSpawned} at breed #{breedCount} ({hMin:F1}m)!");
                }
            }
            else
            {
                ILogger.Log($"[CowSystem] PRE-SPAWN NextEntityId={state.NextEntityId} tick={gameTime?.CurrentTick}");
                babyCow = SpawnCrossbredCow(state, playerEntity, cow1, cow2, guaranteedUpgrade, breedCount);
                ILogger.Log($"[CowSystem] POST-SPAWN babyCow={babyCow.Id} NextEntityId={state.NextEntityId}");

                // Twins on same-pref breeds: cow follows player until dismissed (no house limit)
                if (sameTier && babyCow != Entity.Null)
                {
                    if (breedRandom.NextInt(100) < Balance.Cow.TwinChancePercent)
                    {
                        var babySkin = state.GetComponent<SkinComponent>(babyCow);
                        var twinCow = SpawnCrossbredCow(state, playerEntity, cow1, cow2, false, 0, twinSkin: babySkin);
                        if (twinCow != Entity.Null)
                        {
                            ref var twinComp = ref state.GetComponent<CowComponent>(twinCow);
                            twinComp.FollowingPlayer = playerEntity;
                            twinComp.FollowTarget = babyCow;
                            ILogger.Log($"[CowSystem] TWINS! Second calf {twinCow.Id} born from same-pref breed");
                        }
                    }
                }
            }
            } // end !breedFailed
        }

        // Return parents to their original houses
        if (cow1 != Entity.Null)
        {
            ref var lh2 = ref state.GetComponent<LoveHouseComponent>(loveHouseEntity);
            if (lh2.CowId1 == cow1) lh2.CowId1 = Entity.Null;
            if (lh2.CowId2 == cow1) lh2.CowId2 = Entity.Null;
            ReturnCowToHouse(state, cow1);
        }
        if (cow2 != Entity.Null)
        {
            ref var lh3 = ref state.GetComponent<LoveHouseComponent>(loveHouseEntity);
            if (lh3.CowId1 == cow2) lh3.CowId1 = Entity.Null;
            if (lh3.CowId2 == cow2) lh3.CowId2 = Entity.Null;
            ReturnCowToHouse(state, cow2);
        }
        loveHouse = ref state.GetComponent<LoveHouseComponent>(loveHouseEntity);
        // Mark on cooldown via the unified CooldownComponent (Unit=Days, cleared by SleepLogic.AdvanceDay).
        // Binary semantics: any non-zero TicksRemaining means "on cooldown" — actual ticks don't decay
        // per-tick because the unit is days.
        if (state.HasComponent<CooldownComponent>(loveHouseEntity))
        {
            ref var cd = ref state.GetComponent<CooldownComponent>(loveHouseEntity);
            if (cd.MaxTicks <= 0) cd.MaxTicks = 1;
            cd.TicksRemaining = cd.MaxTicks;
            cd.Unit = CooldownUnit.Days;
        }
        else
        {
            state.AddComponent(loveHouseEntity, new CooldownComponent
            {
                MaxTicks = 1,
                TicksRemaining = 1,
                Unit = CooldownUnit.Days,
            });
        }

        // Love system: after every 2 breeds, set a random timer before the love event fires
        // (instead of triggering immediately)
        if (breedCount > 0)
        {
            Entity grEntity = Entity.Null;
            foreach (var ge in state.Filter<GlobalResourcesComponent>())
            { grEntity = ge; break; }
            if (grEntity != Entity.Null)
            {
                ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                if (Balance.Love.Enabled && breedCount >= gr.NextLoveBreedCount && gr.LoveEventTimer <= 0)
                {
                    var timerSeed = new DeterministicRandom((uint)(breedCount * 31337));
                    gr.LoveEventTimer = timerSeed.NextInt(Balance.Love.EventDelayTicksMin, Balance.Love.EventDelayTicksMax);
                    if (gr.LoveEventTimer == 0) gr.LoveEventTimer = 1; // Ensure at least 1 tick delay
                    gr.LoveEventCowTarget = playerEntity;
                    gr.LoveEventBreedCount = breedCount;
                    ILogger.Log($"[CowSystem] Love event threshold reached: breedCount={breedCount} >= NextLoveBreedCount={gr.NextLoveBreedCount}. Timer set to {gr.LoveEventTimer} ticks ({gr.LoveEventTimer / 60f:F1}s)");

                    var nextSeed = new DeterministicRandom((uint)(breedCount * 31337 + 7));
                    gr.NextLoveBreedCount = breedCount + nextSeed.NextInt(Balance.Love.NextEventBreedsMin, Balance.Love.NextEventBreedsMax);
                    ILogger.Log($"[CowSystem] Next love threshold set to {gr.NextLoveBreedCount}");
                }
            }
        }

        // Re-obtain refs — entity creation (SpawnCrossbredCow, helper spawn) may have
        // resized component stores, invalidating any ref obtained before the spawn.
        playerState = ref state.GetComponent<PlayerStateComponent>(playerEntity);
        sc = ref state.GetComponent<StateComponent>(playerEntity);

        // Baby cow follows the player
        if (babyCow != Entity.Null && state.HasComponent<CowComponent>(babyCow))
        {
            ref var baby = ref state.GetComponent<CowComponent>(babyCow);
            baby.FollowingPlayer = playerEntity;

            if (playerState.FollowingCow == Entity.Null)
            {
                playerState.FollowingCow = babyCow;
                baby = ref state.GetComponent<CowComponent>(babyCow);
                baby.FollowTarget = playerEntity;
            }
            else
            {
                Entity lastCow = FindLastCowInChain(state, playerState.FollowingCow);
                baby = ref state.GetComponent<CowComponent>(babyCow);
                baby.FollowTarget = lastCow;
            }
        }

        playerState.InteractionTarget = Entity.Null;
        ResetState(state, playerEntity, ref sc);

        ILogger.Log($"[CowSystem] Love house breed complete. Released cows {cow1.Id} and {cow2.Id} back to player {playerEntity.Id}");
    }

    /// <summary>
    /// Trigger a love event: pick a random cow to fall in love with the highest-tier cow.
    /// The "lover" cow starts following the player and has its LoveTarget set.
    /// </summary>
    private static void TriggerLoveEvent(EntityWorld state, Entity playerEntity, int breedCount)
    {
        // Debug: count cows in various states for diagnostics
        int cowsInHouses = 0, cowsFollowing = 0, cowsDepressed = 0, cowsTotal = 0;
        foreach (var ce in state.Filter<CowComponent>())
        {
            cowsTotal++;
            var c = state.GetComponent<CowComponent>(ce);
            if (c.HouseId != Entity.Null && state.HasComponent<HouseComponent>(c.HouseId)) cowsInHouses++;
            if (c.FollowingPlayer != Entity.Null) cowsFollowing++;
            if (c.IsDepressed) cowsDepressed++;
        }
        ILogger.Log($"[CowSystem] TriggerLoveEvent: total={cowsTotal} inHouses={cowsInHouses} following={cowsFollowing} depressed={cowsDepressed}");

        // Find the highest preferred food cow that is in a house (the "target")
        Entity bestTarget = Entity.Null;
        int bestFood = -1;
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            var cow = state.GetComponent<CowComponent>(cowEntity);
            // Must be in a regular house, not following anyone, not depressed, not already a love target
            if (cow.HouseId == Entity.Null) continue;
            if (!state.HasComponent<HouseComponent>(cow.HouseId)) continue;
            if (cow.FollowingPlayer != Entity.Null) continue;
            if (cow.IsDepressed) continue;
            if (cow.LoveTarget != Entity.Null) continue;

            if (cow.PreferredFood > bestFood)
            {
                bestFood = cow.PreferredFood;
                bestTarget = cowEntity;
            }
        }

        if (bestTarget == Entity.Null)
        {
            ILogger.Log($"[CowSystem] Love event skipped: no valid target cow found");
            return;
        }

        // Pick a random different cow to be the "lover" — must also be in a house
        var loveSeed = new DeterministicRandom((uint)(breedCount * 7 + bestTarget.Id));
        Entity lover = Entity.Null;
        int candidateCount = 0;

        // Count eligible candidates first
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            if (cowEntity == bestTarget) continue;
            var cow = state.GetComponent<CowComponent>(cowEntity);
            if (cow.HouseId == Entity.Null) continue;
            if (!state.HasComponent<HouseComponent>(cow.HouseId)) continue;
            if (cow.FollowingPlayer != Entity.Null) continue;
            if (cow.IsDepressed) continue;
            if (cow.LoveTarget != Entity.Null) continue;
            candidateCount++;
        }

        if (candidateCount == 0)
        {
            ILogger.Log($"[CowSystem] Love event skipped: no eligible lover cow found");
            return;
        }

        int chosen = loveSeed.NextInt(candidateCount);
        int idx = 0;
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            if (cowEntity == bestTarget) continue;
            var cow = state.GetComponent<CowComponent>(cowEntity);
            if (cow.HouseId == Entity.Null) continue;
            if (!state.HasComponent<HouseComponent>(cow.HouseId)) continue;
            if (cow.FollowingPlayer != Entity.Null) continue;
            if (cow.IsDepressed) continue;
            if (cow.LoveTarget != Entity.Null) continue;
            if (idx == chosen) { lover = cowEntity; break; }
            idx++;
        }

        if (lover == Entity.Null) return;

        // Set the love target on the lover cow
        ref var loverCow = ref state.GetComponent<CowComponent>(lover);
        loverCow.LoveTarget = bestTarget;

        // Remove lover from its house
        var loverHouseId = loverCow.HouseId;
        if (loverHouseId != Entity.Null && state.HasComponent<HouseComponent>(loverHouseId))
        {
            ref var house = ref state.GetComponent<HouseComponent>(loverHouseId);
            if (house.CowId == lover)
                house.CowId = Entity.Null;
            // agent-helpers-in-house: despawn sign when cow leaves
            if (house.HelperId == Entity.Null)
            {
                var ctxLove = new Context(state, playerEntity, null!);
                InteractActionService.DespawnSignsForHouse(ctxLove, loverHouseId);
            }
        }

        // Make the lover follow the player
        loverCow = ref state.GetComponent<CowComponent>(lover);
        loverCow.PreviousHouseId = loverCow.HouseId;
        loverCow.HouseId = Entity.Null;
        loverCow.FollowingPlayer = playerEntity;

        // Add to end of follow chain
        ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
        if (ps.FollowingCow == Entity.Null)
        {
            ps.FollowingCow = lover;
            loverCow = ref state.GetComponent<CowComponent>(lover);
            loverCow.FollowTarget = playerEntity;
        }
        else
        {
            Entity lastCow = Entity.Null;
            var current = ps.FollowingCow;
            int safety = 0;
            while (safety < 100)
            {
                Entity next = Entity.Null;
                foreach (var ce in state.Filter<CowComponent>())
                {
                    ref var c = ref state.GetComponent<CowComponent>(ce);
                    if (c.FollowTarget == current && c.FollowingPlayer != Entity.Null)
                    { next = ce; break; }
                }
                if (next == Entity.Null) { lastCow = current; break; }
                current = next;
                safety++;
            }
            if (lastCow == Entity.Null) lastCow = ps.FollowingCow;
            loverCow = ref state.GetComponent<CowComponent>(lover);
            loverCow.FollowTarget = lastCow;
        }

        // Do NOT show popup immediately — the cow follows the player with a need icon.
        // The popup only triggers when the player interacts with the love cow.
        string loverName = state.HasComponent<NameComponent>(lover) ? state.GetComponent<NameComponent>(lover).Name.ToString() : $"Cow #{lover.Id}";
        string targetName = state.HasComponent<NameComponent>(bestTarget) ? state.GetComponent<NameComponent>(bestTarget).Name.ToString() : $"Cow #{bestTarget.Id}";
        ILogger.Log($"[CowSystem] Love event! {loverName} (cow {lover.Id}) fell in love with {targetName} (cow {bestTarget.Id}) — waiting for player interaction");
    }

    private Entity SpawnCrossbredCow(EntityWorld state, Entity playerEntity, Entity parentA, Entity parentB, bool guaranteedUpgrade = false, int breedCount = 0, SkinComponent? twinSkin = null)
    {
        var skinA = state.GetComponent<SkinComponent>(parentA);
        var skinB = state.GetComponent<SkinComponent>(parentB);

        var spawnPos = state.HasComponent<Deterministic.GameFramework.TwoD.Transform2D>(parentB)
            ? state.GetComponent<Deterministic.GameFramework.TwoD.Transform2D>(parentB).Position + new Vector2(2, 0)
            : new Vector2(0, 0);

        var context = new Context(state, playerEntity, null!);
        var newCow = Definitions.CowDefinition.Create(context, spawnPos);

        var gameTime = state.GetCustomData<IGameTime>();
        uint seed = (uint)(newCow.Id ^ (gameTime?.CurrentTick ?? 0));
        var random = new DeterministicRandom(seed);

        ref var spawnCounts = ref GetSpawnCounts(state);
        var crossbredSkin = twinSkin ?? GameData.GD.SkinsData.CrossbreedSkin(ref random, skinA, skinB, ref spawnCounts);

        // Guaranteed max Megaaaabooba at breed #30
        if (breedCount == GlobalResourcesComponent.GuaranteedMegaBreed)
        {
            var topKey = new FixedString32("Top");
            int megaId = GameData.GD.SkinsData.GetRandomMaxMegaId(ref random);
            if (crossbredSkin.Skins.ContainsKey(topKey))
                crossbredSkin.Skins[topKey] = megaId;
            else
                crossbredSkin.Skins.Add(topKey, megaId);
            ILogger.Log($"[CowSystem] Guaranteed max Megaaaabooba drop at breed #{breedCount}!");
        }

        state.AddComponent(newCow, crossbredSkin);

        int totalExhaust = 0;
        foreach (var skinId in crossbredSkin.Skins.Values)
        {
            var skinDef = GameData.GD.SkinsData.Get(skinId);
            if (skinDef != null)
                totalExhaust += skinDef.Exhaust;
        }
        if (totalExhaust <= 0) totalExhaust = 10;
        // Round up to nearest multiple of 4 so milking always completes cleanly
        totalExhaust = ((totalExhaust + 3) / 4) * 4;

        ref var newCowComp = ref state.GetComponent<CowComponent>(newCow);
        newCowComp.MaxExhaust = totalExhaust;
        newCowComp.ParentA = parentA;
        newCowComp.ParentB = parentB;

        // Food preference: random with parent inheritance.
        //   25% inherit parent A's primary preference
        //   25% inherit parent B's primary preference
        //   50% random (weighted: grass-heavy via FoodType.RandomPreferred)
        var parentACow = state.GetComponent<CowComponent>(parentA);
        var parentBCow = state.GetComponent<CowComponent>(parentB);
        int inheritChance = Balance.Cow.BreedInheritParentChancePercent;
        int prefRoll = random.NextInt(100);
        if (prefRoll < inheritChance)
            newCowComp.PreferredFood = parentACow.PreferredFood;
        else if (prefRoll < inheritChance * 2)
            newCowComp.PreferredFood = parentBCow.PreferredFood;
        else
            newCowComp.PreferredFood = FoodType.RandomPreferred(ref random);

        // Optional secondary preference (any food, not the primary).
        if (random.NextInt(100) < Balance.Cow.SecondaryPreferenceChancePercent)
        {
            int second;
            int safety = 0;
            do { second = random.NextInt(0, 4); safety++; }
            while (second == newCowComp.PreferredFood && safety < 8);
            newCowComp.SecondaryPreferredFood = second == newCowComp.PreferredFood ? -1 : second;
        }
        else
        {
            newCowComp.SecondaryPreferredFood = -1;
        }
        newCowComp.DiscoveredFoodMask = 0;

        state.AddComponent(newCow, new BreedBornComponent());
        ILogger.Log($"[CowSystem] Bred new cow {newCow.Id} with MaxExhaust: {totalExhaust}, PreferredFood: {newCowComp.PreferredFood}");
        return newCow;
    }

    /// <summary>Find the last cow in the follow chain starting from firstCow.</summary>
    private Entity FindLastCowInChain(EntityWorld state, Entity firstCow)
    {
        var current = firstCow;
        int safety = 0;
        while (safety < 100)
        {
            // Find if any cow has FollowTarget == current
            Entity next = Entity.Null;
            foreach (var cowEntity in state.Filter<CowComponent>())
            {
                ref var c = ref state.GetComponent<CowComponent>(cowEntity);
                if (c.FollowTarget == current && c.FollowingPlayer != Entity.Null)
                {
                    next = cowEntity;
                    break;
                }
            }
            if (next == Entity.Null) return current;
            current = next;
            safety++;
        }
        return current;
    }

    /// <summary>Find the cow that follows the given cow (next in chain).</summary>
    private Entity FindNextCowInChain(EntityWorld state, Entity cow)
    {
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var c = ref state.GetComponent<CowComponent>(cowEntity);
            if (c.FollowTarget == cow && c.FollowingPlayer != Entity.Null)
            {
                return cowEntity;
            }
        }
        return Entity.Null;
    }

    /// <summary>Check if a cow is in a love house that's currently being bred.</summary>
    private bool IsCowInActiveBreeding(EntityWorld state, Entity cowEntity)
    {
        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
        var houseId = cow.HouseId;
        if (houseId == Entity.Null || !state.HasComponent<LoveHouseComponent>(houseId)) return false;

        // Check if any player is in breed state targeting this love house
        foreach (var playerEntity in state.Filter<PlayerStateComponent>())
        {
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;
            var sc = state.GetComponent<StateComponent>(playerEntity);
            if (!sc.IsEnabled || sc.Key != StateKeys.Breed) continue;
            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
            if (ps.InteractionTarget == houseId) return true;
        }
        return false;
    }

    /// <summary>Return a cow to its previous house after breeding. If no previous house, assign to any empty house.</summary>
    private void ReturnCowToHouse(EntityWorld state, Entity cowEntity)
    {
        if (!state.HasComponent<CowComponent>(cowEntity)) return;

        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
        var prevHouse = cow.PreviousHouseId;
        cow.FollowingPlayer = Entity.Null;
        cow.FollowTarget = Entity.Null;
        cow.PreviousHouseId = Entity.Null;

        // Try previous house first, then any empty house
        Entity targetHouse = Entity.Null;
        if (prevHouse != Entity.Null && state.HasComponent<HouseComponent>(prevHouse)
            && state.GetComponent<HouseComponent>(prevHouse).CowId == Entity.Null)
        {
            targetHouse = prevHouse;
        }
        else
        {
            foreach (var e in state.Filter<HouseComponent>())
            {
                if (state.GetComponent<HouseComponent>(e).CowId == Entity.Null)
                { targetHouse = e; break; }
            }
        }

        if (targetHouse != Entity.Null)
        {
            ref var house = ref state.GetComponent<HouseComponent>(targetHouse);
            house.CowId = cowEntity;
            cow = ref state.GetComponent<CowComponent>(cowEntity);
            cow.HouseId = targetHouse;
            // agent-helpers-in-house: spawn food sign reflecting cow's persistent SelectedFood.
            var ctxRet = new Context(state, Entity.Null, null!);
            InteractActionService.EnsureFoodSignForHouse(ctxRet, targetHouse, cowEntity);
        }
        else
        {
            cow.HouseId = Entity.Null;
        }
    }

    /// <summary>
    /// Default initial role for a freshly-spawned helper. The player can cycle to any
    /// other role via the role sign, so this is just the starting state.
    /// </summary>
    private const int DefaultHelperRole = HelperType.Gatherer;

    /// <summary>
    /// Returns the default helper role to spawn next, or -1 if the unlock ladder is exhausted.
    /// </summary>
    private int GetNextNeededHelper(EntityWorld state, Entity playerEntity)
    {
        Entity globalResEntity = Entity.Null;
        foreach (var ge in state.Filter<GlobalResourcesComponent>())
        { globalResEntity = ge; break; }
        if (globalResEntity == Entity.Null) return -1;

        int spawnedCount = state.GetComponent<GlobalResourcesComponent>(globalResEntity).HelpersSpawned;
        if (spawnedCount >= Balance.Helper.HelperUnlockBreeds.Length) return -1;
        return DefaultHelperRole;
    }

    private static ref SkinSpawnCountsComponent GetSpawnCounts(EntityWorld state)
    {
        foreach (var e in state.Filter<SkinSpawnCountsComponent>())
            return ref state.GetComponent<SkinSpawnCountsComponent>(e);
        throw new System.InvalidOperationException("SkinSpawnCountsComponent entity not found");
    }
}
