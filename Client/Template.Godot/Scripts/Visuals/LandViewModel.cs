using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;

namespace Template.Godot.Visuals;

public class LandViewModel : EntityViewModel
{
    public LandComponentModel Land { get; }

    public LandViewModel(Context context) : base(context)
    {
        Land = new LandComponentModel(ReactiveSystem.Instance, context);
        Disposables.Add(Land);
    }
}
