using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.TwoD;

namespace Template.Godot.Visuals;

public class EntityViewModel : ViewModel
{
    public Entity Entity { get; }
    public Transform2DModel Transform { get; }

    public EntityViewModel(Context context)
    {
        Entity = context.Entity;
        
        // Always bind Transform for any visual entity
        Transform = new Transform2DModel(ReactiveSystem.Instance, context);
        Disposables.Add(Transform);
    }
}
