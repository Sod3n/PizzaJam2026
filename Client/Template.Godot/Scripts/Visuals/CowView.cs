using Godot;
using R3;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class CowView
{
    partial void OnSpawned(CowViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        ViewHelpers.SetupMovementAnimation(vm, vm.Cow.CharacterBody2D.RealVelocity, flipPivot, characterNode, invertFlip: true);
        ViewHelpers.SetupPositionTween(vm, visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        // Show breed result overlay for newly-born cows (tagged server-side with BreedBornComponent)
        var state = ReactiveSystem.Instance.BoundState;
        if (state != null && state.HasComponent<BreedBornComponent>(vm.Entity))
            Callable.From(() => BreedResultOverlay.ShowForCow(GetTree(), vm, visualNode)).CallDeferred();

        // Depression indicator — visible above cow while depressed
        var depressionIcon = new Label3D();
        depressionIcon.Text = "zzZ";
        depressionIcon.FontSize = 96;
        depressionIcon.Modulate = new Color(0.6f, 0.6f, 1f, 0.9f);
        depressionIcon.OutlineModulate = new Color(0.2f, 0.2f, 0.5f, 1f);
        depressionIcon.Position = new Vector3(0, 2.5f, 0);
        depressionIcon.NoDepthTest = true;
        depressionIcon.RenderPriority = 3;
        depressionIcon.OutlineRenderPriority = 2;
        depressionIcon.Visible = false;
        visualNode.AddChild(depressionIcon);

        vm.Cow.Cow.IsDepressed.Subscribe(depressed =>
        {
            Callable.From(() =>
            {
                if (IsInstanceValid(depressionIcon))
                    depressionIcon.Visible = depressed;
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(CowViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
