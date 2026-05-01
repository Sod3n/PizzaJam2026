using Godot;
using R3;

namespace Template.Godot.Visuals;

public partial class PlayerHouseView
{
    private const int SleepCooldownTicks = 7200;

    partial void OnSpawned(PlayerHouseViewModel vm, global::Godot.Node3D visualNode)
    {
        DespawnDelay = 0.3f;
        ViewHelpers.PlayAppear(visualNode);
        ViewHelpers.SetupInteractAnimation(vm, visualNode);

        var sprite = visualNode.GetNodeOrNull<AnimatedSprite3D>("AnimatedSprite3D");
        var mat = sprite?.MaterialOverride as ShaderMaterial;

        vm.PlayerHouse.PlayerHouse.CooldownTicksRemaining.Subscribe(ticks =>
        {
            Callable.From(() =>
            {
                if (mat == null || !IsInstanceValid(sprite)) return;
                if (ticks > 0)
                {
                    float progress = (float)ticks / SleepCooldownTicks;
                    mat.SetShaderParameter("cooldown_fill", Mathf.Clamp(progress, 0f, 1f));
                }
                else
                {
                    mat.SetShaderParameter("cooldown_fill", 0f);
                }
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(PlayerHouseViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
