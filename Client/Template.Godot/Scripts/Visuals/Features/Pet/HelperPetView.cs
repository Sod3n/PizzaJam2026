using System;
using Godot;
using R3;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

public partial class HelperPetView
{
    private const string CarryAnchorName = "CarryAnchor";

    partial void OnSpawned(HelperPetViewModel vm, Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        var (flipPivot, characterNode) = ViewHelpers.SetupFlipPivot(visualNode);
        vm.HelperPet.CharacterBody2D.Velocity.Subscribe(v =>
        {
            Callable.From(() =>
            {
                bool carried = vm.HelperPet.HelperPet.State.CurrentValue == PetState.Carried;
                float vx = (float)v.X;
                float vy = (float)v.Y;
                bool isMoving = !carried && (vx * vx + vy * vy) > 1f;
                characterNode?.SetDeferred("enable_bounce", isMoving);
                if (!carried)
                {
                    if (vx < 0)
                        flipPivot.Scale = new GVector3(-Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
                    else if (vx > 0)
                        flipPivot.Scale = new GVector3(Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
                }
            }).CallDeferred();
        }).AddTo(vm.Disposables);
        ViewHelpers.SetupInteractAnimation(vm, visualNode, flipPivot);

        var worldParent = (Node3D)visualNode.GetParent();
        IDisposable positionTracker = ViewSmoothingManager.Smoother.TrackPosition3D(vm.Entity, visualNode, tau: 0.08f);
        Disposable.Create(() => positionTracker?.Dispose()).AddTo(vm.Disposables);

        Node3D currentAnchor = null;

        vm.HelperPet.HelperPet.State.Subscribe(state =>
        {
            Callable.From(() =>
            {
                if (!Node.IsInstanceValid(visualNode)) return;

                if (state == PetState.Carried)
                {
                    int carrierId = vm.HelperPet.HelperPet.FollowTarget.CurrentValue;
                    if (carrierId == 0) return;
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
                    if (Node.IsInstanceValid(characterNode))
                        characterNode.Set("enable_bounce", false);
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

        var state = ReactiveSystem.Instance.BoundState;
        if (state != null && state.HasComponent<BreedBornComponent>(vm.Entity))
            Callable.From(() => BreedResultOverlay.ShowForPet(GetTree(), vm, visualNode)).CallDeferred();
    }

    partial void OnDespawned(HelperPetViewModel vm, Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
