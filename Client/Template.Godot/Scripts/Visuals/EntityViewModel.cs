using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Deterministic.GameFramework.TwoD;

namespace Template.Godot.Visuals;

public class EntityViewModel : ViewModel
{
    public Entity Entity { get; }
    public Transform2DModel Transform { get; }
    
    public bool IsPlayer { get; private set; }
    public bool IsCoin { get; private set; }

    public EntityViewModel(Context context)
    {
        Entity = context.Entity;
        
        // Transform2DModel is auto-generated for Transform2D component
        Transform = new Transform2DModel(ReactiveSystem.Instance, context);
        Disposables.Add(Transform);
        
        // Try to initialize immediately
        bool hasPlayer = context.State.HasComponent<PlayerEntity>(Entity);
        bool hasCoin = context.State.HasComponent<CoinComponent>(Entity);
        
        // Godot.GD.Print($"[EntityViewModel] Created for Entity {Entity.Id}. HasPlayer: {hasPlayer}, HasCoin: {hasCoin}");

        if (hasPlayer)
        {
            InitializePlayer(context);
        }

        if (hasCoin)
        {
            InitializeCoin(context);
        }
    }
    
    public void InitializePlayer(Context context)
    {
        if (IsPlayer) return;
        
        IsPlayer = true;
        Player = new PlayerEntityModel(ReactiveSystem.Instance, context);
        Disposables.Add(Player);
    }

    public void InitializeCoin(Context context)
    {
        if (IsCoin) return;
        
        IsCoin = true;
        Coin = new CoinComponentModel(ReactiveSystem.Instance, context);
        Disposables.Add(Coin);
    }
    
    public PlayerEntityModel? Player { get; private set; }
    public CoinComponentModel? Coin { get; private set; }
}
