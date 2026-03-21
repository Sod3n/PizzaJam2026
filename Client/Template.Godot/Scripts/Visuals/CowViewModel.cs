using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;

namespace Template.Godot.Visuals;

public class CowViewModel : EntityViewModel
{
    public CowComponentModel Cow { get; }
    public SkinComponentModel Skin { get; }

    public CowViewModel(Context context) : base(context)
    {
        Cow = new CowComponentModel(ReactiveSystem.Instance, context);
        Disposables.Add(Cow);

        if (context.State.HasComponent<SkinComponent>(Entity))
        {
            Skin = new SkinComponentModel(ReactiveSystem.Instance, context);
            Disposables.Add(Skin);
        }
    }
}
