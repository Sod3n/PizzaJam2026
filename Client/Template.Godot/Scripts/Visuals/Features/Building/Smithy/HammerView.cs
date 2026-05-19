using System;
using Godot;
using R3;
using Deterministic.GameFramework.Reactive;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

public partial class HammerView
{
    private const string CarryAnchorName = "CarryAnchor";

    partial void OnSpawned(HammerViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode, pivotAtNodeOrigin: true);
        IDisposable positionTracker = ViewSmoothingManager.Smoother.TrackPosition3D(vm.Entity, visualNode, tau: 0.08f);
        Disposable.Create(() => positionTracker?.Dispose()).AddTo(vm.Disposables);

        var worldParent = (Node3D)visualNode.GetParent();
        Node3D currentAnchor = null;

        vm.Hammer.Hammer.Carrier.Subscribe(carrierId =>
        {
            Callable.From(() =>
            {
                if (!Node.IsInstanceValid(visualNode)) return;

                if (carrierId > 0)
                {
                    if (!EntityViewModel.EntityVisualNodes.TryGetValue(carrierId, out var carrierVisual)) return;
                    if (!Node.IsInstanceValid(carrierVisual)) return;
                    var anchor = carrierVisual.FindChild(CarryAnchorName, recursive: true, owned: false) as Node3D;
                    if (anchor == null) return;
                    if (currentAnchor == anchor) return;

                    positionTracker?.Dispose();
                    positionTracker = null;

                    var parent = visualNode.GetParent();
                    parent?.RemoveChild(visualNode);
                    anchor.AddChild(visualNode);
                    visualNode.Position = GVector3.Zero;
                    visualNode.Rotation = GVector3.Zero;
                    currentAnchor = anchor;
                }
                else if (currentAnchor != null)
                {
                    var parent = visualNode.GetParent();
                    parent?.RemoveChild(visualNode);
                    if (Node.IsInstanceValid(worldParent))
                        worldParent.AddChild(visualNode);
                    currentAnchor = null;

                    positionTracker ??= ViewSmoothingManager.Smoother.TrackPosition3D(vm.Entity, visualNode, tau: 0.08f);
                }
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(HammerViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
