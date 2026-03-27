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

    public ReactiveProperty<string> Capacity { get; } = new("");

    public HouseViewModel(Context context) : base(context)
    {
        House = new HouseDefinitionModel(ReactiveSystem.Instance, context);
        Disposables.Add(House);

        House.House.CowId.Subscribe(cowId =>
        {
            if (cowId != Entity.Null && EntityViewModels.TryGetValue(cowId, out var vm) && vm is CowViewModel cowVm)
            {
                cowVm.Capacity.Subscribe(c => Capacity.Value = c).AddTo(Disposables);
            }
            else
            {
                Capacity.Value = "";
            }
        }).AddTo(Disposables);
    }
}
