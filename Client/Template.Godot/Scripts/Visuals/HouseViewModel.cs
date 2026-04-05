using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using R3;

namespace Template.Godot.Visuals;

public partial class HouseViewModel
{
    public ReactiveProperty<string> Capacity { get; } = new("");
    public ReactiveProperty<float> ExhaustFill { get; } = new(0f);

    partial void OnInitialize(Context context)
    {
        House.House.CowId.Subscribe(cowId =>
        {
            if (cowId != Entity.Null && EntityViewModels.TryGetValue(cowId, out var vm) && vm is CowViewModel cowVm)
            {
                cowVm.Capacity.Subscribe(c => Capacity.Value = c).AddTo(Disposables);
                cowVm.Cow.Cow.Exhaust.CombineLatest(cowVm.Cow.Cow.MaxExhaust, (ex, max) =>
                    max > 0 ? (float)ex / max : 0f
                ).Subscribe(f => ExhaustFill.Value = f).AddTo(Disposables);
            }
            else
            {
                Capacity.Value = "";
                ExhaustFill.Value = 0f;
            }
        }).AddTo(Disposables);
    }
}
