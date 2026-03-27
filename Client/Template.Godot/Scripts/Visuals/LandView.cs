using Godot;
using R3;

namespace Template.Godot.Visuals;

public partial class LandView
{
    partial void OnSpawned(LandViewModel vm, Node3D visualNode)
    {
        vm.Remaining.Subscribe(x =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(visualNode)) return;
                visualNode.GetNodeOrNull<Label3D>("Remaining")?.SetDeferred("text", x.ToString());
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }
}
