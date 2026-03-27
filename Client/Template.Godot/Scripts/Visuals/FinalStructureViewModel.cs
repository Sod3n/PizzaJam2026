using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using R3;

namespace Template.Godot.Visuals;

public partial class FinalStructureViewModel
{
    public ReadOnlyReactiveProperty<int> Remaining { get; private set; } = null!;

    partial void OnInitialize(Context context)
    {
        Remaining = FinalStructure.FinalStructure.CurrentCoins
            .CombineLatest(FinalStructure.FinalStructure.Threshold, (current, max) => max - current)
            .ToReadOnlyReactiveProperty();
        Disposables.Add(Remaining);
    }
}
