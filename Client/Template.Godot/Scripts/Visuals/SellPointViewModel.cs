using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;

namespace Template.Godot.Visuals;

public class SellPointViewModel : EntityViewModel
{
    public SellPointComponentModel SellPoint { get; }

    public SellPointViewModel(Context context) : base(context)
    {
        SellPoint = new SellPointComponentModel(ReactiveSystem.Instance, context);
        Disposables.Add(SellPoint);
    }
}
