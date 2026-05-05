using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using R3;

namespace Template.Godot.Visuals;

public partial class LandViewModel
{
    public ReadOnlyReactiveProperty<int> Remaining { get; private set; } = null!;

    partial void OnInitialize(Context context)
    {
        Remaining = Land.Land.CurrentCoins
            .CombineLatest(Land.Land.Threshold, (current, max) => max - current)
            .ToReadOnlyReactiveProperty();
        Disposables.Add(Remaining);
    }
}
