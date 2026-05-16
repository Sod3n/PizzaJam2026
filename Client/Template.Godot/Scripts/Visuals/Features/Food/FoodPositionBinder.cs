using Deterministic.GameFramework.Reactive;
using Godot;
using DTransform2D = Deterministic.GameFramework.TwoD.Transform2D;

namespace Template.Godot.Visuals;

internal static class FoodPositionBinder
{
    public static void Bind(EntityViewModel vm, Node3D visualNode)
    {
        var state = ReactiveSystem.Instance.BoundState;
        if (state == null || !state.HasComponent<DTransform2D>(vm.Entity)) return;
        var p = state.GetComponent<DTransform2D>(vm.Entity).Position;
        visualNode.Position = new Vector3((float)p.X, 0f, (float)p.Y);
    }
}
