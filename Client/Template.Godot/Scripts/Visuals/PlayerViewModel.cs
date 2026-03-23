using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;
using R3;

namespace Template.Godot.Visuals;

public class PlayerViewModel : EntityViewModel
{
    public PlayerDefinitionModel Player { get; }
    public SkinComponentModel Skin { get; }
    public ReactiveProperty<bool> IsHidden { get; } = new();

    public PlayerViewModel(Context context) : base(context)
    {
        Player = new PlayerDefinitionModel(ReactiveSystem.Instance, context);
        Disposables.Add(Player);

        if (context.State.HasComponent<SkinComponent>(Entity))
        {
            Skin = new SkinComponentModel(ReactiveSystem.Instance, context);
            Disposables.Add(Skin);
        }
        
        ReactiveSystem.Instance.ObserveAdd<HiddenComponent>().Where(x => x == Entity).Subscribe(_ => IsHidden.Value = true).AddTo(Disposables);
        ReactiveSystem.Instance.ObserveRemove<HiddenComponent>().Where(x => x == Entity).Subscribe(_ => IsHidden.Value = false).AddTo(Disposables);
    }
}
