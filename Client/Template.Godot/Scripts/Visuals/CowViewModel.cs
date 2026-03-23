using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;
using R3;

namespace Template.Godot.Visuals;

public class CowViewModel : EntityViewModel
{
    public CowDefinitionModel Cow { get; }
    public SkinComponentModel Skin { get; }
    
    public ReactiveProperty<bool> IsHidden { get; } = new();
    
    public ReadOnlyReactiveProperty<string> Capacity { get; }

    public CowViewModel(Context context) : base(context)
    {
        Cow = new CowDefinitionModel(ReactiveSystem.Instance, context);
        Disposables.Add(Cow);

        if (context.State.HasComponent<SkinComponent>(Entity))
        {
            Skin = new SkinComponentModel(ReactiveSystem.Instance, context);
            Disposables.Add(Skin);
        }
        
        Capacity = Cow.Cow.MaxExhaust.CombineLatest(Cow.Cow.Exhaust, (max, current) => $"[{max - current}/{max}]").ToReadOnlyReactiveProperty();
        
        ReactiveSystem.Instance.ObserveAdd<HiddenComponent>().Where(x => x == Entity).Subscribe(_ => IsHidden.Value = true).AddTo(Disposables);
        ReactiveSystem.Instance.ObserveRemove<HiddenComponent>().Where(x => x == Entity).Subscribe(_ => IsHidden.Value = false).AddTo(Disposables);
    }
}
