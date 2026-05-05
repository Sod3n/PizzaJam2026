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

// Thin dispatcher.
//
// Handles cross-cutting gates that don't fit the per-feature ISystem pattern:
//   - in-state taps (Milking / Breed click → dedicated handlers)
//   - cooldown gate
//   - hammer carry (drop in air, drop on click of self, demolish on building)
//   - pet carry (assign to clicked target / self)
//
// Everything else is delegated by tagging the player with InteractRequestComponent. Per-feature
// systems (HouseMilkSystem, CowClickSystem, FoodSignSystem, etc.) consume the marker, check
// their own preconditions, and execute. InteractFallbackSystem cleans up unclaimed markers and
// emits BuildingInfo popups for known buildings.
public class InteractActionService : ActionService<InteractAction, PlayerEntity>
{
    protected override void ExecuteProcess(InteractAction action, ref PlayerEntity playerComp, Context ctx)
    {
        Entity playerEntity = ctx.Entity;

        if (playerComp.UserId != action.UserId) return;
        if (!ctx.State.HasComponent<PlayerStateComponent>(playerEntity)) return;
        if (!ctx.State.HasComponent<StateComponent>(playerEntity)) return;

        ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
        ref var sc = ref ctx.State.GetComponent<StateComponent>(playerEntity);

        // In-state taps
        if (sc.Key == StateKeys.Milking && sc.Phase == StatePhase.Active && sc.IsEnabled)
        {
            HandleMilkingClick(ctx, playerEntity, ref playerState, ref sc);
            return;
        }
        if (sc.Key == StateKeys.Breed && sc.Phase == StatePhase.Active && sc.IsEnabled)
        {
            HandleBreedingClick(ctx, playerEntity, ref playerState, ref sc);
            return;
        }
        if (sc.IsEnabled) return;

        Entity nearestTarget = FindNearestFromZone(ctx, playerEntity, ref playerState);
        bool isHelperPlayer = ctx.State.HasComponent<HelperPlayerComponent>(playerEntity);

        // Empty-air clicks: carry-only behaviors
        if (nearestTarget == Entity.Null)
        {
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
            if (!isHelperPlayer && playerState.CarriedEntity != Entity.Null
                && HandlePetAssign(ctx, playerEntity, playerEntity, ref playerState))
            {
                ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
            }
            return;
        }

        bool carryingHammer = playerState.CarriedEntity != Entity.Null
                              && ctx.State.HasComponent<HammerComponent>(playerState.CarriedEntity);

        // Cooldown gate (hammer-related interactions are exempt — demolishing a cooled-down building is fine)
        if (IsOnCooldown(ctx, nearestTarget)
            && !ctx.State.HasComponent<HammerComponent>(nearestTarget)
            && !carryingHammer)
        {
            ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.NotEnoughResource, Param = "cooldown", Age = 0 });
            return;
        }

        // Hammer carry: drop self / demolish target
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
            Entity grEntity = InteractFeedback.GetGlobalResourcesEntity(ctx.State);
            if (grEntity != Entity.Null)
            {
                ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(grEntity);
                var hammer = playerState.CarriedEntity;
                DemolishBuilding(ctx, nearestTarget, ref globalRes);
                playerState.CarriedEntity = Entity.Null;
                if (ctx.State.HasComponent<HammerComponent>(hammer))
                    ctx.State.DeleteEntity(hammer);
                ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "demolish", Age = 0 });
            }
            return;
        }

        // Pet carry: assign to clicked target
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

        // Normal dispatch — per-feature systems consume the marker.
        ctx.State.AddComponent(playerEntity, new InteractRequestComponent { Target = nearestTarget });
    }

    // ─────────────────────────────── In-state taps ───────────────────────────────

    private void HandleMilkingClick(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        var cowEntity = playerState.InteractionTarget;
        if (cowEntity == Entity.Null || !ctx.State.HasComponent<CowComponent>(cowEntity)) return;

        Entity globalResEntity = InteractFeedback.GetGlobalResourcesEntity(ctx.State);
        if (globalResEntity == Entity.Null) return;
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

    private static Entity FindNearestFromZone(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState)
        => InteractionLogic.FindNearestInteractableInZone(ctx.State, playerEntity, playerState.InteractionZone);

    // ─────────────────────────────── Sign management (used by feature systems and others) ───────────────────────────────

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

    // ─────────────────────────────── Pet carry pre-dispatch helpers ───────────────────────────────

    private static bool IsValidPetAssignTarget(Context ctx, Entity target)
    {
        if (target == Entity.Null) return false;
        if (ctx.State.HasComponent<HelperComponent>(target)) return true;
        if (ctx.State.HasComponent<CowComponent>(target)) return true;
        if (ctx.State.HasComponent<PlayerEntity>(target)) return true;
        return false;
    }

    private static bool HandlePetAssign(Context ctx, Entity playerEntity, Entity targetEntity, ref PlayerStateComponent playerState)
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

    // ─────────────────────────────── Land building (used by feature systems and helpers) ───────────────────────────────

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

    /// <summary>
    /// Shared logic for completing a land purchase — builds the structure and spawns neighbors.
    /// Called by both player interaction and builder helper. The land entity should already be
    /// deleted before calling this.
    /// </summary>
    public static void CompleteLandBuilding(Context ctx, Vector2 position, LandType landType, int gridX, int gridY, CooldownComponent? carryCooldown = null)
    {
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

    // ─────────────────────────────── Cooldown utility (used everywhere) ───────────────────────────────

    /// <summary>True when <paramref name="e"/> has an active CooldownComponent (any unit). Read-only check.</summary>
    public static bool IsOnCooldown(Context ctx, Entity e)
    {
        if (!ctx.State.HasComponent<CooldownComponent>(e)) return false;
        return ctx.State.GetComponent<CooldownComponent>(e).TicksRemaining > 0;
    }

    // ─────────────────────────────── Hammer / demolish pre-dispatch helpers ───────────────────────────────

    private static bool IsDemolishableBuilding(Context ctx, Entity e)
    {
        return ctx.State.HasComponent<BuildingComponent>(e);
    }

    private static LandType ResolveBuildingType(Context ctx, Entity e)
    {
        if (ctx.State.HasComponent<BuildingComponent>(e))
            return ctx.State.GetComponent<BuildingComponent>(e).Type;
        return LandType.House;
    }

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
        float step = StarGrid.GridStep;
        gx = (int)System.Math.Round((double)((float)position.X / step));
        gy = (int)System.Math.Round((double)((float)position.Y / step));
    }

    private static void DemolishBuilding(Context ctx, Entity buildingEntity, ref GlobalResourcesComponent globalRes)
    {
        if (!ctx.State.HasComponent<Transform2D>(buildingEntity)) return;
        var pos = ctx.State.GetComponent<Transform2D>(buildingEntity).Position;
        GridCoordsFromPosition(pos, out int gx, out int gy);

        var type = ResolveBuildingType(ctx, buildingEntity);
        int refund = ComputeDemolishRefund(type, gx, gy);
        globalRes.Coins += refund;

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

        Definitions.LandDefinition.DeleteSignsForLand(ctx.State, buildingEntity);
        ctx.State.DeleteEntity(buildingEntity);

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
