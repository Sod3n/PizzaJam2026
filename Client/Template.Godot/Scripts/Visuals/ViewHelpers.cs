using Godot;
using R3;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

public static class ViewHelpers
{
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

    public static void SetupMovementAnimation(EntityViewModel vm, R3.ReadOnlyReactiveProperty<Deterministic.GameFramework.Types.Vector2> velocity, Node3D flipPivot, Node3D characterNode)
    {
        velocity.Subscribe(v =>
        {
            Callable.From(() =>
            {
                bool isMoving = (float)v.X != 0f || (float)v.Y != 0f;
                characterNode?.SetDeferred("enable_bounce", isMoving);
                if ((float)v.X < 0)
                    flipPivot.Scale = new GVector3(-Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
                else if ((float)v.X > 0)
                    flipPivot.Scale = new GVector3(Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    public static void SetupMovementAnimation(EntityViewModel vm, R3.ReadOnlyReactiveProperty<global::Godot.Vector2> velocity, Node3D flipPivot, Node3D characterNode)
    {
        velocity.Subscribe(v =>
        {
            Callable.From(() =>
            {
                bool isMoving = v.X != 0f || v.Y != 0f;
                characterNode?.SetDeferred("enable_bounce", isMoving);
                if (v.X < 0)
                    flipPivot.Scale = new GVector3(-Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
                else if (v.X > 0)
                    flipPivot.Scale = new GVector3(Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }
}
