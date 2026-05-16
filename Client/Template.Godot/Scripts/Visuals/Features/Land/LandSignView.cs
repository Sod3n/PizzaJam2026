using Deterministic.GameFramework.ECS;
using Godot;
using R3;
using Template.Godot.GameResources;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class LandSignView
{
    private const string IconSetPath = "res://Resources/LandTypeIcons.tres";
    private static Resource _iconSet;

    private static Resource GetIconSet()
        => _iconSet ??= ResourceLoader.Load<Resource>(IconSetPath);

    partial void OnSpawned(LandSignViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var iconSprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("TypeIcon");
        if (iconSprite == null) return;

        // Cache the original frame texture size so we can rescale when swapping textures
        float baseSize = 0;
        var origTex = iconSprite.SpriteFrames?.GetFrameTexture("default", 0);
        if (origTex != null)
            baseSize = Mathf.Max(origTex.GetWidth(), origTex.GetHeight());
        var basePixelSize = iconSprite.PixelSize;

        vm.LandSign.LandSign.SelectedType.Subscribe(landType =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(iconSprite)) return;
                var iconSet = GetIconSet();
                if (iconSet == null || !LandTypeIcons.TryGet(iconSet, landType, out var texture)) return;
                if (texture == null) return;

                var frames = new SpriteFrames();
                frames.AddAnimation("default");
                frames.AddFrame("default", texture);
                iconSprite.SpriteFrames = frames;
                iconSprite.Animation = "default";
                iconSprite.Frame = 0;

                float newSize = Mathf.Max(texture.GetWidth(), texture.GetHeight());
                if (newSize > 0 && baseSize > 0)
                    iconSprite.PixelSize = basePixelSize * (baseSize / newSize);
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        // Selected-build badge: appears once the player has invested coins into the linked land.
        // Cycling stops at that point (see HandleLandSignInteraction), so the badge signals
        // "this is locked in".
        var badge = visualNode.GetNodeOrNull<Sprite3D>("SelectedBadge");
        var landId = vm.LandSign.LandSign.LandId.CurrentValue;
        if (badge != null && landId != Entity.Null
            && EntityViewModel.EntityViewModels.TryGetValue(landId, out var landBase)
            && landBase is LandViewModel landVm)
        {
            var origScale = badge.Scale;
            bool wasSelected = false;
            landVm.Land.Land.CurrentCoins.Subscribe(coins =>
            {
                bool selected = coins > 0;
                if (selected == wasSelected) return;
                wasSelected = selected;
                Callable.From(() =>
                {
                    if (!IsInstanceValid(badge)) return;
                    if (selected)
                    {
                        badge.Visible = true;
                        badge.Scale = Vector3.Zero;
                        var tween = badge.CreateTween();
                        tween.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
                        tween.TweenProperty(badge, "scale", origScale * 1.3f, 0.2f);
                        tween.Chain().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut)
                            .TweenProperty(badge, "scale", origScale, 0.12f);
                    }
                    else
                    {
                        badge.Visible = false;
                        badge.Scale = origScale;
                    }
                }).CallDeferred();
            }).AddTo(vm.Disposables);
        }
    }

    partial void OnDespawned(LandSignViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
