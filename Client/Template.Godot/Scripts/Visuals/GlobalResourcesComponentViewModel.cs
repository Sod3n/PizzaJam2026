using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public class GlobalResourcesComponentViewModel : EntityViewModel
{
    public GlobalResourcesComponentModel Resources { get; }

    public GlobalResourcesComponentViewModel(Context context) : base(context)
    {
        Resources = new GlobalResourcesComponentModel(ReactiveSystem.Instance, context);
        Disposables.Add(Resources);
    }
}