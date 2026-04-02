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

    public static void SetupPositionTween(EntityViewModel vm, Node3D visualNode)
    {
        Tween currentTween = null;
        vm.Transform.Position.Subscribe(p =>
        {
            Callable.From(() =>
            {
                if (!Node.IsInstanceValid(visualNode)) return;
                currentTween?.Kill();
                currentTween = visualNode.CreateTween();
                currentTween.TweenProperty(visualNode, "position", new GVector3((float)p.X, 0f, (float)p.Y), 0.1f);
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    public static void SetupInteractAnimation(EntityViewModel vm, Node3D visualNode, Node3D animateNode = null)
    {
        EntityViewModel.EntityVisualNodes[vm.Entity.Id] = visualNode;
        Disposable.Create(() => EntityViewModel.EntityVisualNodes.Remove(vm.Entity.Id)).AddTo(vm.Disposables);

        SetupNotEnoughResource(vm, visualNode);
        SetupGainedResource(vm, visualNode);
        animateNode ??= visualNode;
        vm.OnInteract.Subscribe(_ =>
        {
            Callable.From(() =>
            {
                if (!Node.IsInstanceValid(animateNode)) return;
                if (animateNode.GetMeta("scale_tween").Obj is Tween tw) tw.SetSpeedScale(100000f);

                var tween = animateNode.CreateTween();
                var origScale = animateNode.GetMeta("orig_scale").Obj is Vector3 s ? s : animateNode.Scale;
                if (animateNode.GetMeta("orig_scale").Obj == null) animateNode.SetMeta("orig_scale", animateNode.Scale);
                tween.SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
                tween.TweenProperty(animateNode, "scale", new Vector3(origScale.X * 1.4f, origScale.Y * 0.7f, origScale.Z), 0.1f);
                tween.Chain().TweenProperty(animateNode, "scale", origScale, 0.1f);
                animateNode.SetMeta("scale_tween", tween);

                SpawnHeartBlast(visualNode, vm.Entity);
            }).CallDeferred();
        }).AddTo(vm.Disposables);
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
        var tween = node.CreateTween();
        tween.TweenProperty(node, "rotation_degrees:x", -60f, duration)
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

    private static void SpawnFanHearts(Node3D parent, Texture2D texture)
    {
        for (int i = 0; i < 5; i++)
        {
            float t = i / 4f;
            float angle = Mathf.Lerp(-1.2f, 1.2f, t);
            SpawnFanHeart(parent, texture, angle);
        }
    }

    private static void SpawnFanHeart(Node3D parent, Texture2D texture, float angle)
    {
        var sprite = new Sprite3D();
        sprite.Texture = texture;
        sprite.PixelSize = 0.001f;
        sprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        sprite.AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass;
        sprite.Shaded = false;

        parent.AddChild(sprite);

        float startY = 3.8f;
        sprite.Position = new GVector3(0f, startY, 0f);

        // Fan direction: angle controls X spread, all rise upward
        float dist = 1.2f + (float)HeartRng.NextDouble() * 0.3f;
        float endX = Mathf.Sin(angle) * dist;
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
