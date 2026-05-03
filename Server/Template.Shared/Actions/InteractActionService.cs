using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Actions;

public class InteractActionService : ActionService<InteractAction, PlayerEntity>
{
    public const int NotEnoughResourceDurationTicks = 2;

    protected override void ExecuteProcess(InteractAction action, ref PlayerEntity playerComp, Context ctx)
    {
        Entity playerEntity = ctx.Entity;

        if (playerComp.UserId != action.UserId) return;
        if (!ctx.State.HasComponent<PlayerStateComponent>(playerEntity)) return;
        if (!ctx.State.HasComponent<StateComponent>(playerEntity)) return;

        ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
        ref var sc = ref ctx.State.GetComponent<StateComponent>(playerEntity);

        // If actively milking, handle milk clicks
        if (sc.Key == StateKeys.Milking && sc.Phase == StatePhase.Active && sc.IsEnabled)
        {
            HandleMilkingClick(ctx, playerEntity, ref playerState, ref sc);
            return;
        }

        // If actively breeding, handle breed clicks
        if (sc.Key == StateKeys.Breed && sc.Phase == StatePhase.Active && sc.IsEnabled)
        {
            HandleBreedingClick(ctx, playerEntity, ref playerState, ref sc);
            return;
        }

        // Skip interaction if in any other active state
        if (sc.IsEnabled) return;

        // Find nearest interactable from interaction zone overlaps
        Entity nearestTarget = FindNearestFromZone(ctx, playerEntity, ref playerState);

        bool isHelperPlayer = ctx.State.HasComponent<HelperPlayerComponent>(playerEntity);

        if (nearestTarget == Entity.Null)
        {
            // Empty-air click while carrying a hammer → drop the hammer at player's feet.
            if (playerState.CarriedEntity != Entity.Null
                && ctx.State.HasComponent<HammerComponent>(playerState.CarriedEntity))
            {
                if (ctx.State.HasComponent<Transform2D>(playerEntity))
                {
                    var pp = ctx.State.GetComponent<Transform2D>(playerEntity).Position;
                    DropCarriedHammerAt(ctx, ref playerState, pp);
                    ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "drop_hammer", Age = 0 });
                }
                return;
            }
            // Empty-air click while carrying a pet → assign to self.
            if (!isHelperPlayer && playerState.CarriedEntity != Entity.Null
                && HandlePetAssign(ctx, playerEntity, playerEntity, ref playerState))
            {
                ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
            }
            return;
        }

        Entity globalResEntity = GetGlobalResourcesEntity(ctx);
        if (globalResEntity == Entity.Null) return;
        ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity);

        bool success = false;
        string missingResource = null;
        string gainedResource = null;
        Entity interactedTarget = nearestTarget;

        // Cooldown gate: any entity with an active CooldownComponent ignores all gameplay interactions
        // (smithy/hammer flow above is exempt — hammer demolish should still work on cooled-down buildings,
        // and carried-hammer drop is handled before this point).
        if (IsOnCooldown(ctx, nearestTarget)
            && !ctx.State.HasComponent<HammerComponent>(nearestTarget)
            && !(playerState.CarriedEntity != Entity.Null && ctx.State.HasComponent<HammerComponent>(playerState.CarriedEntity)))
        {
            ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.NotEnoughResource, Param = "cooldown", Age = 0 });
            return;
        }

        // Hammer takes priority: clicking a building demolishes it; clicking the carried hammer drops it.
        bool carryingHammer = playerState.CarriedEntity != Entity.Null
                              && ctx.State.HasComponent<HammerComponent>(playerState.CarriedEntity);
        if (carryingHammer && nearestTarget == playerState.CarriedEntity)
        {
            if (ctx.State.HasComponent<Transform2D>(playerEntity))
            {
                var pp = ctx.State.GetComponent<Transform2D>(playerEntity).Position;
                DropCarriedHammerAt(ctx, ref playerState, pp);
                ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "drop_hammer", Age = 0 });
            }
            return;
        }
        if (carryingHammer && IsDemolishableBuilding(ctx, nearestTarget))
        {
            var hammer = playerState.CarriedEntity;
            DemolishBuilding(ctx, nearestTarget, ref globalRes);
            playerState.CarriedEntity = Entity.Null;
            if (ctx.State.HasComponent<HammerComponent>(hammer))
                ctx.State.DeleteEntity(hammer);
            ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "demolish", Age = 0 });
            return;
        }

        // Carrying a pet → click target to assign (or click pet itself to drop).
        // Helper-players are tactical only — they never carry pets, so this branch is a no-op for them.
        if (!isHelperPlayer
            && playerState.CarriedEntity != Entity.Null
            && nearestTarget != playerState.CarriedEntity
            && IsValidPetAssignTarget(ctx, nearestTarget))
        {
            if (HandlePetAssign(ctx, playerEntity, nearestTarget, ref playerState))
            {
                ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
                return;
            }
        }

        // Pet pickup is a strategic action (cat distribution = late-game build identity).
        // Helper-players cannot pick up cats.
        if (!isHelperPlayer && ctx.State.HasComponent<HelperPetComponent>(nearestTarget))
        {
            success = HandlePetInteraction(ctx, playerEntity, nearestTarget, ref playerState);
            if (success)
            {
                ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
                return;
            }
        }
        // Helper interaction → resource exchange first; fall back to pickup into FollowingHelper
        else if (ctx.State.HasComponent<HelperComponent>(nearestTarget))
        {
            // Drop the helper if clicking the one we're already carrying
            if (playerState.FollowingHelper == nearestTarget)
            {
                DropFollowingHelperInPlace(ctx, ref playerState);
                ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.Interacted, Param = "drop", Age = 0 });
                return;
            }

            // Try resource exchange (works for Seller/Builder/Milker idle + WaitingForPickup states)
            success = HandleHelperInteraction(ctx, nearestTarget, ref globalRes);
            if (success)
            {
                ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
            }
            else if (playerState.FollowingHelper == Entity.Null && !IsHelperAssignedToHouse(ctx, nearestTarget))
            {
                // No exchange happened → pick up the helper to carry/assign
                success = HandleHelperPickup(ctx, playerEntity, nearestTarget, ref playerState);
                if (success) return;
            }
        }
        // Main player ↔ helper-player resource exchange (treat helper-player like a helper)
        else if (ctx.State.HasComponent<HelperPlayerComponent>(nearestTarget) && !isHelperPlayer)
        {
            success = HandleHelperPlayerInteraction(ctx, nearestTarget, ref globalRes);
            if (success)
                ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
        }
        // Player-on-player interact: shove the target a bit. If a helper-player clicks
        // the main player, also dump their bag into global storage.
        else if (ctx.State.HasComponent<PlayerEntity>(nearestTarget) && nearestTarget != playerEntity)
        {
            ApplyPlayerPush(ctx, playerEntity, nearestTarget);
            if (isHelperPlayer && !ctx.State.HasComponent<HelperPlayerComponent>(nearestTarget))
                success = DumpHelperPlayerBagToGlobal(ctx, playerEntity, ref globalRes, out gainedResource);
            else
                success = true;
        }
        // Cow interaction → always tame (add to follow chain)
        else if (ctx.State.HasComponent<CowComponent>(nearestTarget))
        {
            success = HandleCowTame(ctx, playerEntity, nearestTarget, ref playerState, ref sc);
            if (success) return;
        }
        // House interaction
        else if (ctx.State.HasComponent<HouseComponent>(nearestTarget))
        {
            // Helper-player branch: move into an empty house, or move out of their own.
            if (isHelperPlayer)
            {
                ref var house = ref ctx.State.GetComponent<HouseComponent>(nearestTarget);
                if (house.HelperId == playerEntity)
                {
                    // Already living here → move out, despawn role sign.
                    house.HelperId = Entity.Null;
                    DespawnSignsForHouse(ctx, nearestTarget);
                    ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
                    return;
                }
                if (house.CowId == Entity.Null && house.HelperId == Entity.Null)
                {
                    // Empty house → move in, spawn role sign matching current role.
                    house.HelperId = playerEntity;
                    int currentRole = ctx.State.GetComponent<HelperPlayerComponent>(playerEntity).Type;
                    EnsureRoleSignForHouseHelperPlayer(ctx, nearestTarget, currentRole);
                    ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
                    return;
                }
                // Otherwise (occupied by a cow / a real maid / another helper-player) — no-op.
                return;
            }

            if (playerState.FollowingCow != Entity.Null)
            {
                // Have following cows → assign first cow to house
                success = HandleHouseAssign(ctx, playerEntity, nearestTarget, ref playerState, ref sc);
                if (success) return;
            }
            else
            {
                ref var house = ref ctx.State.GetComponent<HouseComponent>(nearestTarget);
                if (house.CowId != Entity.Null && ctx.State.HasComponent<CowComponent>(house.CowId))
                {
                    // No following cows → milk the cow in this house
                    success = HandleHouseMilk(ctx, playerEntity, nearestTarget, house.CowId, ref playerState, ref sc, ref globalRes, out missingResource);
                    if (success) return;
                }
                // Empty house + carrying a helper → assign helper to house
                else if (house.CowId == Entity.Null && house.HelperId == Entity.Null && playerState.FollowingHelper != Entity.Null)
                {
                    success = HandleHouseAssignFollowingHelper(ctx, playerEntity, nearestTarget, ref playerState);
                    if (success) return;
                }
            }
        }
        // Love House interaction — strategic (controls breeding pipeline). Main player only.
        else if (ctx.State.HasComponent<LoveHouseComponent>(nearestTarget))
        {
            if (isHelperPlayer) return;
            ref var lh = ref ctx.State.GetComponent<LoveHouseComponent>(nearestTarget);
            // Cooldown is sleep-only — click does nothing while it's active.
            if (IsOnCooldown(ctx, nearestTarget)) return;
            bool bothFull = lh.CowId1 != Entity.Null && lh.CowId2 != Entity.Null;
            if (bothFull)
            {
                // Both slots full → start breeding
                success = HandleLoveHouseStartBreed(ctx, playerEntity, nearestTarget, ref playerState, ref sc, ref globalRes, out missingResource);
                if (success) return;
            }
            else if (playerState.FollowingCow != Entity.Null)
            {
                // Has following cows and love house has empty slot → assign cow
                success = HandleLoveHouseAssign(ctx, playerEntity, nearestTarget, ref playerState, ref sc, ref globalRes, out missingResource);
                if (success) return;
            }
        }
        else if (ctx.State.HasComponent<FoodSignComponent>(nearestTarget))
        {
            success = HandleFoodSignInteraction(ctx, nearestTarget);
        }
        else if (ctx.State.HasComponent<RoleSignComponent>(nearestTarget))
        {
            // agent-helpers-in-house: role sign cycles helper type
            success = HandleRoleSignInteraction(ctx, nearestTarget);
        }
        else if (ctx.State.HasComponent<LandSignComponent>(nearestTarget))
        {
            // Choosing the building type for a plot is a strategic decision — main player only.
            if (isHelperPlayer) return;
            success = HandleLandSignInteraction(ctx, nearestTarget);
        }
        else if (ctx.State.HasComponent<LandPriceSignComponent>(nearestTarget))
        {
            // Price sign deposits coins on the linked land plot.
            // Keep interactedTarget = sign so the squish + outline animations fire on the sign visual.
            var landId = ctx.State.GetComponent<LandPriceSignComponent>(nearestTarget).LandId;
            if (landId != Entity.Null && ctx.State.HasComponent<LandComponent>(landId))
                success = HandleLandInteraction(ctx, playerEntity, landId, ref globalRes, out missingResource);
        }
        else if (ctx.State.HasComponent<WarehouseSignComponent>(nearestTarget))
        {
            success = HandleWarehouseSignInteraction(ctx, nearestTarget);
        }
        else if (ctx.State.HasComponent<GrassComponent>(nearestTarget))
        {
            var foodType = ctx.State.GetComponent<GrassComponent>(nearestTarget).FoodType;
            if (isHelperPlayer)
                success = HandleFoodInteractionForHelperPlayer(ctx, playerEntity, nearestTarget);
            else
                success = HandleFoodInteraction(ctx, nearestTarget, ref globalRes);
            if (success) gainedResource = FoodTypeToKey(foodType);
        }
        else if (ctx.State.HasComponent<PlayerHouseComponent>(nearestTarget))
        {
            // Sleep advances the day — strategic, main player only.
            if (isHelperPlayer) return;
            success = HandlePlayerHouseInteraction(ctx, nearestTarget);
            if (!success) missingResource = null;
        }
        else if (ctx.State.HasComponent<SellPointComponent>(nearestTarget))
        {
            success = HandleSellPointInteraction(ctx, playerEntity, nearestTarget, ref playerState, ref globalRes, out missingResource);
            if (success) gainedResource = StateKeys.Coins;
        }
        else if (ctx.State.HasComponent<FinalStructureComponent>(nearestTarget))
        {
            success = HandleFinalStructureInteraction(ctx, nearestTarget, ref globalRes, out missingResource);
        }
        else if (ctx.State.HasComponent<SmithyComponent>(nearestTarget))
        {
            // Smithy hands out a hammer. Player must have empty hands.
            if (playerState.CarriedEntity == Entity.Null && ctx.State.HasComponent<Transform2D>(playerEntity))
            {
                var pp = ctx.State.GetComponent<Transform2D>(playerEntity).Position;
                SpawnAndCarryHammer(ctx, ref playerState, pp);
                success = true;
            }
        }
        else if (ctx.State.HasComponent<HammerComponent>(nearestTarget))
        {
            // Pick up an idle hammer off the ground (or no-op if hands are full or it's already carried).
            var h = ctx.State.GetComponent<HammerComponent>(nearestTarget);
            if (h.State == HammerState.Idle && playerState.CarriedEntity == Entity.Null)
            {
                ref var hh = ref ctx.State.GetComponent<HammerComponent>(nearestTarget);
                hh.State = HammerState.Carried;
                playerState.CarriedEntity = nearestTarget;
                success = true;
            }
        }

        if (success)
        {
            ctx.State.AddComponent(interactedTarget, new EnterStateComponent { Key = StateKeys.Interacted, Param = gainedResource ?? "", Age = 0 });
            if (gainedResource != null)
                ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.GainedResource, Param = gainedResource, Age = 0 });
            ILogger.Log($"[InteractActionService] Marked {interactedTarget.Id} as successfully interacted, gained: {gainedResource}");
        }
        else if (missingResource != null)
        {
            sc.Key = StateKeys.NotEnoughResource;
            sc.CurrentTime = 0;
            sc.MaxTime = NotEnoughResourceDurationTicks;
            sc.IsEnabled = true;

            // Show not-enough popup above target, not above player
            ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.NotEnoughResource, Param = missingResource, Age = 0 });
            ILogger.Log($"[InteractActionService] Not enough {missingResource} for interaction with {nearestTarget.Id}");
        }
        else
        {
            // No action taken — show building info popup if the target is a known building
            string infoKey = GetBuildingInfoKey(ctx, nearestTarget);
            if (infoKey != null)
            {
                ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.BuildingInfo, Param = infoKey, Age = 0 });
                ILogger.Log($"[InteractActionService] Showing building info '{infoKey}' for entity {nearestTarget.Id}");
            }
        }
    }

    private void HandleMilkingClick(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        var cowEntity = playerState.InteractionTarget;
        if (cowEntity == Entity.Null || !ctx.State.HasComponent<CowComponent>(cowEntity)) return;

        Entity globalResEntity = GetGlobalResourcesEntity(ctx);
        if (globalResEntity == Entity.Null) return;
        ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity);
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);

        // agent-helpers-in-house: source of truth is cow.SelectedFood (travels with cow)
        int foodToUse = cow.SelectedFood;

        int cowBoost = Template.Shared.GameData.Balance.Pets.AdditiveBoostBase + Template.Shared.GameData.Balance.Pets.BoostPerPet * cow.PetCount;
        int exhaustPerClick = cowBoost;
        bool produced = InteractionLogic.MilkCow(ctx.State, cowEntity, foodToUse, exhaustPerClick, out bool cowDone);

        Entity target = cowEntity;
        cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
        if (cow.HouseId != Entity.Null)
            target = cow.HouseId;

        ctx.State.AddComponent(target, new EnterStateComponent { Key = StateKeys.Interacted, Param = produced ? "milk_ok" : "milk_fail", Age = 0 });

        if (cowDone)
        {
            StateDefinitions.BeginExit(ref sc);
            ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = sc.Key, Phase = sc.Phase, Age = 0 });
        }
    }

    private void HandleBreedingClick(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        var loveHouseEntity = playerState.InteractionTarget;
        if (loveHouseEntity == Entity.Null || !ctx.State.HasComponent<LoveHouseComponent>(loveHouseEntity)) return;

        ref var loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);
        loveHouse.BreedProgress++;

        var _gt = ctx.State.GetCustomData<IGameTime>();
        bool _resim = _gt is GameSimulation _sim && _sim.IsResimulating;
        ILogger.Log($"[BreedClick] Tick={_gt?.CurrentTick} Progress={loveHouse.BreedProgress}/{loveHouse.BreedCost} Resim={_resim}");

        // Compute breed luck for visual heart feedback
        int heartPercent = Template.Shared.GameData.Balance.Breed.HeartDefault;
        if (ctx.State.HasComponent<CowComponent>(loveHouse.CowId1) && ctx.State.HasComponent<CowComponent>(loveHouse.CowId2))
        {
            var c1 = ctx.State.GetComponent<CowComponent>(loveHouse.CowId1);
            var c2 = ctx.State.GetComponent<CowComponent>(loveHouse.CowId2);
            bool sameTier = c1.PreferredFood == c2.PreferredFood;

            bool isLovePair = c1.LoveTarget == loveHouse.CowId2 || c2.LoveTarget == loveHouse.CowId1;
            if (isLovePair)
                heartPercent = Template.Shared.GameData.Balance.Breed.HeartLovePair;
            else if (sameTier)
                heartPercent = Template.Shared.GameData.Balance.Breed.HeartSameTierDuring;
            else
            {
                int tierGap = System.Math.Abs(c1.PreferredFood - c2.PreferredFood);
                heartPercent = tierGap switch
                {
                    1 => Template.Shared.GameData.Balance.Breed.HeartTierGap1,
                    2 => Template.Shared.GameData.Balance.Breed.HeartTierGap2,
                    _ => Template.Shared.GameData.Balance.Breed.HeartTierGap3Plus,
                };
            }
        }

        ctx.State.AddComponent(loveHouseEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = $"breed_{heartPercent}", Age = 0 });
        ILogger.Log($"[InteractActionService] Breed click {loveHouse.BreedProgress}/{loveHouse.BreedCost} at love house {loveHouseEntity.Id}");

        if (loveHouse.BreedProgress >= loveHouse.BreedCost)
        {
            StateDefinitions.BeginExit(ref sc);
            ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = sc.Key, Phase = sc.Phase, Age = 0 });
        }
    }

    private bool HandleCowTame(Context ctx, Entity playerEntity, Entity cowEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);

        // Clicking a cow that's following us
        if (cow.FollowingPlayer == playerEntity)
        {
            // Love cow interaction: first click shows confession popup, subsequent clicks are no-ops (cow keeps following)
            if (cow.LoveTarget != Entity.Null)
            {
                if (!cow.LoveConfessed)
                {
                    // First interaction: show the love popup and mark as confessed
                    string targetName = "???";
                    if (ctx.State.HasComponent<NameComponent>(cow.LoveTarget))
                        targetName = ctx.State.GetComponent<NameComponent>(cow.LoveTarget).Name.ToString();

                    ref var cowRef = ref ctx.State.GetComponent<CowComponent>(cowEntity);
                    cowRef.LoveConfessed = true;

                    ctx.State.AddComponent(cowEntity, new EnterStateComponent { Key = StateKeys.LoveCow, Param = targetName, Age = 0 });
                    ILogger.Log($"[InteractActionService] Love cow {cowEntity.Id} confessed — loves {targetName} (cow {cow.LoveTarget.Id})");
                }
                else
                {
                    ILogger.Log($"[InteractActionService] Love cow {cowEntity.Id} already confessed — still following player");
                }
                return true;
            }

            // Normal dismiss: stop following
            // Find next cow in chain (the one following this cow)
            Entity next = Entity.Null;
            foreach (var ce in ctx.State.Filter<CowComponent>())
            {
                if (ce == cowEntity) continue;
                if (ctx.State.GetComponent<CowComponent>(ce).FollowTarget == cowEntity
                    && ctx.State.GetComponent<CowComponent>(ce).FollowingPlayer == playerEntity)
                { next = ce; break; }
            }

            if (playerState.FollowingCow == cowEntity)
            {
                // First in chain: promote next
                playerState.FollowingCow = next;
                if (next != Entity.Null)
                {
                    ref var nc = ref ctx.State.GetComponent<CowComponent>(next);
                    nc.FollowTarget = playerEntity;
                }
            }
            else
            {
                // Mid-chain: relink next to follow what this cow was following
                Entity myTarget = cow.FollowTarget;
                if (next != Entity.Null)
                {
                    ref var nc = ref ctx.State.GetComponent<CowComponent>(next);
                    nc.FollowTarget = myTarget;
                }
            }

            cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
            cow.FollowingPlayer = Entity.Null;
            cow.FollowTarget = Entity.Null;
            ILogger.Log($"[InteractActionService] Player {playerEntity.Id} dismissed cow {cowEntity.Id} from follow chain");
            return true;
        }

        // Can't tame a cow that's being milked, depressed, or already following someone
        if (cow.IsMilking) return false;
        if (cow.IsDepressed)
        {
            ctx.State.AddComponent(cowEntity, new EnterStateComponent { Key = StateKeys.BuildingInfo, Param = StateKeys.InfoDepressed, Age = 0 });
            return true;
        }
        if (cow.FollowingPlayer != Entity.Null) return false;

        StateDefinitions.Begin(ref sc, StateKeys.Taming);
        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Taming, Phase = sc.Phase, Age = 0 });
        ctx.State.AddComponent(cowEntity, new EnterStateComponent { Key = StateKeys.Interacted, Age = 0 });

        playerState.InteractionTarget = cowEntity;

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} taming cow {cowEntity.Id}");
        return true;
    }

    private bool HandleHouseMilk(Context ctx, Entity playerEntity, Entity houseEntity, Entity cowEntity, ref PlayerStateComponent playerState, ref StateComponent sc, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
        ref var house = ref ctx.State.GetComponent<HouseComponent>(houseEntity);

        if (cow.IsMilking) return false;
        if (cow.IsDepressed) return false;
        if (cow.Exhaust >= cow.MaxExhaust)
        {
            missingResource = StateKeys.CowTired;
            return false;
        }

        // agent-helpers-in-house: read selected food from cow (travels with cow between houses)
        int selectedFood = cow.SelectedFood;
        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);

        // Selected food must be within the cow's supported tier range. Lower-tier
        // food can work, but milking logic may consume it without producing milk.
        if (selectedFood < 0 || selectedFood > cowMaxTier)
        {
            missingResource = FoodTypeToKey(selectedFood);
            return false;
        }

        // Must have the selected food available
        if (globalRes.GetFood(selectedFood) <= 0)
        {
            missingResource = FoodTypeToKey(selectedFood);
            return false;
        }

        // Must have the prerequisite milk product (if any)
        int prereq = FoodType.PrerequisiteProduct(selectedFood);
        if (prereq >= 0 && globalRes.GetMilkProduct(prereq) <= 0)
        {
            missingResource = MilkProductToKey(prereq);
            return false;
        }

        cow.IsMilking = true;

        StateDefinitions.Begin(ref sc, StateKeys.Milking);
        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Milking, Phase = sc.Phase, Age = 0 });

        playerState.InteractionTarget = cowEntity;

        if (ctx.State.HasComponent<Transform2D>(playerEntity))
            playerState.ReturnPosition = ctx.State.GetComponent<Transform2D>(playerEntity).Position;

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} milking cow {cowEntity.Id} at house {houseEntity.Id}");
        return true;
    }

    private static Entity FindNearestFromZone(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState)
        => InteractionLogic.FindNearestInteractableInZone(ctx.State, playerEntity, playerState.InteractionZone);

    private bool HandleFoodSignInteraction(Context ctx, Entity signEntity)
    {
        ref var sign = ref ctx.State.GetComponent<FoodSignComponent>(signEntity);

        // Cycle: Grass → Carrot → Apple → Mushroom → Grass
        sign.SelectedFood = (sign.SelectedFood + 1) % 4;

        // agent-helpers-in-house: cow.SelectedFood is source of truth; update both house cache and cow.
        if (sign.HouseId != Entity.Null && ctx.State.HasComponent<HouseComponent>(sign.HouseId))
        {
            ref var house = ref ctx.State.GetComponent<HouseComponent>(sign.HouseId);
            house.SelectedFood = sign.SelectedFood;
            var cowId = house.CowId;
            if (cowId != Entity.Null && ctx.State.HasComponent<CowComponent>(cowId))
            {
                ref var cow = ref ctx.State.GetComponent<CowComponent>(cowId);
                cow.SelectedFood = sign.SelectedFood;
            }
        }

        ILogger.Log($"[InteractActionService] Food sign {signEntity.Id} cycled to food type {sign.SelectedFood}");
        return true;
    }

    // agent-helpers-in-house: cycle helper role on a role sign
    private bool HandleRoleSignInteraction(Context ctx, Entity signEntity)
    {
        ref var sign = ref ctx.State.GetComponent<RoleSignComponent>(signEntity);

        int prev = sign.Role;
        int next = prev switch
        {
            HelperType.Assistant => HelperType.Gatherer,
            HelperType.Gatherer => HelperType.Seller,
            HelperType.Seller => HelperType.Builder,
            HelperType.Builder => HelperType.Milker,
            HelperType.Milker => HelperType.Assistant,
            _ => HelperType.Assistant,
        };
        sign.Role = next;

        if (sign.HouseId != Entity.Null && ctx.State.HasComponent<HouseComponent>(sign.HouseId))
        {
            var house = ctx.State.GetComponent<HouseComponent>(sign.HouseId);
            var helperId = house.HelperId;
            if (helperId != Entity.Null && ctx.State.HasComponent<HelperPlayerComponent>(helperId))
            {
                int hpNext = prev switch
                {
                    HelperType.Gatherer => HelperType.Seller,
                    HelperType.Seller => HelperType.Builder,
                    HelperType.Builder => HelperType.Milker,
                    HelperType.Milker => HelperType.Gatherer,
                    _ => HelperType.Gatherer,
                };
                sign.Role = hpNext;
                next = hpNext;

                ref var hp = ref ctx.State.GetComponent<HelperPlayerComponent>(helperId);
                hp.Type = hpNext;
                hp.State = HelperState.Idle;
                hp.WantedFoodType = -1;
                hp.ClearBag();
                hp.BagCapacity = Template.Shared.GameData.Balance.HelperPlayer.BagCapacity;
            }
            else if (helperId != Entity.Null && ctx.State.HasComponent<HelperComponent>(helperId))
            {
                ref var helper = ref ctx.State.GetComponent<HelperComponent>(helperId);
                helper.Type = next;
                helper.State = HelperState.Idle;
                helper.WantedFoodType = -1;
                helper.TargetEntity = Entity.Null;
                helper.WorkTimer = 0;
                helper.WorkDuration = 0;
                var info = HelperConfig.GetByType(next);
                helper.BagCapacity = info.BaseCapacity;
                helper.BagGrass = 0;
                helper.BagCarrot = 0;
                helper.BagApple = 0;
                helper.BagMushroom = 0;
                helper.BagMilk = 0;
                helper.BagCarrotMilkshake = 0;
                helper.BagVitaminMix = 0;
                helper.BagPurplePotion = 0;
                helper.BagCoins = 0;
            }
        }

        ILogger.Log($"[InteractActionService] Role sign {signEntity.Id} cycled to role {next}");
        return true;
    }

    // agent-helpers-in-house: find existing food/role signs for a house
    private static Entity FindFoodSignForHouse(Context ctx, Entity houseEntity)
    {
        foreach (var e in ctx.State.Filter<FoodSignComponent>())
        {
            if (ctx.State.GetComponent<FoodSignComponent>(e).HouseId == houseEntity) return e;
        }
        return Entity.Null;
    }

    private static Entity FindRoleSignForHouse(Context ctx, Entity houseEntity)
    {
        foreach (var e in ctx.State.Filter<RoleSignComponent>())
        {
            if (ctx.State.GetComponent<RoleSignComponent>(e).HouseId == houseEntity) return e;
        }
        return Entity.Null;
    }

    /// <summary>Spawn or update the food sign next to a house with a cow occupant. Despawns any role sign.</summary>
    public static void EnsureFoodSignForHouse(Context ctx, Entity houseEntity, Entity cowEntity)
    {
        if (!ctx.State.HasComponent<HouseComponent>(houseEntity)) return;
        if (!ctx.State.HasComponent<CowComponent>(cowEntity)) return;

        var existingRoleSign = FindRoleSignForHouse(ctx, houseEntity);
        if (existingRoleSign != Entity.Null) ctx.State.DeleteEntity(existingRoleSign);

        int cowFood = ctx.State.GetComponent<CowComponent>(cowEntity).SelectedFood;
        var existing = FindFoodSignForHouse(ctx, houseEntity);
        if (existing != Entity.Null)
        {
            ref var s = ref ctx.State.GetComponent<FoodSignComponent>(existing);
            s.SelectedFood = cowFood;
        }
        else
        {
            var housePos = ctx.State.GetComponent<Transform2D>(houseEntity).Position;
            var sign = FoodSignDefinition.Create(ctx, housePos + new Vector2(-2, 0), houseEntity);
            ref var s = ref ctx.State.GetComponent<FoodSignComponent>(sign);
            s.SelectedFood = cowFood;
        }

        ref var house = ref ctx.State.GetComponent<HouseComponent>(houseEntity);
        house.SelectedFood = cowFood;
    }

    /// <summary>Spawn or update the role sign next to a house with a helper occupant. Despawns any food sign.</summary>
    public static void EnsureRoleSignForHouse(Context ctx, Entity houseEntity, Entity helperEntity)
    {
        if (!ctx.State.HasComponent<HouseComponent>(houseEntity)) return;
        if (!ctx.State.HasComponent<HelperComponent>(helperEntity)) return;

        var existingFoodSign = FindFoodSignForHouse(ctx, houseEntity);
        if (existingFoodSign != Entity.Null) ctx.State.DeleteEntity(existingFoodSign);

        int role = ctx.State.GetComponent<HelperComponent>(helperEntity).Type;
        var existing = FindRoleSignForHouse(ctx, houseEntity);
        if (existing != Entity.Null)
        {
            ref var s = ref ctx.State.GetComponent<RoleSignComponent>(existing);
            s.Role = role;
        }
        else
        {
            var housePos = ctx.State.GetComponent<Transform2D>(houseEntity).Position;
            RoleSignDefinition.Create(ctx, housePos + new Vector2(-2, 0), houseEntity, role);
        }
    }

    /// <summary>Spawn or update the role sign for a house occupied by a helper-player.</summary>
    public static void EnsureRoleSignForHouseHelperPlayer(Context ctx, Entity houseEntity, int role)
    {
        if (!ctx.State.HasComponent<HouseComponent>(houseEntity)) return;

        var existingFoodSign = FindFoodSignForHouse(ctx, houseEntity);
        if (existingFoodSign != Entity.Null) ctx.State.DeleteEntity(existingFoodSign);

        var existing = FindRoleSignForHouse(ctx, houseEntity);
        if (existing != Entity.Null)
        {
            ref var s = ref ctx.State.GetComponent<RoleSignComponent>(existing);
            s.Role = role;
        }
        else
        {
            var housePos = ctx.State.GetComponent<Transform2D>(houseEntity).Position;
            RoleSignDefinition.Create(ctx, housePos + new Vector2(-2, 0), houseEntity, role);
        }
    }

    /// <summary>Despawn any food/role sign attached to a house (called when occupant leaves).</summary>
    public static void DespawnSignsForHouse(Context ctx, Entity houseEntity)
    {
        var foodSign = FindFoodSignForHouse(ctx, houseEntity);
        if (foodSign != Entity.Null) ctx.State.DeleteEntity(foodSign);
        var roleSign = FindRoleSignForHouse(ctx, houseEntity);
        if (roleSign != Entity.Null) ctx.State.DeleteEntity(roleSign);
    }

    private bool HandleLandSignInteraction(Context ctx, Entity signEntity)
    {
        ref var sign = ref ctx.State.GetComponent<LandSignComponent>(signEntity);
        if (sign.LandId == Entity.Null || !ctx.State.HasComponent<LandComponent>(sign.LandId))
            return false;

        ref var land = ref ctx.State.GetComponent<LandComponent>(sign.LandId);

        // Fixed positions (PlayerHouse at center, shifted SellPoint, FinalStructure)
        // display the type sign for clarity but their type is locked — no cycling.
        if (StarGrid.GetFixedType(land.Arm, land.Ring).HasValue) return false;

        // Locked once the player has started depositing — cycling resets when the plot completes.
        if (land.CurrentCoins > 0) return false;

        int ringDist = System.Math.Abs(land.Arm) + System.Math.Abs(land.Ring);
        var pool = StarGrid.GetCycleableTypesForRing(ctx.State, ringDist, land.Arm, land.Ring);
        if (pool.Length == 0) return false;

        int idx = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == sign.SelectedType) { idx = i; break; }
        }
        var next = pool[(idx + 1) % pool.Length];

        sign.SelectedType = next;
        land.Type = next;
        int pm = StarGrid.GetPriceMultiplier(next);
        int gridDist = System.Math.Max(1, ringDist);
        land.Threshold = pm < 0
            ? gridDist * StarGrid.GetEraMultiplier(gridDist) * Template.Shared.GameData.Balance.Build.BasePriceMultiplier / 4
            : gridDist * StarGrid.GetEraMultiplier(gridDist) * pm * Template.Shared.GameData.Balance.Build.BasePriceMultiplier;

        ctx.State.AddComponent(sign.LandId, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });

        ILogger.Log($"[InteractActionService] Land sign {signEntity.Id} cycled to type {next} (ring {ringDist})");
        return true;
    }

    private bool HandleWarehouseSignInteraction(Context ctx, Entity signEntity)
    {
        ref var sign = ref ctx.State.GetComponent<WarehouseSignComponent>(signEntity);

        // Toggle: 0 → 1 → 0
        sign.Enabled = sign.Enabled == 0 ? 1 : 0;

        // Also update the linked warehouse's Enabled state
        if (sign.WarehouseId != Entity.Null && ctx.State.HasComponent<WarehouseComponent>(sign.WarehouseId))
        {
            ref var warehouse = ref ctx.State.GetComponent<WarehouseComponent>(sign.WarehouseId);
            warehouse.Enabled = sign.Enabled;
        }

        ILogger.Log($"[InteractActionService] Warehouse sign {signEntity.Id} toggled to {(sign.Enabled == 1 ? "ENABLED" : "DISABLED")}");
        return true;
    }

    private bool HandleFoodInteraction(Context ctx, Entity foodEntity, ref GlobalResourcesComponent globalRes)
    {
        ref var grass = ref ctx.State.GetComponent<GrassComponent>(foodEntity);
        grass.Durability -= 1;
        globalRes.AddFood(grass.FoodType, 1);

        if (grass.Durability <= 0)
        {
            ctx.State.DeleteEntity(foodEntity);
            return false;
        }
        return true;
    }

    private bool HandleHouseAssign(Context ctx, Entity playerEntity, Entity houseEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        StateDefinitions.Begin(ref sc, StateKeys.Assign);
        playerState.InteractionTarget = houseEntity;

        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Assign, Phase = sc.Phase, Age = 0 });

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} assigning cow {playerState.FollowingCow.Id} to house {houseEntity.Id}");
        return true;
    }

    private bool HandleHelperPickup(Context ctx, Entity playerEntity, Entity helperEntity, ref PlayerStateComponent playerState)
    {
        if (!ctx.State.HasComponent<HelperComponent>(helperEntity)) return false;

        ref var helper = ref ctx.State.GetComponent<HelperComponent>(helperEntity);
        helper.OwnerPlayer = playerEntity;

        playerState.FollowingHelper = helperEntity;

        if (ctx.State.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(helperEntity))
        {
            ref var hb = ref ctx.State.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(helperEntity);
            hb.Velocity = Vector2.Zero;
        }

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} picked up helper {helperEntity.Id}");
        return true;
    }

    private bool HandleHouseAssignFollowingHelper(Context ctx, Entity playerEntity, Entity houseEntity, ref PlayerStateComponent playerState)
    {
        if (!ctx.State.HasComponent<HouseComponent>(houseEntity)) return false;
        var helperEntity = playerState.FollowingHelper;
        if (helperEntity == Entity.Null || !ctx.State.HasComponent<HelperComponent>(helperEntity)) return false;

        ref var house = ref ctx.State.GetComponent<HouseComponent>(houseEntity);
        house.HelperId = helperEntity;

        if (ctx.State.HasComponent<Transform2D>(houseEntity) && ctx.State.HasComponent<Transform2D>(helperEntity))
        {
            var housePos = ctx.State.GetComponent<Transform2D>(houseEntity).Position;
            ref var ht = ref ctx.State.GetComponent<Transform2D>(helperEntity);
            ht.Position = housePos;
        }
        if (ctx.State.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(helperEntity))
        {
            ref var hb = ref ctx.State.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(helperEntity);
            hb.Velocity = Vector2.Zero;
        }

        EnsureRoleSignForHouse(ctx, houseEntity, helperEntity);

        playerState.FollowingHelper = Entity.Null;

        ctx.State.AddComponent(houseEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} assigned carried helper {helperEntity.Id} to house {houseEntity.Id}");
        return true;
    }

    private void DropFollowingHelperInPlace(Context ctx, ref PlayerStateComponent playerState)
    {
        var helperEntity = playerState.FollowingHelper;
        if (helperEntity == Entity.Null) return;
        playerState.FollowingHelper = Entity.Null;
        ILogger.Log($"[InteractActionService] Dropped carried helper {helperEntity.Id} in place");
    }

    private static bool IsHelperAssignedToHouse(Context ctx, Entity helperEntity)
    {
        foreach (var he in ctx.State.Filter<HouseComponent>())
        {
            if (ctx.State.GetComponent<HouseComponent>(he).HelperId == helperEntity) return true;
        }
        return false;
    }

    private bool HandleLoveHouseAssign(Context ctx, Entity playerEntity, Entity loveHouseEntity, ref PlayerStateComponent playerState, ref StateComponent sc, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        var cowToAssign = playerState.FollowingCow;
        if (cowToAssign == Entity.Null) return false;

        ref var loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);

        // Block assignment while love house is on cooldown
        if (IsOnCooldown(ctx, loveHouseEntity))
        {
            ILogger.Log($"[InteractActionService] Love house {loveHouseEntity.Id} is on cooldown, cannot assign cow");
            return false;
        }

        // Check if love house already has 2 cows (full)
        if (loveHouse.CowId1 != Entity.Null && loveHouse.CowId2 != Entity.Null) return false;

        // Find next cow in chain (to promote after removing first)
        Entity nextCow = Entity.Null;
        foreach (var ce in ctx.State.Filter<CowComponent>())
        {
            var c = ctx.State.GetComponent<CowComponent>(ce);
            if (c.FollowTarget == cowToAssign && c.FollowingPlayer != Entity.Null)
            { nextCow = ce; break; }
        }

        // Remove cow from follow chain, preserve previous house for return after breeding
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowToAssign);
        if (cow.PreviousHouseId == Entity.Null)
            cow.PreviousHouseId = cow.HouseId;
        cow.FollowingPlayer = Entity.Null;
        cow.FollowTarget = Entity.Null;
        cow.HouseId = loveHouseEntity;

        if (ctx.State.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowToAssign))
        {
            ref var body = ref ctx.State.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowToAssign);
            body.Velocity = Deterministic.GameFramework.Types.Vector2.Zero;
        }

        // Re-get love house ref after touching other components
        loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);

        // Assign to first empty slot, position cow on corresponding side
        bool isFirstSlot = loveHouse.CowId1 == Entity.Null;
        if (isFirstSlot)
            loveHouse.CowId1 = cowToAssign;
        else
            loveHouse.CowId2 = cowToAssign;

        // Cow will walk to love house via CowFollowSystem navigation

        // Promote next cow in chain
        if (nextCow != Entity.Null)
        {
            ref var nextCowComp = ref ctx.State.GetComponent<CowComponent>(nextCow);
            nextCowComp.FollowTarget = playerEntity;
            playerState.FollowingCow = nextCow;
        }
        else
        {
            playerState.FollowingCow = Entity.Null;
        }

        // Show interacted feedback for assignment
        ctx.State.AddComponent(loveHouseEntity, new EnterStateComponent { Key = StateKeys.Interacted, Age = 0 });
        ILogger.Log($"[InteractActionService] Assigned cow {cowToAssign.Id} to love house {loveHouseEntity.Id}");

        return true;
    }

    private bool HandleLoveHouseStartBreed(Context ctx, Entity playerEntity, Entity loveHouseEntity, ref PlayerStateComponent playerState, ref StateComponent sc, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        ref var loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);

        // Block breeding while love house is on cooldown
        if (IsOnCooldown(ctx, loveHouseEntity))
        {
            ILogger.Log($"[InteractActionService] Love house {loveHouseEntity.Id} is on cooldown");
            return false;
        }

        // Set breed cost and heart visual feedback based on cow exhaust/tier values
        int breedCost = Template.Shared.GameData.Balance.Breed.MinCost;
        int heartPercent = Template.Shared.GameData.Balance.Breed.HeartDefault;
        loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);
        if (ctx.State.HasComponent<CowComponent>(loveHouse.CowId1) && ctx.State.HasComponent<CowComponent>(loveHouse.CowId2))
        {
            var c1 = ctx.State.GetComponent<CowComponent>(loveHouse.CowId1);
            var c2 = ctx.State.GetComponent<CowComponent>(loveHouse.CowId2);
            breedCost = System.Math.Max(Template.Shared.GameData.Balance.Breed.MinCost, (c1.MaxExhaust + c2.MaxExhaust) / 2);

            bool sameTier = c1.PreferredFood == c2.PreferredFood;

            bool isLovePair = c1.LoveTarget == loveHouse.CowId2 || c2.LoveTarget == loveHouse.CowId1;
            if (isLovePair)
                heartPercent = Template.Shared.GameData.Balance.Breed.HeartLovePair;
            else if (sameTier)
                heartPercent = Template.Shared.GameData.Balance.Breed.HeartSameTierPre;
            else
            {
                int tierGap = System.Math.Abs(c1.PreferredFood - c2.PreferredFood);
                heartPercent = tierGap switch
                {
                    1 => Template.Shared.GameData.Balance.Breed.HeartTierGap1,
                    2 => Template.Shared.GameData.Balance.Breed.HeartTierGap2,
                    _ => Template.Shared.GameData.Balance.Breed.HeartTierGap3Plus,
                };

                // Pre-roll fail check (mirrors CowSystem.HandleLoveHouseBreedComplete).
                // Skipped entirely when depression is disabled — breeds always succeed.
                if (Template.Shared.GameData.Balance.Cow.DepressionEnabled)
                {
                    var gameTime = ctx.State.GetCustomData<IGameTime>();
                    uint breedSeed = (uint)((loveHouse.CowId1.Id * 7919 + loveHouse.CowId2.Id * 104729) ^ (gameTime?.CurrentTick ?? 0));
                    var breedRandom = new DeterministicRandom(breedSeed);
                    int failChance = tierGap switch
                    {
                        1 => Template.Shared.GameData.Balance.Breed.FailChanceTier1,
                        2 => Template.Shared.GameData.Balance.Breed.FailChanceTier2,
                        _ => Template.Shared.GameData.Balance.Breed.FailChanceTier3Plus,
                    };
                    bool willFail = breedRandom.NextInt(100) < failChance;
                    if (willFail)
                        breedCost *= Template.Shared.GameData.Balance.Breed.FailCostMultiplier;
                }
            }
        }
        loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);
        loveHouse.BreedProgress = 0;
        loveHouse.BreedCost = breedCost;
        loveHouse.HeartPercent = heartPercent;

        StateDefinitions.Begin(ref sc, StateKeys.Breed);
        playerState.InteractionTarget = loveHouseEntity;

        if (ctx.State.HasComponent<Transform2D>(playerEntity))
            playerState.ReturnPosition = ctx.State.GetComponent<Transform2D>(playerEntity).Position;

        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Breed, Phase = sc.Phase, Age = 0 });

        ILogger.Log($"[InteractActionService] Started breeding at love house {loveHouseEntity.Id}, cost={breedCost}");
        return true;
    }

    private bool HandlePlayerHouseInteraction(Context ctx, Entity playerHouseEntity)
    {
        if (ctx.State.HasComponent<CooldownComponent>(playerHouseEntity))
        {
            ref var cd = ref ctx.State.GetComponent<CooldownComponent>(playerHouseEntity);
            if (cd.TicksRemaining > 0)
            {
                // On cooldown — each click subtracts 1 second (60 ticks) from remaining time
                cd.TicksRemaining = System.Math.Max(0, cd.TicksRemaining - Template.Shared.GameData.Balance.PlayerHouse.ClickToSkipTicks);
                ctx.State.AddComponent(playerHouseEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "cooldown_skip", Age = 0 });
                return true;
            }
        }

        // Sleep — advance day, regen cow exhaust, reset food caps
        Systems.SleepLogic.AdvanceDay(ctx.State);
        if (ctx.State.HasComponent<CooldownComponent>(playerHouseEntity))
        {
            ref var cd = ref ctx.State.GetComponent<CooldownComponent>(playerHouseEntity);
            cd.MaxTicks = PlayerHouseComponent.SleepCooldownTicks;
            cd.TicksRemaining = PlayerHouseComponent.SleepCooldownTicks;
            cd.Unit = CooldownUnit.Ticks;
        }
        else
        {
            ctx.State.AddComponent(playerHouseEntity, new CooldownComponent
            {
                MaxTicks = PlayerHouseComponent.SleepCooldownTicks,
                TicksRemaining = PlayerHouseComponent.SleepCooldownTicks,
                Unit = CooldownUnit.Ticks,
            });
        }
        ctx.State.AddComponent(playerHouseEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "sleep", Age = 0 });
        ILogger.Log($"[InteractActionService] Player slept at PlayerHouse {playerHouseEntity.Id} — day advanced");
        return true;
    }

    private bool HandleSellPointInteraction(Context ctx, Entity playerEntity, Entity sellPointEntity, ref PlayerStateComponent playerState, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        // Cow day: every 3rd day (day 3, 6, 9...). Day mod 3 == 2 because counter starts at 0.
        bool cowDay = (globalRes.DayCounter % Template.Shared.GameData.Balance.Sell.DayCycle) == Template.Shared.GameData.Balance.Sell.CowDayRemainder;

        if (cowDay)
        {
            // Selling cows is strategic — main player only. Helper-players fall through
            // to the milk-sell branch on cow-days too (no-op if no milk in stock).
            if (ctx.State.HasComponent<HelperPlayerComponent>(playerEntity))
            {
                missingResource = StateKeys.Cows;
                return false;
            }

            // Sell the front cow in the player's follow chain
            var cowEntity = playerState.FollowingCow;
            if (cowEntity == Entity.Null || !ctx.State.HasComponent<CowComponent>(cowEntity))
            {
                missingResource = StateKeys.Cows;
                return false;
            }

            ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
            if (cow.IsMilking || cow.IsDepressed)
                return false;

            // Block selling the last cow — leaves the player with no breed pool.
            int activeCowCount = 0;
            foreach (var ce in ctx.State.Filter<CowComponent>())
            {
                if (ctx.State.HasComponent<CowForSaleComponent>(ce)) continue;
                activeCowCount++;
                if (activeCowCount > 1) break;
            }
            if (activeCowCount <= 1)
            {
                missingResource = StateKeys.Cows;
                return false;
            }

            // Cow price: rested cow = full price, exhausted = lower. Tier scales price.
            int rested = System.Math.Max(0, cow.MaxExhaust - cow.Exhaust);
            int tierBonus = (cow.PreferredFood + 1) * Template.Shared.GameData.Balance.Sell.CowTierPrice;
            int price = Template.Shared.GameData.Balance.Sell.CowBasePrice
                      + tierBonus
                      + rested * Template.Shared.GameData.Balance.Sell.CowRestedPrice;

            globalRes.Coins += price;

            // Find next cow in chain to promote
            Entity nextCow = Entity.Null;
            foreach (var ce in ctx.State.Filter<CowComponent>())
            {
                if (ce == cowEntity) continue;
                var c = ctx.State.GetComponent<CowComponent>(ce);
                if (c.FollowTarget == cowEntity && c.FollowingPlayer == playerEntity)
                { nextCow = ce; break; }
            }

            // Detach sold cow: stop following, mark for sale, distribute around sell point
            cow.FollowingPlayer = Entity.Null;
            cow.FollowTarget = Entity.Null;
            cow.HouseId = Entity.Null;

            ctx.State.AddComponent(cowEntity, new CowForSaleComponent());

            // Distribute around sell point in a deterministic pseudo-random spot
            if (ctx.State.HasComponent<Transform2D>(sellPointEntity) && ctx.State.HasComponent<Transform2D>(cowEntity))
            {
                var sellPos = ctx.State.GetComponent<Transform2D>(sellPointEntity).Position;
                var gameTime = ctx.State.GetCustomData<IGameTime>();
                uint seed = (uint)(cowEntity.Id * 7919 + (gameTime?.CurrentTick ?? 0));
                var rng = new DeterministicRandom(seed);
                Float angle = rng.NextFloat((Float)0, (Float)6.2831853f);
                Float radius = rng.NextFloat((Float)2.5f, (Float)5.5f);
                var offset = new Vector2(Float.Cos(angle) * radius, Float.Sin(angle) * radius);
                ref var ct = ref ctx.State.GetComponent<Transform2D>(cowEntity);
                ct.Position = sellPos + offset;
                if (ctx.State.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowEntity))
                {
                    ref var body = ref ctx.State.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowEntity);
                    body.Velocity = Vector2.Zero;
                }
            }

            // Promote next cow in chain
            if (nextCow != Entity.Null)
            {
                ref var nc = ref ctx.State.GetComponent<CowComponent>(nextCow);
                nc.FollowTarget = playerEntity;
                playerState.FollowingCow = nextCow;
            }
            else
            {
                playerState.FollowingCow = Entity.Null;
            }

            ctx.State.AddComponent(sellPointEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = StateKeys.Coins, Age = 0 });
            ILogger.Log($"[InteractActionService] Sold cow {cowEntity.Id} for {price} coins (rested={rested}, tier={cow.PreferredFood})");
            return true;
        }

        // Milk day (default) — one milk per click; helper players just click faster (cadence).
        int coins = InteractionLogic.SellFromGlobal(ctx.State, 1);
        if (coins > 0) return true;
        missingResource = StateKeys.Milk;
        return false;
    }

    private bool HandleLandInteraction(Context ctx, Entity playerEntity, Entity landEntity, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        if (globalRes.Coins > 0)
        {
            // One coin per click — helper players just click faster (cadence).
            int coins = System.Math.Min(1, globalRes.Coins);

            int deposited = InteractionLogic.DepositToLand(ctx.State, landEntity, coins, leaveOneForPlayer: false, out bool landComplete);
            globalRes.Coins -= deposited;

            if (landComplete)
            {
                var transform = ctx.State.GetComponent<Transform2D>(landEntity);
                var position = transform.Position;
                var land = ctx.State.GetComponent<LandComponent>(landEntity);
                var landType = land.Type;
                int gridX = land.Arm;
                int gridY = land.Ring;
                CooldownComponent? carry = null;
                if (ctx.State.HasComponent<CooldownComponent>(landEntity))
                    carry = ctx.State.GetComponent<CooldownComponent>(landEntity);
                LandDefinition.DeleteSignsForLand(ctx.State, landEntity);
                ctx.State.DeleteEntity(landEntity);

                CompleteLandBuilding(ctx, position, landType, gridX, gridY, carry);
                return false;
            }
            return deposited > 0;
        }
        missingResource = StateKeys.Coins;
        return false;
    }

    private static void DestroyNearbyProps(Context ctx, Vector2 position, Float radius)
    {
        Float radiusSq = radius * radius;
        var toDelete = new System.Collections.Generic.List<Entity>();
        foreach (var entity in ctx.State.Filter<PropComponent>())
        {
            var propPos = ctx.State.GetComponent<Transform2D>(entity).Position;
            var diff = propPos - position;
            if (diff.SqrMagnitude < radiusSq)
                toDelete.Add(entity);
        }
        foreach (var entity in toDelete)
            ctx.State.DeleteEntity(entity);
    }

    private bool HandleFinalStructureInteraction(Context ctx, Entity finalEntity, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;
        if (globalRes.Coins <= 0)
        {
            missingResource = StateKeys.Coins;
            return false;
        }

        ref var final = ref ctx.State.GetComponent<FinalStructureComponent>(finalEntity);

        if (final.CurrentCoins >= final.Threshold) return false;

        // One coin per click — helper players just click faster (cadence).
        int deposit = System.Math.Min(1, globalRes.Coins);
        deposit = System.Math.Min(deposit, final.Threshold - final.CurrentCoins);
        globalRes.Coins -= deposit;
        final.CurrentCoins += deposit;
        return true;
    }

    private bool HandleHelperInteraction(Context ctx, Entity helperEntity, ref GlobalResourcesComponent globalRes)
    {
        ref var helper = ref ctx.State.GetComponent<HelperComponent>(helperEntity);

        // Priority 1: If helper is waiting for pickup, collect resources from helper
        if (helper.State == HelperState.WaitingForPickup)
        {
            return PickupFromHelper(ctx, helperEntity, ref helper, ref globalRes);
        }

        // Priority 2: Give resources TO helper (seller gets milk, builder gets coins)
        if (helper.Type == HelperType.Seller && helper.State == HelperState.Idle)
        {
            // Transfer general milk from global to seller's bag
            int transferred = 0;
            int capacity = helper.BagCapacity - helper.GetBagTotal();

            while (transferred < capacity && globalRes.Milk > 0) { globalRes.Milk--; helper.BagMilk++; transferred++; }

            if (transferred > 0)
            {
                ILogger.Log($"[InteractActionService] Loaded {transferred} milk into Seller helper {helperEntity.Id}");
                return true;
            }
        }
        else if (helper.Type == HelperType.Builder && helper.State == HelperState.Idle)
        {
            // Give builder all available coins (up to bag capacity)
            int needed = helper.BagCapacity - helper.BagCoins;
            int toGive = System.Math.Max(0, System.Math.Min(needed, globalRes.Coins));
            if (toGive > 0)
            {
                globalRes.Coins -= toGive;
                helper.BagCoins += toGive;
                ILogger.Log($"[InteractActionService] Gave {toGive} coins to Builder helper {helperEntity.Id}");
                return true;
            }
        }
        else if (helper.Type == HelperType.Milker && helper.State == HelperState.Idle && helper.WantedFoodType >= 0)
        {
            // Give milker the food it needs AND the prerequisite milk products for the chain
            int foodType = helper.WantedFoodType;
            int capacity = helper.BagCapacity - helper.GetBagTotal();
            int available = globalRes.GetFood(foodType);
            int toGive = System.Math.Max(0, System.Math.Min(capacity, available));
            if (toGive > 0)
            {
                for (int i = 0; i < toGive; i++)
                    globalRes.ConsumeFood(foodType);
                switch (foodType)
                {
                    case FoodType.Grass: helper.BagGrass += toGive; break;
                    case FoodType.Carrot: helper.BagCarrot += toGive; break;
                    case FoodType.Apple: helper.BagApple += toGive; break;
                    case FoodType.Mushroom: helper.BagMushroom += toGive; break;
                }

                // Also give prerequisite milk products needed for the chain recipe
                int prereq = FoodType.PrerequisiteProduct(foodType);
                if (prereq >= 0)
                {
                    // Give enough prerequisite products (1 per 4 food, since milk is produced every 4 clicks)
                    int prereqNeeded = System.Math.Max(1, toGive / 4);
                    int prereqCapacity = helper.BagCapacity - helper.GetBagTotal();
                    int prereqAvailable = globalRes.GetMilkProduct(prereq);
                    int prereqToGive = System.Math.Min(prereqNeeded, System.Math.Min(prereqCapacity, prereqAvailable));
                    for (int i = 0; i < prereqToGive; i++)
                        globalRes.ConsumeMilkProduct(prereq);
                    helper.AddBagMilkProduct(prereq, prereqToGive);
                }

                ILogger.Log($"[InteractActionService] Gave {toGive} food (type={foodType}) to Milker helper {helperEntity.Id}");
                return true;
            }
        }

        return false;
    }

    private bool HandleFoodInteractionForHelperPlayer(Context ctx, Entity playerEntity, Entity foodEntity)
    {
        ref var hp = ref ctx.State.GetComponent<HelperPlayerComponent>(playerEntity);
        if (hp.IsBagFull()) return false;

        ref var grass = ref ctx.State.GetComponent<GrassComponent>(foodEntity);
        grass.Durability -= 1;
        switch (grass.FoodType)
        {
            case FoodType.Grass: hp.BagGrass++; break;
            case FoodType.Carrot: hp.BagCarrot++; break;
            case FoodType.Apple: hp.BagApple++; break;
            case FoodType.Mushroom: hp.BagMushroom++; break;
        }
        if (grass.Durability <= 0)
        {
            ctx.State.DeleteEntity(foodEntity);
            return false;
        }
        return true;
    }

    private bool HandleHelperPlayerInteraction(Context ctx, Entity helperPlayerEntity, ref GlobalResourcesComponent globalRes)
    {
        ref var hp = ref ctx.State.GetComponent<HelperPlayerComponent>(helperPlayerEntity);

        if (hp.HasAnyResources())
        {
            string gainedKey = "";
            if (hp.BagGrass > 0) gainedKey = StateKeys.Grass;
            else if (hp.BagCarrot > 0) gainedKey = StateKeys.Carrot;
            else if (hp.BagApple > 0) gainedKey = StateKeys.Apple;
            else if (hp.BagMushroom > 0) gainedKey = StateKeys.Mushroom;
            else if (hp.BagMilk > 0) gainedKey = StateKeys.Milk;
            else if (hp.BagCoins > 0) gainedKey = StateKeys.Coins;

            globalRes.AddFood(FoodType.Grass, hp.BagGrass);
            globalRes.AddFood(FoodType.Carrot, hp.BagCarrot);
            globalRes.AddFood(FoodType.Apple, hp.BagApple);
            globalRes.AddFood(FoodType.Mushroom, hp.BagMushroom);
            globalRes.AddMilkProduct(MilkProduct.Milk, hp.BagMilk);
            globalRes.Coins += hp.BagCoins;
            hp.ClearBag();

            if (!string.IsNullOrEmpty(gainedKey))
                ctx.State.AddComponent(ctx.Entity, new EnterStateComponent { Key = StateKeys.GainedResource, Param = gainedKey, Age = 0 });

            ILogger.Log($"[InteractActionService] Main player picked up bag from helper-player {helperPlayerEntity.Id}");
            return true;
        }

        if (hp.Type == HelperType.Seller)
        {
            int capacity = hp.BagCapacity - hp.GetBagTotal();
            int transferred = 0;
            while (transferred < capacity && globalRes.Milk > 0) { globalRes.Milk--; hp.BagMilk++; transferred++; }
            if (transferred > 0) return true;
        }
        else if (hp.Type == HelperType.Builder)
        {
            int needed = hp.BagCapacity - hp.BagCoins;
            int toGive = System.Math.Max(0, System.Math.Min(needed, globalRes.Coins));
            if (toGive > 0) { globalRes.Coins -= toGive; hp.BagCoins += toGive; return true; }
        }
        else if (hp.Type == HelperType.Milker && hp.WantedFoodType >= 0)
        {
            int foodType = hp.WantedFoodType;
            int capacity = hp.BagCapacity - hp.GetBagTotal();
            int available = globalRes.GetFood(foodType);
            int toGive = System.Math.Max(0, System.Math.Min(capacity, available));
            if (toGive > 0)
            {
                for (int i = 0; i < toGive; i++) globalRes.ConsumeFood(foodType);
                switch (foodType)
                {
                    case FoodType.Grass: hp.BagGrass += toGive; break;
                    case FoodType.Carrot: hp.BagCarrot += toGive; break;
                    case FoodType.Apple: hp.BagApple += toGive; break;
                    case FoodType.Mushroom: hp.BagMushroom += toGive; break;
                }
                int prereq = FoodType.PrerequisiteProduct(foodType);
                if (prereq >= 0)
                {
                    int prereqNeeded = System.Math.Max(1, toGive / 4);
                    int prereqCapacity = hp.BagCapacity - hp.GetBagTotal();
                    int prereqAvailable = globalRes.GetMilkProduct(prereq);
                    int prereqToGive = System.Math.Min(prereqNeeded, System.Math.Min(prereqCapacity, prereqAvailable));
                    for (int i = 0; i < prereqToGive; i++) globalRes.ConsumeMilkProduct(prereq);
                    hp.AddBagMilkProduct(prereq, prereqToGive);
                }
                return true;
            }
        }

        return false;
    }

    /// <summary>Velocity nudge applied to a player when another player interacts with them.</summary>
    private const float PlayerPushImpulse = 6.0f;

    private static void ApplyPlayerPush(Context ctx, Entity from, Entity target)
    {
        if (!ctx.State.HasComponent<Transform2D>(from)) return;
        if (!ctx.State.HasComponent<Transform2D>(target)) return;
        if (!ctx.State.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(target)) return;

        var fromPos = ctx.State.GetComponent<Transform2D>(from).Position;
        var targetPos = ctx.State.GetComponent<Transform2D>(target).Position;
        var diff = targetPos - fromPos;
        Float distSq = diff.SqrMagnitude;
        if (distSq < (Float)0.0001f) return; // perfect overlap — pick an arbitrary direction next frame

        Float dist = Float.Sqrt(distSq);
        var dir = diff / dist;
        ref var body = ref ctx.State.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(target);
        body.Velocity = body.Velocity + dir * (Float)PlayerPushImpulse;
    }

    private bool DumpHelperPlayerBagToGlobal(Context ctx, Entity helperPlayerEntity, ref GlobalResourcesComponent globalRes, out string gainedResource)
    {
        gainedResource = null;
        ref var hp = ref ctx.State.GetComponent<HelperPlayerComponent>(helperPlayerEntity);
        if (!hp.HasAnyResources()) return false;

        if (hp.BagGrass > 0) gainedResource = StateKeys.Grass;
        else if (hp.BagCarrot > 0) gainedResource = StateKeys.Carrot;
        else if (hp.BagApple > 0) gainedResource = StateKeys.Apple;
        else if (hp.BagMushroom > 0) gainedResource = StateKeys.Mushroom;
        else if (hp.BagMilk > 0) gainedResource = StateKeys.Milk;
        else if (hp.BagCoins > 0) gainedResource = StateKeys.Coins;

        globalRes.AddFood(FoodType.Grass, hp.BagGrass);
        globalRes.AddFood(FoodType.Carrot, hp.BagCarrot);
        globalRes.AddFood(FoodType.Apple, hp.BagApple);
        globalRes.AddFood(FoodType.Mushroom, hp.BagMushroom);
        globalRes.AddMilkProduct(MilkProduct.Milk, hp.BagMilk);
        globalRes.Coins += hp.BagCoins;
        hp.ClearBag();
        return true;
    }

    /// <summary>
    /// Player picks up resources from a helper that is in WaitingForPickup state.
    /// Transfers the helper's bag contents into global resources and resets helper to Idle.
    /// </summary>
    private bool PickupFromHelper(Context ctx, Entity helperEntity, ref HelperComponent helper, ref GlobalResourcesComponent globalRes)
    {
        bool pickedUp = false;
        string gainedKey = "";

        // Pick up food (from gatherer)
        if (helper.GetFoodTotal() > 0)
        {
            gainedKey = helper.BagGrass > 0 ? StateKeys.Grass
                : helper.BagCarrot > 0 ? StateKeys.Carrot
                : helper.BagApple > 0 ? StateKeys.Apple
                : helper.BagMushroom > 0 ? StateKeys.Mushroom : "";

            globalRes.AddFood(FoodType.Grass, helper.BagGrass);
            globalRes.AddFood(FoodType.Carrot, helper.BagCarrot);
            globalRes.AddFood(FoodType.Apple, helper.BagApple);
            globalRes.AddFood(FoodType.Mushroom, helper.BagMushroom);
            helper.BagGrass = 0;
            helper.BagCarrot = 0;
            helper.BagApple = 0;
            helper.BagMushroom = 0;
            pickedUp = true;
        }

        // Pick up milk products (from milker)
        if (helper.GetMilkTotal() > 0)
        {
            if (string.IsNullOrEmpty(gainedKey))
            {
                gainedKey = StateKeys.Milk;
            }

            globalRes.AddMilkProduct(MilkProduct.Milk, helper.BagMilk);
            helper.BagMilk = 0;
            helper.BagCarrotMilkshake = 0;
            helper.BagVitaminMix = 0;
            helper.BagPurplePotion = 0;
            pickedUp = true;
        }

        // Pick up coins (from seller or builder returning unused coins)
        if (helper.BagCoins > 0)
        {
            if (string.IsNullOrEmpty(gainedKey))
                gainedKey = StateKeys.Coins;

            globalRes.Coins += helper.BagCoins;
            helper.BagCoins = 0;
            pickedUp = true;
        }

        if (pickedUp)
        {
            // Show gained resource icon on player
            if (!string.IsNullOrEmpty(gainedKey))
            {
                ctx.State.AddComponent(ctx.Entity, new EnterStateComponent { Key = StateKeys.GainedResource, Param = gainedKey, Age = 0 });
                helper = ref ctx.State.GetComponent<HelperComponent>(helperEntity);
            }
            helper.State = HelperState.Idle;
            ILogger.Log($"[InteractActionService] Player picked up resources from helper {helperEntity.Id} (type={helper.Type})");
        }

        return pickedUp;
    }

    private static bool IsValidPetAssignTarget(Context ctx, Entity target)
    {
        if (target == Entity.Null) return false;
        if (ctx.State.HasComponent<HelperComponent>(target)) return true;
        if (ctx.State.HasComponent<CowComponent>(target)) return true;
        if (ctx.State.HasComponent<PlayerEntity>(target)) return true;
        return false;
    }

    private bool HandlePetInteraction(Context ctx, Entity playerEntity, Entity petEntity, ref PlayerStateComponent playerState)
    {
        ref var pet = ref ctx.State.GetComponent<HelperPetComponent>(petEntity);

        if (playerState.CarriedEntity == petEntity)
        {
            DropPetToIdle(ctx, petEntity, ref pet);
            playerState.CarriedEntity = Entity.Null;
            ILogger.Log($"[Pet] Player {playerEntity.Id} dropped pet {petEntity.Id} at idle spawn");
            return true;
        }

        if (playerState.CarriedEntity != Entity.Null)
        {
            if (ctx.State.HasComponent<HelperPetComponent>(playerState.CarriedEntity))
            {
                ref var prev = ref ctx.State.GetComponent<HelperPetComponent>(playerState.CarriedEntity);
                DropPetToIdle(ctx, playerState.CarriedEntity, ref prev);
            }
            playerState.CarriedEntity = Entity.Null;
            pet = ref ctx.State.GetComponent<HelperPetComponent>(petEntity);
        }

        pet.State = PetState.Carried;
        pet.FollowTarget = playerEntity;
        pet.AssignedTo = Entity.Null;
        playerState.CarriedEntity = petEntity;
        ILogger.Log($"[Pet] Player {playerEntity.Id} picked up pet {petEntity.Id}");
        return true;
    }

    private bool HandlePetAssign(Context ctx, Entity playerEntity, Entity targetEntity, ref PlayerStateComponent playerState)
    {
        var petEntity = playerState.CarriedEntity;
        if (petEntity == Entity.Null) return false;
        if (!ctx.State.HasComponent<HelperPetComponent>(petEntity)) return false;

        ref var pet = ref ctx.State.GetComponent<HelperPetComponent>(petEntity);
        pet.State = PetState.Assigned;
        pet.AssignedTo = targetEntity;
        pet.FollowTarget = targetEntity;
        playerState.CarriedEntity = Entity.Null;
        ILogger.Log($"[Pet] Player {playerEntity.Id} assigned pet {petEntity.Id} to target {targetEntity.Id}");
        return true;
    }

    private static void DropPetToIdle(Context ctx, Entity petEntity, ref HelperPetComponent pet)
    {
        pet.State = PetState.Idle;
        pet.FollowTarget = Entity.Null;
        pet.AssignedTo = Entity.Null;
        if (ctx.State.HasComponent<Transform2D>(petEntity))
        {
            ref var t = ref ctx.State.GetComponent<Transform2D>(petEntity);
            t.Position = new Vector2((Float)pet.IdleSpawnX, (Float)pet.IdleSpawnY);
        }
        if (ctx.State.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(petEntity))
        {
            ref var body = ref ctx.State.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(petEntity);
            body.Velocity = Vector2.Zero;
        }
    }

    private static string FoodTypeToKey(int foodType) => foodType switch
    {
        FoodType.Grass => StateKeys.Grass,
        FoodType.Carrot => StateKeys.Carrot,
        FoodType.Apple => StateKeys.Apple,
        FoodType.Mushroom => StateKeys.Mushroom,
        _ => StateKeys.Food
    };

    private static string MilkProductToKey(int milkProduct) => milkProduct switch
    {
        MilkProduct.Milk => StateKeys.Milk,
        MilkProduct.CarrotMilkshake => StateKeys.CarrotMilkshake,
        MilkProduct.VitaminMix => StateKeys.VitaminMix,
        MilkProduct.PurplePotion => StateKeys.PurplePotion,
        _ => StateKeys.Milk
    };

    private Entity GetGlobalResourcesEntity(Context ctx)
    {
        foreach (var entity in ctx.State.Filter<GlobalResourcesComponent>())
        {
            return entity;
        }
        return Entity.Null;
    }

    /// <summary>
    /// Shared logic for completing a land purchase — builds the structure and spawns neighbors.
    /// Called by both player interaction and builder helper.
    /// The land entity should already be deleted before calling this.
    /// </summary>
    public static void CompleteLandBuilding(Context ctx, Vector2 position, LandType landType, int gridX, int gridY, CooldownComponent? carryCooldown = null)
    {
        // Destroy nearby props to clear space
        DestroyNearbyProps(ctx, position, 4f);

        Entity built = Entity.Null;
        switch (landType)
        {
            case LandType.LoveHouse:
                built = LoveHouseDefinition.Create(ctx, position);
                break;
            case LandType.SellPoint:
                built = SellPointDefinition.Create(ctx, position);
                break;
            case LandType.FinalStructure:
                built = FinalStructureDefinition.Create(ctx, position, 0);
                break;
            case LandType.CarrotFarm:
                built = CarrotFarmDefinition.Create(ctx, position);
                break;
            case LandType.AppleOrchard:
                built = AppleOrchardDefinition.Create(ctx, position);
                break;
            case LandType.MushroomCave:
                built = MushroomCaveDefinition.Create(ctx, position);
                break;
            case LandType.HelperAssistant:
                {
                    built = HelperAssistantDefinition.Create(ctx, position);
                    // Offset the pet so it doesn't sit on top of the cat-house — player needs
                    // to click the pet itself to carry it.
                    var petPos = position + new Vector2((Float)1.5f, (Float)0);
                    var assistant = HelperPetDefinition.CreateIdle(ctx, petPos, HelperType.Assistant);
                    ctx.State.AddComponent(assistant, new BreedBornComponent());
                    var gt1 = ctx.State.GetCustomData<IGameTime>();
                    ILogger.Log($"[Building] HelperAssistant built at {(gt1 != null ? gt1.CurrentTick / 60f / 60f : -1):F1}m — pet idling, click to pick up");
                    break;
                }
            case LandType.Warehouse:
                built = WarehouseDefinition.Create(ctx, position);
                break;
            case LandType.Library:
                built = LibraryDefinition.Create(ctx, position);
                break;
            case LandType.PlayerHouse:
                built = PlayerHouseDefinition.Create(ctx, position);
                break;
            case LandType.Decoration:
                built = DecorationDefinition.Create(ctx, position);
                break;
            case LandType.Smithy:
                built = SmithyDefinition.Create(ctx, position);
                break;
            default:
                built = HouseDefinition.Create(ctx, position);
                break;
        }

        // Inherit cooldown from the demolished plot, if any. The new building starts at MaxTicks
        // (full cooldown) so a quick demolish-rebuild cycle can't be used to skip cycles.
        if (carryCooldown.HasValue && built != Entity.Null && carryCooldown.Value.MaxTicks > 0)
        {
            ctx.State.AddComponent(built, new CooldownComponent
            {
                MaxTicks = carryCooldown.Value.MaxTicks,
                TicksRemaining = carryCooldown.Value.MaxTicks,
                Unit = carryCooldown.Value.Unit,
            });
        }

        StarGrid.SpawnNeighbors(ctx, gridX, gridY);
    }

    /// <summary>
    /// Returns a building info param key for the given entity, or null if it's not a known building.
    /// Used to show info popups when the player interacts with a building that has no primary action.
    /// </summary>
    /// <summary>True when <paramref name="e"/> has an active CooldownComponent (any unit). Read-only check.</summary>
    public static bool IsOnCooldown(Context ctx, Entity e)
    {
        if (!ctx.State.HasComponent<CooldownComponent>(e)) return false;
        return ctx.State.GetComponent<CooldownComponent>(e).TicksRemaining > 0;
    }

    private static string GetBuildingInfoKey(Context ctx, Entity entity)
    {
        if (ctx.State.HasComponent<SellPointComponent>(entity)) return StateKeys.InfoSellPoint;
        if (ctx.State.HasComponent<HouseComponent>(entity)) return StateKeys.InfoHouse;
        if (ctx.State.HasComponent<LoveHouseComponent>(entity)) return StateKeys.InfoLoveHouse;
        if (ctx.State.HasComponent<CarrotFarmComponent>(entity)) return StateKeys.InfoCarrotFarm;
        if (ctx.State.HasComponent<AppleOrchardComponent>(entity)) return StateKeys.InfoAppleOrchard;
        if (ctx.State.HasComponent<MushroomCaveComponent>(entity)) return StateKeys.InfoMushroomCave;
        if (ctx.State.HasComponent<HelperAssistantComponent>(entity)) return StateKeys.InfoHelperAssistant;
        if (ctx.State.HasComponent<DecorationComponent>(entity)) return StateKeys.InfoDecoration;
        if (ctx.State.HasComponent<WarehouseComponent>(entity)) return StateKeys.InfoWarehouse;
        return null;
    }

    /// <summary>
    /// Any entity carrying a building tag — used by the hammer to determine "is this a building I can demolish".
    /// Smithy is included on purpose: hammers destroy hammers' source too.
    /// </summary>
    private static bool IsDemolishableBuilding(Context ctx, Entity e)
    {
        return ctx.State.HasComponent<HouseComponent>(e)
            || ctx.State.HasComponent<LoveHouseComponent>(e)
            || ctx.State.HasComponent<SellPointComponent>(e)
            || ctx.State.HasComponent<FinalStructureComponent>(e)
            || ctx.State.HasComponent<CarrotFarmComponent>(e)
            || ctx.State.HasComponent<AppleOrchardComponent>(e)
            || ctx.State.HasComponent<MushroomCaveComponent>(e)
            || ctx.State.HasComponent<HelperAssistantComponent>(e)
            || ctx.State.HasComponent<WarehouseComponent>(e)
            || ctx.State.HasComponent<LibraryComponent>(e)
            || ctx.State.HasComponent<PlayerHouseComponent>(e)
            || ctx.State.HasComponent<DecorationComponent>(e)
            || ctx.State.HasComponent<SmithyComponent>(e);
    }

    private static LandType ResolveBuildingType(Context ctx, Entity e)
    {
        if (ctx.State.HasComponent<HouseComponent>(e)) return LandType.House;
        if (ctx.State.HasComponent<LoveHouseComponent>(e)) return LandType.LoveHouse;
        if (ctx.State.HasComponent<SellPointComponent>(e)) return LandType.SellPoint;
        if (ctx.State.HasComponent<FinalStructureComponent>(e)) return LandType.FinalStructure;
        if (ctx.State.HasComponent<CarrotFarmComponent>(e)) return LandType.CarrotFarm;
        if (ctx.State.HasComponent<AppleOrchardComponent>(e)) return LandType.AppleOrchard;
        if (ctx.State.HasComponent<MushroomCaveComponent>(e)) return LandType.MushroomCave;
        if (ctx.State.HasComponent<HelperAssistantComponent>(e)) return LandType.HelperAssistant;
        if (ctx.State.HasComponent<WarehouseComponent>(e)) return LandType.Warehouse;
        if (ctx.State.HasComponent<LibraryComponent>(e)) return LandType.Library;
        if (ctx.State.HasComponent<PlayerHouseComponent>(e)) return LandType.PlayerHouse;
        if (ctx.State.HasComponent<DecorationComponent>(e)) return LandType.Decoration;
        if (ctx.State.HasComponent<SmithyComponent>(e)) return LandType.Smithy;
        return LandType.House;
    }

    /// <summary>
    /// Refund = sticker price (Threshold) of the destroyed building. Recomputed from grid
    /// coords + StarGrid since the LandComponent is gone after build completion.
    /// </summary>
    private static int ComputeDemolishRefund(LandType type, int gx, int gy)
    {
        int gridDist = System.Math.Max(1, System.Math.Abs(gx) + System.Math.Abs(gy));
        int pm = StarGrid.GetPriceMultiplier(type);
        return pm < 0
            ? gridDist * StarGrid.GetEraMultiplier(gridDist) * Template.Shared.GameData.Balance.Build.BasePriceMultiplier / 4
            : gridDist * StarGrid.GetEraMultiplier(gridDist) * pm * Template.Shared.GameData.Balance.Build.BasePriceMultiplier;
    }

    private static void GridCoordsFromPosition(Vector2 position, out int gx, out int gy)
    {
        // Round nearest grid step. StarGrid.GridStep is the spacing.
        float step = StarGrid.GridStep;
        gx = (int)System.Math.Round((double)((float)position.X / step));
        gy = (int)System.Math.Round((double)((float)position.Y / step));
    }

    /// <summary>
    /// Demolish a completed building. Refunds CurrentCoins (= threshold) to global coins, recreates
    /// the plot as a cycling Land. Cooldown comes from the victim's CooldownComponent.MaxTicks
    /// (and unit) — if absent, the recreated plot has no demolish cooldown.
    /// </summary>
    private static void DemolishBuilding(Context ctx, Entity buildingEntity, ref GlobalResourcesComponent globalRes)
    {
        if (!ctx.State.HasComponent<Transform2D>(buildingEntity)) return;
        var pos = ctx.State.GetComponent<Transform2D>(buildingEntity).Position;
        GridCoordsFromPosition(pos, out int gx, out int gy);

        var type = ResolveBuildingType(ctx, buildingEntity);
        int refund = ComputeDemolishRefund(type, gx, gy);
        globalRes.Coins += refund;

        // Capture cooldown spec from the victim before deletion.
        int cdMax = 0;
        int cdUnit = CooldownUnit.Ticks;
        if (ctx.State.HasComponent<CooldownComponent>(buildingEntity))
        {
            var cd = ctx.State.GetComponent<CooldownComponent>(buildingEntity);
            cdMax = cd.MaxTicks;
            cdUnit = cd.Unit;
        }

        // Detach any cow assigned to this house — release into the wild (no follow target).
        if (ctx.State.HasComponent<HouseComponent>(buildingEntity))
        {
            var house = ctx.State.GetComponent<HouseComponent>(buildingEntity);
            if (house.CowId != Entity.Null && ctx.State.HasComponent<CowComponent>(house.CowId))
            {
                ref var cow = ref ctx.State.GetComponent<CowComponent>(house.CowId);
                cow.HouseId = Entity.Null;
            }
        }

        // Wipe any signs that reference this entity (price sign, role sign) before deletion.
        Definitions.LandDefinition.DeleteSignsForLand(ctx.State, buildingEntity);
        ctx.State.DeleteEntity(buildingEntity);

        // Recreate the cycling plot at the same grid cell. Threshold is recomputed inside.
        int threshold = ComputeDemolishRefund(type, gx, gy);
        var plot = Definitions.LandDefinition.Create(ctx, pos, threshold, type, gx, gy);
        if (cdMax > 0)
        {
            ctx.State.AddComponent(plot, new CooldownComponent
            {
                MaxTicks = cdMax,
                TicksRemaining = cdMax,
                Unit = cdUnit,
            });
        }

        ILogger.Log($"[Demolish] Razed {type} at ({gx},{gy}); refund={refund} coins; cooldownMax={cdMax} unit={cdUnit}");
    }

    private static void SpawnAndCarryHammer(Context ctx, ref PlayerStateComponent playerState, Vector2 position)
    {
        var hammer = Definitions.HammerDefinition.Create(ctx, position);
        ref var h = ref ctx.State.GetComponent<HammerComponent>(hammer);
        h.State = HammerState.Carried;
        playerState.CarriedEntity = hammer;
    }

    private static void DropCarriedHammerAt(Context ctx, ref PlayerStateComponent playerState, Vector2 position)
    {
        var hammer = playerState.CarriedEntity;
        if (hammer == Entity.Null) return;
        if (!ctx.State.HasComponent<HammerComponent>(hammer)) return;
        ref var h = ref ctx.State.GetComponent<HammerComponent>(hammer);
        h.State = HammerState.Idle;
        if (ctx.State.HasComponent<Transform2D>(hammer))
        {
            ref var t = ref ctx.State.GetComponent<Transform2D>(hammer);
            t.Position = position;
        }
        playerState.CarriedEntity = Entity.Null;
    }
}
