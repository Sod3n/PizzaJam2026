using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public class MetricsComponentViewModel : EntityViewModel
{
    public MetricsComponentModel Metrics { get; }

    public MetricsComponentViewModel(Context context) : base(context)
    {
        Metrics = new MetricsComponentModel(ReactiveSystem.Instance, context);
        Disposables.Add(Metrics);
    }
}
