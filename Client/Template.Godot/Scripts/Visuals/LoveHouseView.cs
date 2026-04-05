using Godot;
using R3;

namespace Template.Godot.Visuals;

public partial class LoveHouseView
{
    private const int TickRate = 60;
    private const int BreedCooldownTicks = 5400; // must match server constant

    partial void OnSpawned(LoveHouseViewModel vm, global::Godot.Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var sprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("AnimatedSprite3D");
        var mat = sprite?.MaterialOverride as ShaderMaterial;

        vm.LoveHouse.LoveHouse.CooldownTicksRemaining.Subscribe(ticks =>
        {
            Callable.From(() =>
            {
                if (mat == null || !IsInstanceValid(sprite)) return;
                if (ticks > 0)
                {
                    float progress = (float)ticks / BreedCooldownTicks;
                    mat.SetShaderParameter("cooldown_fill", Mathf.Clamp(progress, 0f, 1f));
                }
                else
                {
                    mat.SetShaderParameter("cooldown_fill", 0f);
                }
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(LoveHouseViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
