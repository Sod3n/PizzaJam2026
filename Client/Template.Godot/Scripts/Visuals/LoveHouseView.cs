using Godot;
using R3;

namespace Template.Godot.Visuals;

public partial class LoveHouseView
{
    partial void OnSpawned(LoveHouseViewModel vm, global::Godot.Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var sprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("AnimatedSprite3D");
        var mat = sprite?.MaterialOverride as ShaderMaterial;

        // Cooldown is binary now (sleep-only reset). Show the shader overlay full-on while
        // CooldownTicksRemaining > 0, off when it's 0.
        vm.LoveHouse.LoveHouse.CooldownTicksRemaining.Subscribe(ticks =>
        {
            Callable.From(() =>
            {
                if (mat == null || !IsInstanceValid(sprite)) return;
                mat.SetShaderParameter("cooldown_fill", ticks > 0 ? 1f : 0f);
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(LoveHouseViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
