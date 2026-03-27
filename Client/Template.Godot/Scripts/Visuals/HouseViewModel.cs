using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using R3;

namespace Template.Godot.Visuals;

public partial class HouseViewModel
{
    public ReactiveProperty<string> Capacity { get; } = new("");

    partial void OnInitialize(Context context)
    {
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
