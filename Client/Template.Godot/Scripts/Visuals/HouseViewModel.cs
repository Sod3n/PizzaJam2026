using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;

namespace Template.Godot.Visuals;

public class HouseViewModel : EntityViewModel
{
    public HouseComponentModel House { get; }

    public HouseViewModel(Context context) : base(context)
    {
        House = new HouseComponentModel(ReactiveSystem.Instance, context);
        Disposables.Add(House);
    }
}
