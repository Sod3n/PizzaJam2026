using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
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
        // Handle state completions on players
        foreach (var playerEntity in state.Filter<ExitStateComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            var exit = state.GetComponent<ExitStateComponent>(playerEntity);
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

        // Handle Cow Exhaust & Visibility
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);

            if (cow is { Exhaust: > 0, IsMilking: false })
            {
                // Depressed cows stay visible (but non-interactable) — unhide if still hidden from breeding
                if (cow.IsDepressed && state.HasComponent<HiddenComponent>(cowEntity))
                {
                    state.UnhideEntity(cowEntity);
                }

                var gameTime = state.GetCustomData<IGameTime>();
                if (gameTime != null)
                {
                    var random = new DeterministicRandom((uint)(cowEntity.Id ^ gameTime.CurrentTick));
                    if (random.NextInt(0, 750) == 0)
                    {
                        cow.Exhaust--;
                        if (cow.Exhaust <= 0 && cow.IsDepressed)
                        {
                            cow.IsDepressed = false;
                            ILogger.Log($"[CowSystem] Cow {cowEntity.Id} recovered from depression");
                        }
                    }
                }
            }
            else if (!cow.IsMilking)
            {
                // Don't unhide cows that are in a love house during active breeding
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

        if (state.HasComponent<CowComponent>(cow1) && state.HasComponent<CowComponent>(cow2)
            && state.HasComponent<SkinComponent>(cow1) && state.HasComponent<SkinComponent>(cow2))
        {
            // Track same-tier breeding for guaranteed upgrade
            var parentACow = state.GetComponent<CowComponent>(cow1);
            var parentBCow = state.GetComponent<CowComponent>(cow2);
            bool sameTier = parentACow.PreferredFood == parentBCow.PreferredFood;
            bool guaranteedUpgrade = false;

            if (sameTier)
            {
                loveHouse.SameTierBreedCount++;
                if (loveHouse.SameTierBreedCount >= LoveHouseComponent.GuaranteedUpgradeEvery)
                {
                    guaranteedUpgrade = true;
                    loveHouse.SameTierBreedCount = 0;
                    ILogger.Log($"[CowSystem] Guaranteed tier upgrade! (after {LoveHouseComponent.GuaranteedUpgradeEvery} same-tier breeds)");
                }
            }
            else
            {
                loveHouse.SameTierBreedCount = 0;
            }

            var gameTime = state.GetCustomData<IGameTime>();
            uint breedSeed = (uint)((cow1.Id * 7919 + cow2.Id * 104729) ^ (gameTime?.CurrentTick ?? 0));
            var breedRandom = new DeterministicRandom(breedSeed);

            // Different-pref breeding: chance of failure based on tier gap
            bool breedFailed = false;
            if (!sameTier)
            {
                int tierGap = System.Math.Abs(parentACow.PreferredFood - parentBCow.PreferredFood);
                // 1 tier apart = 50%, 2 tiers = 75%, 3 tiers = 90%
                int failChance = tierGap switch
                {
                    1 => 50,
                    2 => 75,
                    _ => 90,
                };
                breedFailed = breedRandom.NextInt(100) < failChance;
            }

            if (breedFailed)
            {
                // Both cows enter depression: exhaust maxed out, hidden in house until recovery
                ILogger.Log($"[CowSystem] Breed FAILED! Cows {cow1.Id} and {cow2.Id} are depressed (tier gap: {System.Math.Abs(parentACow.PreferredFood - parentBCow.PreferredFood)})");
                if (state.HasComponent<CowComponent>(cow1))
                {
                    ref var c1 = ref state.GetComponent<CowComponent>(cow1);
                    c1.Exhaust = c1.MaxExhaust;
                    c1.IsDepressed = true;
                }
                if (state.HasComponent<CowComponent>(cow2))
                {
                    ref var c2 = ref state.GetComponent<CowComponent>(cow2);
                    c2.Exhaust = c2.MaxExhaust;
                    c2.IsDepressed = true;
                }
            }
            else
            {
            // Increment global breed counter
            Entity globalResEntity = Entity.Null;
            foreach (var ge in state.Filter<GlobalResourcesComponent>())
            { globalResEntity = ge; break; }
            int breedCount = 0;
            if (globalResEntity != Entity.Null)
            {
                ref var gr = ref state.GetComponent<GlobalResourcesComponent>(globalResEntity);
                gr.TotalBreedCount++;
                breedCount = gr.TotalBreedCount;
            }

            // Helper unlock: deterministic at breed count threshold
            int neededHelper = GetNextNeededHelper(state, playerEntity);
            bool spawnHelper = false;

            if (neededHelper >= 0 && !guaranteedUpgrade)
            {
                int spawnedCount = state.GetComponent<GlobalResourcesComponent>(globalResEntity).HelpersSpawned;
                if (spawnedCount < HelperUnlockOrder.Length)
                    spawnHelper = breedCount >= HelperUnlockOrder[spawnedCount].threshold;
            }

            if (spawnHelper && GetHelpersEnabled(state))
            {
                {
                    var spawnPos = state.HasComponent<Transform2D>(loveHouseEntity)
                        ? state.GetComponent<Transform2D>(loveHouseEntity).Position + new Vector2(2, 0)
                        : new Vector2(0, 0);
                    var ctx = new Context(state, playerEntity, null!);

                    babyHelper = Definitions.HelperDefinition.Create(ctx, spawnPos, neededHelper, playerEntity);
                    state.AddComponent(babyHelper, new BreedBornComponent());
                    ref var gr2 = ref state.GetComponent<GlobalResourcesComponent>(globalResEntity);
                    gr2.HelpersSpawned++;
                    var gt = state.GetCustomData<IGameTime>();
                    float hMin = gt != null ? gt.CurrentTick / 60f / 60f : -1;
                    ILogger.Log($"[CowSystem] Helper unlocked: {neededHelper switch { 0 => "Assistant", 1 => "Gatherer", 3 => "Builder", 2 => "Seller", _ => "Helper" }} #{gr2.HelpersSpawned} at breed #{breedCount} ({hMin:F1}m)!");

                    // Wire Assistant to player so milking/breeding clicks are doubled
                    if (neededHelper == HelperType.Assistant)
                    {
                        ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
                        ps.AssistantHelper = babyHelper;
                    }
                }
            }
            else
            {
                babyCow = SpawnCrossbredCow(state, playerEntity, cow1, cow2, guaranteedUpgrade, breedCount);

                // Twins: 5% chance for same-pref breeds (no house limit — twin follows player until dismissed)
                if (sameTier && babyCow != Entity.Null)
                {
                    if (breedRandom.NextInt(100) < 1)
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

        var crossbredSkin = twinSkin ?? GameData.GD.SkinsData.CrossbreedSkin(ref random, skinA, skinB);

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

        ref var newCowComp = ref state.GetComponent<CowComponent>(newCow);
        newCowComp.MaxExhaust = totalExhaust;

        // Inherit food preference from parents
        var parentACow = state.GetComponent<CowComponent>(parentA);
        var parentBCow = state.GetComponent<CowComponent>(parentB);

        if (guaranteedUpgrade)
        {
            // Guaranteed upgrade: one tier above the LOWEST parent — rewards same-tier pairing
            int minParent = System.Math.Min(parentACow.PreferredFood, parentBCow.PreferredFood);
            newCowComp.PreferredFood = System.Math.Min(minParent + 1, FoodType.Mushroom);
            ILogger.Log($"[CowSystem] Guaranteed upgrade breed: min({parentACow.PreferredFood},{parentBCow.PreferredFood}) → {newCowComp.PreferredFood}");
        }
        else
        {
            // If breeding succeeded, parents are same-tier or got lucky with diff-tier
            // Same-tier: 75% upgrade / 1% downgrade / 24% inherit
            // Diff-tier (survived fail check): 50% upgrade / 1% downgrade / 49% inherit
            bool sameFood = parentACow.PreferredFood == parentBCow.PreferredFood;
            int upgradeChance = sameFood ? 75 : 50;
            int prefRoll = random.NextInt(100);
            if (prefRoll < upgradeChance)
            {
                int maxParent = System.Math.Max(parentACow.PreferredFood, parentBCow.PreferredFood);
                newCowComp.PreferredFood = System.Math.Min(maxParent + 1, FoodType.Mushroom);
            }
            else if (prefRoll < upgradeChance + 1)
            {
                int minParent = System.Math.Min(parentACow.PreferredFood, parentBCow.PreferredFood);
                newCowComp.PreferredFood = System.Math.Max(minParent - 1, FoodType.Grass);
            }
            else if (prefRoll < upgradeChance + 1 + (100 - upgradeChance - 1) / 2)
                newCowComp.PreferredFood = parentACow.PreferredFood;
            else
                newCowComp.PreferredFood = parentBCow.PreferredFood;
        }

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
        }
        else
        {
            cow.HouseId = Entity.Null;
        }
    }

    private static readonly (int type, int threshold)[] HelperUnlockOrder =
    {
        (HelperType.Gatherer,  GlobalResourcesComponent.GathererUnlockBreed),
        (HelperType.Builder,   GlobalResourcesComponent.BuilderUnlockBreed),
        (HelperType.Seller,    GlobalResourcesComponent.SellerUnlockBreed),
        (HelperType.Milker,    GlobalResourcesComponent.MilkerUnlockBreed),
    };

    /// <summary>
    /// Returns the next helper type and threshold based on sequential spawn index.
    /// Returns -1 if all helpers have been spawned.
    /// </summary>
    private int GetNextNeededHelper(EntityWorld state, Entity playerEntity)
    {
        Entity globalResEntity = Entity.Null;
        foreach (var ge in state.Filter<GlobalResourcesComponent>())
        { globalResEntity = ge; break; }
        if (globalResEntity == Entity.Null) return -1;

        int spawnedCount = state.GetComponent<GlobalResourcesComponent>(globalResEntity).HelpersSpawned;
        if (spawnedCount >= HelperUnlockOrder.Length) return -1;
        return HelperUnlockOrder[spawnedCount].type;
    }
}
