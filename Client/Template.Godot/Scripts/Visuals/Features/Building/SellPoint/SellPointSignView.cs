using Godot;
using R3;

namespace Template.Godot.Visuals;

public partial class SellPointSignView
{
    partial void OnSpawned(SellPointSignViewModel vm, global::Godot.Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);

        var icon = visualNode.GetNodeOrNull<AnimatedSprite3D>("ProductIcon");

        vm.SellPointSign.SellPointSign.CurrentProduct.Subscribe(product =>
        {
            Callable.From(() =>
            {
                if (icon == null || !IsInstanceValid(icon)) return;
                icon.Frame = product;
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(SellPointSignViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
