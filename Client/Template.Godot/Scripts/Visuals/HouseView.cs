using Godot;
using R3;
using Deterministic.GameFramework.ECS;
using Template.Godot.Twitch;

namespace Template.Godot.Visuals;

public partial class HouseView
{
    partial void OnSpawned(HouseViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        vm.Capacity.Subscribe(c =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(visualNode)) return;
                visualNode.GetNodeOrNull<Label3D>("Capacity")?.SetDeferred("text", c);
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        var sprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("AnimatedSprite3D/AnimatedSprite3D");
        var mat = sprite?.MaterialOverride as ShaderMaterial;
        if (mat != null)
        {
            vm.ExhaustFill.Subscribe(fill =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(sprite)) return;
                    mat.SetShaderParameter("cooldown_fill", Mathf.Clamp(fill, 0f, 1f));
                }).CallDeferred();
            }).AddTo(vm.Disposables);
        }

        // Cow name label — defined in House.tscn
        var cowNameLabel = visualNode.GetNodeOrNull<Label3D>("CowName");

        vm.House.House.CowId.Subscribe(cowIdInt =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(cowNameLabel)) return;
                var cowEntity = (Entity)cowIdInt;
                if (cowEntity == Entity.Null)
                {
                    cowNameLabel.Visible = false;
                    cowNameLabel.Text = "";
                }
                else
                {
                    cowNameLabel.Text = TwitchIntegration.GetDisplayName(cowEntity);
                    cowNameLabel.Visible = true;
                }
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode, visualNode.GetNodeOrNull<Node3D>("AnimatedSprite3D"));
    }

    partial void OnDespawned(HouseViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
