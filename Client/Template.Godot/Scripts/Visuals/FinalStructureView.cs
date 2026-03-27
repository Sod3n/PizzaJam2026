using Godot;
using R3;

namespace Template.Godot.Visuals;

public partial class FinalStructureView
{
    partial void OnSpawned(FinalStructureViewModel vm, Node3D visualNode)
    {
        vm.Remaining.Subscribe(x =>
        {
            GD.Print($"[FinalStructureView] Remaining={x}, Threshold={vm.FinalStructure.FinalStructure.Threshold.CurrentValue}, CurrentCoins={vm.FinalStructure.FinalStructure.CurrentCoins.CurrentValue}");
            Callable.From(() =>
            {
                if (!IsInstanceValid(visualNode)) return;
                if (x <= 0)
                {
                    visualNode.GetNodeOrNull<Node3D>("Node3D")?.SetDeferred("visible", false);
                    visualNode.GetNodeOrNull<Node3D>("Finale")?.SetDeferred("visible", true);
                    GetTree().Root.GetNode("Global").EmitSignal("on_finale");
                    return;
                }
                visualNode.GetNodeOrNull<Label3D>("Node3D/Remaining")?.SetDeferred("text", x.ToString());
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);
    }
}
