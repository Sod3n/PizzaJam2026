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

        // Cooldown overlay scales by remaining/max — works for both Days and Ticks units
        // (Days now decrements per sleep, so a 2-day cooldown shows 100% → 50% → 0%).
        var cd = vm.LoveHouse.Cooldown;
        cd.TicksRemaining.Subscribe(ticks =>
        {
            Callable.From(() =>
            {
                if (mat == null || !IsInstanceValid(sprite)) return;
                int max = cd.MaxTicks.CurrentValue;
                float progress = max > 0 ? (float)ticks / max : 0f;
                mat.SetShaderParameter("cooldown_fill", Mathf.Clamp(progress, 0f, 1f));
            }).CallDeferred();
        }).AddTo(vm.Disposables);
    }

    partial void OnDespawned(LoveHouseViewModel vm, global::Godot.Node3D visualNode)
    {
        ViewHelpers.PlayDisappear(visualNode, 0.3f, freeAfter: false);
    }
}
