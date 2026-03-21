using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;

namespace Template.Godot.Visuals;

public class PlayerViewModel : EntityViewModel
{
    public PlayerEntityModel Player { get; }
    public SkinComponentModel Skin { get; }

    public PlayerViewModel(Context context) : base(context)
    {
        Player = new PlayerEntityModel(ReactiveSystem.Instance, context);
        Disposables.Add(Player);

        if (context.State.HasComponent<SkinComponent>(Entity))
        {
            Skin = new SkinComponentModel(ReactiveSystem.Instance, context);
            Disposables.Add(Skin);
        }
    }
}
