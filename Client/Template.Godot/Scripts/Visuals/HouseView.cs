using Godot;
using R3;

namespace Template.Godot.Visuals;

public partial class HouseView
{
    partial void OnSpawned(HouseViewModel vm, Node3D visualNode)
    {
        vm.Capacity.Subscribe(c =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(visualNode)) return;
                visualNode.GetNodeOrNull<Label3D>("Capacity")?.SetDeferred("text", c);
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode, visualNode.GetNodeOrNull<Node3D>("AnimatedSprite3D"));
    }
}
