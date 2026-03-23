using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;
using R3;

namespace Template.Godot.Visuals;

public class LandViewModel : EntityViewModel
{
    public LandDefinitionModel Land { get; }
    
    public ReadOnlyReactiveProperty<int> Remaining { get; }

    public LandViewModel(Context context) : base(context)
    {
        Land = new LandDefinitionModel(ReactiveSystem.Instance, context);
        Disposables.Add(Land);
        
        Remaining = Land.Land.CurrentCoins.CombineLatest(Land.Land.Threshold, (current, max) => max - current).ToReadOnlyReactiveProperty();
    }
}
