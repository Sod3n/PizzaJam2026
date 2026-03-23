using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;
using R3;

namespace Template.Godot.Visuals;

public class HouseViewModel : EntityViewModel
{
    public HouseDefinitionModel House { get; }
    
    public ReadOnlyReactiveProperty<string> Capacity { get; }

    public HouseViewModel(Context context) : base(context)
    {
        House = new HouseDefinitionModel(ReactiveSystem.Instance, context);
        Disposables.Add(House);

        if (EntityViewModels[House.House.CowId.CurrentValue] is CowViewModel cowVm) Capacity = cowVm.Capacity;
    }
}
