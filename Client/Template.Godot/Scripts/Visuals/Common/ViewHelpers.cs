using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Godot;
using R3;
using Template.Shared.Components;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

public static class ViewHelpers
{
    private static readonly Texture2D HeartTexture = GD.Load<Texture2D>("res://sprites/heart.png");
    private static readonly Texture2D BrokenHeartTexture = GD.Load<Texture2D>("res://sprites/broken-heart.png");
    private static readonly System.Random HeartRng = new();

    /// <summary>
    /// Register an entity's Transform2D position for view-layer smoothing.
    /// Frame-rate-independent exponential smoothing via <see cref="ViewSmoother"/>:
    /// reads ECS state each render frame, writes only to the Godot node.
    /// Views are spawned after <c>GameManager.OnGameStarted</c>, by which point
    /// <see cref="ViewSmoothingManager.Smoother"/> is always attached.
    /// </summary>
    public static void SetupPositionTween(EntityViewModel vm, Node3D visualNode)
    {
        vm.Disposables.Add(
            ViewSmoothingManager.Smoother.TrackPosition3D(vm.Entity, visualNode, tau: 0.08f));
    }

    public static void SetupInteractAnimation(EntityViewModel vm, Node3D visualNode, Node3D animateNode = null, bool pivotAtNodeOrigin = false, float strengthMultiplier = 1f)
    {
        EntityViewModel.EntityVisualNodes[vm.Entity.Id] = visualNode;
        Disposable.Create(() => EntityViewModel.EntityVisualNodes.Remove(vm.Entity.Id)).AddTo(vm.Disposables);

        SetupNotEnoughResource(vm, visualNode);
        SetupGainedResource(vm, visualNode);
        animateNode ??= visualNode;
        vm.OnInteract.Subscribe(param =>
        {
            Callable.From(() =>
            {
                if (!Node.IsInstanceValid(animateNode)) return;
                if (animateNode.HasMeta("scale_tween") && animateNode.GetMeta("scale_tween").Obj is Tween tw)
                    tw.SetSpeedScale(100000f);

                var tween = animateNode.CreateTween();
                Vector3 origScale;
                if (animateNode.HasMeta("orig_scale")) origScale = animateNode.GetMeta("orig_scale").AsVector3();
                else { origScale = animateNode.Scale; animateNode.SetMeta("orig_scale", origScale); }
                float widen = 0.2f * strengthMultiplier;
                float flatten = 0.15f * strengthMultiplier;
                var squishScale = new Vector3(origScale.X * (1f + widen), origScale.Y * (1f - flatten), origScale.Z);
                tween.SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);

                if (pivotAtNodeOrigin)
                {
                    // animateNode's local origin is already authored at the foot (e.g. ScaleAnchor)
                    // — skip AABB-based bottom compensation and position tweening entirely.
                    tween.TweenProperty(animateNode, "scale", squishScale, 0.1f);
                    tween.TweenProperty(animateNode, "scale", origScale, 0.1f);
                }
                else
                {
                    Vector3 origPos;
                    if (animateNode.HasMeta("orig_pos")) origPos = animateNode.GetMeta("orig_pos").AsVector3();
                    else { origPos = animateNode.Position; animateNode.SetMeta("orig_pos", origPos); }
                    // Squish pivots at the bottom of the visual's AABB instead of the node origin
                    // so the object squashes onto the ground instead of pinching at its center.
                    float bottomY = animateNode.HasMeta("squish_bottom_y")
                        ? (float)animateNode.GetMeta("squish_bottom_y").AsSingle()
                        : ComputeAndCacheBottomY(animateNode);
                    // When scaleY shrinks from origScaleY → newScaleY, a point at local Y=bottomY moves
                    // up by (origScaleY - newScaleY) * |bottomY|. Push the node down by the same amount
                    // so the bottom stays put.
                    float yShift = (origScale.Y - squishScale.Y) * bottomY;
                    var squishPos = new Vector3(origPos.X, origPos.Y + yShift, origPos.Z);
                    tween.TweenProperty(animateNode, "scale", squishScale, 0.1f);
                    tween.Parallel().TweenProperty(animateNode, "position", squishPos, 0.1f);
                    tween.TweenProperty(animateNode, "scale", origScale, 0.1f);
                    tween.Parallel().TweenProperty(animateNode, "position", origPos, 0.1f);
                }

                animateNode.SetMeta("scale_tween", tween);

                // Only show heart blast when milk is actually produced (every 4th click)
                if (param != "milk_fail")
                    SpawnHeartBlast(visualNode, vm.Entity);
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    /// <summary>
    /// Walk <paramref name="root"/>'s descendants for VisualInstance3D children, union their
    /// global AABBs, then convert the min-Y back into <paramref name="root"/>'s local space.
    /// Cached as the "squish_bottom_y" meta on the root so it's only paid once.
    /// </summary>
    private static float ComputeAndCacheBottomY(Node3D root)
    {
        bool found = false;
        float globalMinY = 0f;
        WalkVisuals(root, ref found, ref globalMinY);
        float bottomY;
        if (!found)
        {
            bottomY = -0.5f; // sensible default for ~1-unit-tall pivot-at-center assets
        }
        else
        {
            var localPoint = root.GlobalTransform.AffineInverse() * new Vector3(0f, globalMinY, 0f);
            bottomY = localPoint.Y;
        }
        root.SetMeta("squish_bottom_y", bottomY);
        return bottomY;
    }

    private static void WalkVisuals(Node node, ref bool found, ref float globalMinY)
    {
        if (node is VisualInstance3D vi)
        {
            var aabb = vi.GetAabb();
            // Transform the 8 AABB corners through vi's global transform; take min Y.
            var gt = vi.GlobalTransform;
            for (int corner = 0; corner < 8; corner++)
            {
                var local = new Vector3(
                    (corner & 1) == 0 ? aabb.Position.X : aabb.End.X,
                    (corner & 2) == 0 ? aabb.Position.Y : aabb.End.Y,
                    (corner & 4) == 0 ? aabb.Position.Z : aabb.End.Z);
                float gy = (gt * local).Y;
                if (!found || gy < globalMinY) { globalMinY = gy; found = true; }
            }
        }
        foreach (var child in node.GetChildren(true))
            WalkVisuals(child, ref found, ref globalMinY);
    }

    public static (Node3D flipPivot, Node3D characterNode) SetupFlipPivot(Node3D visualNode)
    {
        var characterNode = visualNode.GetNodeOrNull<Node3D>("Character");
        var flipPivot = new Node3D { Name = "FlipPivot" };
        if (characterNode != null)
        {
            var charTransform = characterNode.Transform;
            visualNode.RemoveChild(characterNode);
            visualNode.AddChild(flipPivot);
            flipPivot.Transform = charTransform;
            characterNode.Transform = global::Godot.Transform3D.Identity;
            flipPivot.AddChild(characterNode);
        }
        return (flipPivot, characterNode);
    }

    public static void SetupMovementAnimation(EntityViewModel vm, R3.ReadOnlyReactiveProperty<Deterministic.GameFramework.Types.Vector2> velocity, Node3D flipPivot, Node3D characterNode, bool invertFlip = false)
    {
        velocity.Subscribe(v =>
        {
            Callable.From(() =>
            {
                float speedSq = (float)v.X * (float)v.X + (float)v.Y * (float)v.Y;
                bool isMoving = speedSq > 1f; // ignore tiny velocities from ORCA/separation
                characterNode?.SetDeferred("enable_bounce", isMoving);
                float vx = invertFlip ? -(float)v.X : (float)v.X;
                if (vx < 0)
                    flipPivot.Scale = new GVector3(-Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
                else if (vx > 0)
                    flipPivot.Scale = new GVector3(Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    public static void PlayAppear(Node3D node, float duration = 0.5f)
    {
        node.RotationDegrees = new GVector3(-60f, node.RotationDegrees.Y, node.RotationDegrees.Z);
        var tween = node.CreateTween();
        tween.TweenProperty(node, "rotation_degrees:x", 0f, duration)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
    }

    public static void PlayDisappear(Node3D node, float duration = 0.5f, bool freeAfter = true)
    {
        var baseScale = node.Scale;
        var basePos = node.Position;
        float squashTime = Mathf.Min(0.12f, duration * 0.35f);
        float fallTime = Mathf.Max(0.05f, duration - squashTime);

        // Relaxed squash — gentler vertical compression than before.
        var squashScale = new GVector3(baseScale.X * 1.15f, baseScale.Y * 0.7f, baseScale.Z * 1.15f);
        float bottomY = node.HasMeta("squish_bottom_y")
            ? node.GetMeta("squish_bottom_y").AsSingle()
            : ComputeAndCacheBottomY(node);
        float yShift = (baseScale.Y - squashScale.Y) * bottomY;
        var squashPos = new GVector3(basePos.X, basePos.Y + yShift, basePos.Z);

        var tween = node.CreateTween();
        tween.TweenProperty(node, "scale", squashScale, squashTime)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(node, "position", squashPos, squashTime)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(node, "rotation_degrees:x", -60f, fallTime)
            .SetDelay(squashTime)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.In);
        if (freeAfter)
            tween.TweenCallback(Callable.From(node.QueueFree));
    }

    private static readonly PackedScene NotEnoughResourceScene =
        GD.Load<PackedScene>("res://templates/resources/not_enough_resource.tscn");

    public static void SetupGainedResource(EntityViewModel vm, Node3D visualNode)
    {
        vm.OnGainedResource.Subscribe(resourceKey =>
        {
            Callable.From(() =>
            {
                if (!Node.IsInstanceValid(visualNode)) return;
                if (NotEnoughResourceScene == null) return;

                var instance = NotEnoughResourceScene.Instantiate<Node3D>();
                visualNode.AddChild(instance);
                instance.Position = new GVector3(0, 0.5f, 0);

                if (instance is NotEnoughResourceView view)
                {
                    view.Setup(resourceKey);
                    var sprite = instance.GetNodeOrNull<AnimatedSprite3D>("AnimatedSprite3D");
                    if (sprite != null)
                        sprite.Modulate = new Color(0.85f, 1f, 0.85f);
                }
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    public static void SetupNotEnoughResource(EntityViewModel vm, Node3D visualNode)
    {
        vm.OnNotEnoughResource.Subscribe(resourceKey =>
        {
            Callable.From(() =>
            {
                if (!Node.IsInstanceValid(visualNode)) return;
                if (NotEnoughResourceScene == null) return;

                var instance = NotEnoughResourceScene.Instantiate<Node3D>();
                visualNode.AddChild(instance);
                instance.Position = new GVector3(0, 0.5f, 0);

                if (instance is NotEnoughResourceView view)
                {
                    view.Setup(resourceKey);
                    var sprite = instance.GetNodeOrNull<AnimatedSprite3D>("AnimatedSprite3D");
                    if (sprite != null)
                        sprite.Modulate = new Color(1f, 0.85f, 0.85f);
                }
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    public static void SetupMovementAnimation(EntityViewModel vm, R3.ReadOnlyReactiveProperty<global::Godot.Vector2> velocity, Node3D flipPivot, Node3D characterNode, bool invertFlip = false)
    {
        velocity.Subscribe(v =>
        {
            Callable.From(() =>
            {
                float speedSq = v.X * v.X + v.Y * v.Y;
                bool isMoving = speedSq > 1f;
                characterNode?.SetDeferred("enable_bounce", isMoving);
                float vx = invertFlip ? -v.X : v.X;
                if (vx < 0)
                    flipPivot.Scale = new GVector3(-Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
                else if (vx > 0)
                    flipPivot.Scale = new GVector3(Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    private static void SpawnHeartBlast(Node3D parent, Entity entity)
    {
        if (HeartTexture == null || BrokenHeartTexture == null) return;
        var state = ReactiveSystem.Instance.BoundState;
        if (state == null) return;

        // Milking: house with a cow that is milking
        if (state.HasComponent<HouseComponent>(entity))
        {
            var house = state.GetComponent<HouseComponent>(entity);
            if (house.CowId != Entity.Null && state.HasComponent<CowComponent>(house.CowId))
            {
                var cow = state.GetComponent<CowComponent>(house.CowId);
                if (cow.IsMilking)
                {
                    bool isPreferred = house.SelectedFood == cow.PreferredFood;
                    var texture = isPreferred ? HeartTexture : (HeartRng.Next(2) == 0 ? HeartTexture : BrokenHeartTexture);
                    SpawnFanHearts(parent, texture);
                    return;
                }
            }
        }

        // Breeding: love house with breed in progress
        if (state.HasComponent<LoveHouseComponent>(entity))
        {
            var lh = state.GetComponent<LoveHouseComponent>(entity);
            if (lh.BreedProgress > 0)
            {
                int heartPercent = lh.HeartPercent > 0 ? lh.HeartPercent : 50;
                var texture = HeartRng.Next(100) < heartPercent ? HeartTexture : BrokenHeartTexture;
                SpawnFanHearts(parent, texture);
                return;
            }
        }
    }

    public static void SpawnHeartsAtWorld(Node sceneParent, GVector3 worldPos, float halfArc = 1.2f, float baseHalfWidth = 0f)
    {
        if (HeartTexture == null || sceneParent == null) return;
        var anchor = new Node3D();
        sceneParent.AddChild(anchor);
        anchor.GlobalPosition = worldPos;
        SpawnFanHearts(anchor, HeartTexture, halfArc, baseHalfWidth);
        var timer = new Timer { WaitTime = 1.5f, OneShot = true };
        anchor.AddChild(timer);
        timer.Timeout += () => { if (Node.IsInstanceValid(anchor)) anchor.QueueFree(); };
        timer.Start();
    }

    private static void SpawnFanHearts(Node3D parent, Texture2D texture, float halfArc = 1.2f, float baseHalfWidth = 0f)
    {
        for (int i = 0; i < 5; i++)
        {
            float t = i / 4f;
            float angle = Mathf.Lerp(-halfArc, halfArc, t);
            float startX = Mathf.Lerp(-baseHalfWidth, baseHalfWidth, t);
            SpawnFanHeart(parent, texture, angle, startX);
        }
    }

    private static void SpawnFanHeart(Node3D parent, Texture2D texture, float angle, float startX = 0f)
    {
        var sprite = new Sprite3D();
        sprite.Texture = texture;
        sprite.PixelSize = 0.001f;
        sprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        sprite.AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass;
        sprite.Shaded = false;

        parent.AddChild(sprite);

        float startY = 3.8f;
        sprite.Position = new GVector3(startX, startY, 0f);

        // Fan direction: angle controls X spread, all rise upward
        float dist = 1.2f + (float)HeartRng.NextDouble() * 0.3f;
        float endX = startX + Mathf.Sin(angle) * dist;
        float endY = startY + Mathf.Cos(angle) * dist;

        sprite.Scale = GVector3.Zero;

        var tween = sprite.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(sprite, "scale", GVector3.One, 0.075f)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(sprite, "position", new GVector3(endX, endY, 0f), 0.35f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(sprite, "modulate:a", 0f, 0.15f)
            .SetDelay(0.2f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(sprite.QueueFree));
    }
}
