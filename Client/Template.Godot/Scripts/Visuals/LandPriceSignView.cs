using Deterministic.GameFramework.ECS;
using Godot;
using R3;

namespace Template.Godot.Visuals;

public partial class LandPriceSignView
{
    partial void OnSpawned(LandPriceSignViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        // Registers EntityVisualNodes[entity.Id] so InteractOutlineView can find this sign,
        // AND wires the click squish animation on OnInteract.
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var label = visualNode.GetNodeOrNull<Label3D>("Remaining");
        if (label == null) return;

        var landId = vm.LandPriceSign.LandPriceSign.LandId.CurrentValue;
        if (landId == Entity.Null) return;
        if (!EntityViewModel.EntityViewModels.TryGetValue(landId, out var landVmBase)) return;
        if (landVmBase is not LandViewModel landVm) return;

        landVm.Remaining.Subscribe(remaining =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(label)) return;
                label.Text = remaining.ToString();
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(LandPriceSignViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
