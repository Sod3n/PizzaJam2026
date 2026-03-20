using System;
using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Network.Client;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.TwoD;
using ObservableCollections;
using R3;
using Template.Client.ViewModels;
using Template.Shared.Components;

namespace Template.Client.Visuals;

public class ConsoleVisualizer : IDisposable
{
    private readonly GameClient _client;
    private ObservableList<EntityViewModel>? _entities;
    private readonly CompositeDisposable _disposables = new();
    
    // Keep track of active view models to dispose them if needed or just for lookup
    private readonly Dictionary<int, EntityViewModel> _activeViewModels = new();

    public ConsoleVisualizer(GameClient client)
    {
        _client = client;
    }

    public void Initialize()
    {
        if (_entities != null) return;
        
        Console.WriteLine("[ConsoleVisualizer] Initializing MVVM bindings...");

        // Create ObservableList from ReactiveSystem
        // Querying for all entities with Transform2D component
        _entities = _client.Reactive.ObservableList<Transform2D, EntityViewModel>(
            ctx => new EntityViewModel(ctx),
            _disposables
        );

        // Subscribe to list changes
        _entities.ObserveAdd().Subscribe(e => OnEntityAdded(e.Value)).AddTo(_disposables);
        _entities.ObserveRemove().Subscribe(e => OnEntityRemoved(e.Value)).AddTo(_disposables);
        _entities.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);
        
        // Also observe component additions to update existing ViewModels
        // This handles cases where PlayerEntity/CoinComponent are added AFTER Transform2D
        _client.Reactive.ObserveAdd<PlayerEntity>()
            .Subscribe(entity => UpdateViewModelType(entity, vm => 
            {
                vm.InitializePlayer(new Context(_client.State, entity, _client));
                Console.WriteLine($"[ConsoleVisualizer] Entity {entity.Id} identified as PLAYER");
            }))
            .AddTo(_disposables);

        _client.Reactive.ObserveAdd<CoinComponent>()
            .Subscribe(entity => UpdateViewModelType(entity, vm => 
            {
                vm.InitializeCoin(new Context(_client.State, entity, _client));
                Console.WriteLine($"[ConsoleVisualizer] Entity {entity.Id} identified as COIN");
            }))
            .AddTo(_disposables);
        
        // Initialize existing entities
        foreach (var vm in _entities)
        {
            OnEntityAdded(vm);
        }
    }

    private void OnEntityAdded(EntityViewModel vm)
    {
        if (_activeViewModels.ContainsKey(vm.Entity.Id)) return;
        
        _activeViewModels[vm.Entity.Id] = vm;
        
        string type = "Unknown";
        if (vm.IsPlayer) type = "Player";
        if (vm.IsCoin) type = "Coin";
        
        Console.WriteLine($"[ConsoleVisualizer] Entity Added: {vm.Entity.Id} (Type: {type})");

        // Bind Position
        vm.Transform.Position.Subscribe(pos => 
        {
            // Throttle logs or just print? Let's just print for major changes or keep it quiet to avoid spam
            // Console.WriteLine($"[Entity {vm.Entity.Id}] Pos: {pos}");
        }).AddTo(vm.Disposables);
    }

    private void OnEntityRemoved(EntityViewModel vm)
    {
        if (_activeViewModels.Remove(vm.Entity.Id))
        {
            Console.WriteLine($"[ConsoleVisualizer] Entity Removed: {vm.Entity.Id}");
        }
    }

    private void OnListReset()
    {
        Console.WriteLine("[ConsoleVisualizer] List Reset");
        _activeViewModels.Clear();
    }

    private void UpdateViewModelType(Entity entity, Action<EntityViewModel> updateAction)
    {
        if (_activeViewModels.TryGetValue(entity.Id, out var vm))
        {
            updateAction(vm);
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _entities = null;
        _activeViewModels.Clear();
    }
}
