using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.TwoD;
using R3;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public class EntityViewModel : ViewModel
{
    public static Dictionary<int, EntityViewModel> EntityViewModels { get; } = new();
    
    public Entity Entity { get; }
    public Transform2DModel Transform { get; }
    
    public Subject<Unit> OnInteract { get; } = new();

    public EntityViewModel(Context context)
    {
        Entity = context.Entity;
        EntityViewModels[Entity.Id] = this;
        
        // Always bind Transform for any visual entity
        Transform = new Transform2DModel(ReactiveSystem.Instance, context);
        Disposables.Add(Transform);
        
        ReactiveSystem.Instance.ObserveAdd<EnterStateComponent>()
            .Where(x => x == Entity && ReactiveSystem.Instance.BoundState != null
                && ReactiveSystem.Instance.BoundState.GetComponent<EnterStateComponent>(x).Key == "interacted")
            .Subscribe(_ => OnInteract.OnNext(Unit.Default)).AddTo(Disposables);
    }
}
