using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;

namespace Template.Godot.Visuals;

public class CoinViewModel : EntityViewModel
{
    public CoinComponentModel Coin { get; }

    public CoinViewModel(Context context) : base(context)
    {
        Coin = new CoinComponentModel(ReactiveSystem.Instance, context);
        Disposables.Add(Coin);
    }
}
