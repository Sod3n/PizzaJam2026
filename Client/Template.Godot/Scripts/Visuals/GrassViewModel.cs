using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;

namespace Template.Godot.Visuals;

public class GrassViewModel : EntityViewModel
{
    public GrassComponentModel Grass { get; }

    public GrassViewModel(Context context) : base(context)
    {
        Grass = new GrassComponentModel(ReactiveSystem.Instance, context);
        Disposables.Add(Grass);
    }
}
