using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;
using R3;

namespace Template.Godot.Visuals;

public class FinalStructureViewModel : EntityViewModel
{
    public FinalStructureDefinitionModel FinalStructure { get; }
    
    public ReadOnlyReactiveProperty<int> Remaining { get; }

    public FinalStructureViewModel(Context context) : base(context)
    {
        FinalStructure = new FinalStructureDefinitionModel(ReactiveSystem.Instance, context);
        Disposables.Add(FinalStructure);
        
        Remaining = FinalStructure.FinalStructure.CurrentCoins.CombineLatest(FinalStructure.FinalStructure.Threshold, (current, max) => max - current).ToReadOnlyReactiveProperty();
    }
}
