using Deterministic.GameFramework.ECS;
using Godot;
using R3;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class FoodSignView
{
    private static readonly string[] FoodIconPaths =
    {
        "res://sprites/export/icons/Grass_/1.png",
        "res://sprites/export/icons/Carrot_/1.png",
        "res://sprites/export/icons/Apply_/1.png",
        "res://sprites/export/icons/Mashroom/1.png",
    };

    partial void OnSpawned(FoodSignViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var sprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("FoodIcon");
        if (sprite == null) return;

        // Set initial icon
        UpdateFoodIcon(sprite, vm.FoodSign.FoodSign.SelectedFood.CurrentValue);

        // React to changes
        vm.FoodSign.FoodSign.SelectedFood.Subscribe(foodType =>
        {
            Callable.From(() => UpdateFoodIcon(sprite, foodType)).CallDeferred();
        }).AddTo(vm.Disposables);

        var heartSprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("HeartIcon");
        if (heartSprite != null)
            SetupHeartIcon(vm, heartSprite);
    }

    partial void OnDespawned(FoodSignViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }

    private static void UpdateFoodIcon(AnimatedSprite3D sprite, int foodType)
    {
        if (foodType >= 0 && foodType < FoodIconPaths.Length)
        {
            var texture = GD.Load<Texture2D>(FoodIconPaths[foodType]);
            if (texture != null)
            {
                var frames = new SpriteFrames();
                frames.AddAnimation("default");
                frames.AddFrame("default", texture);
                sprite.SpriteFrames = frames;
                sprite.Animation = "default";
                sprite.Frame = 0;
            }
        }
    }

    private static void SetupHeartIcon(FoodSignViewModel vm, AnimatedSprite3D heartSprite)
    {
        heartSprite.Visible = false;

        var heartTexture = GD.Load<Texture2D>("res://sprites/heart.png");
        var brokenHeartTexture = GD.Load<Texture2D>("res://sprites/broken-heart.png");

        // -1 means no cow assigned
        var cowPreferredFood = new ReactiveProperty<int>(-1);
        vm.Disposables.Add(cowPreferredFood);

        cowPreferredFood
            .CombineLatest(vm.FoodSign.FoodSign.SelectedFood, (pref, selected) => (pref, selected))
            .Subscribe(pair =>
            {
                Callable.From(() =>
                {
                    if (pair.pref < 0)
                    {
                        heartSprite.Visible = false;
                        return;
                    }
                    var texture = pair.selected == pair.pref ? heartTexture : brokenHeartTexture;
                    if (texture != null)
                        SetSpriteTexture(heartSprite, texture);
                    heartSprite.Visible = true;
                }).CallDeferred();
            }).AddTo(vm.Disposables);

        var houseId = vm.FoodSign.FoodSign.HouseId.CurrentValue;
        if (houseId == Entity.Null) return;
        if (!EntityViewModel.EntityViewModels.TryGetValue(houseId, out var houseVmBase)) return;
        if (houseVmBase is not HouseViewModel houseVm) return;

        houseVm.House.House.CowId.Subscribe(cowId =>
        {
            if (cowId != Entity.Null && EntityViewModel.EntityViewModels.TryGetValue(cowId, out var cowVmBase) && cowVmBase is CowViewModel cowVm)
            {
                cowVm.Cow.Cow.PreferredFood.Subscribe(pref =>
                {
                    cowPreferredFood.Value = pref;
                }).AddTo(vm.Disposables);
            }
            else
            {
                cowPreferredFood.Value = -1;
            }
        }).AddTo(vm.Disposables);
    }

    private static void SetSpriteTexture(AnimatedSprite3D sprite, Texture2D texture)
    {
        var frames = new SpriteFrames();
        frames.AddAnimation("default");
        frames.AddFrame("default", texture);
        sprite.SpriteFrames = frames;
        sprite.Animation = "default";
        sprite.Frame = 0;
    }
}
