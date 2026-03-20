using Godot;
using System;
using System.Collections.Generic;
using Template.Godot.Core;
using Template.Shared.Components;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.TwoD;
using ObservableCollections;
using R3;
using Transform2D = Deterministic.GameFramework.TwoD.Transform2D;

namespace Template.Godot.Visuals;

public partial class EntityVisualizer : Node2D
{
	[Export] public Core.GameManager GameManager;
	[Export] public PackedScene PlayerPrefab; // Assign in Editor
	[Export] public PackedScene CoinPrefab;   // Assign in Editor

	private ObservableList<EntityViewModel> _entities;
	private readonly CompositeDisposable _disposables = new();
	private readonly Dictionary<EntityViewModel, Node2D> _spawnedEntities = new();
	private readonly System.Collections.Concurrent.ConcurrentDictionary<int, EntityViewModel> _vmLookup = new();

	public override void _Ready()
	{
		// Try to initialize immediately if possible
		if (GameManager != null && GameManager.IsGameRunning)
		{
			Initialize();
		}
	}

	public override void _Process(double delta)
	{
		// Lazy initialization if not ready in _Ready
		if (_entities == null && GameManager != null && GameManager.IsGameRunning)
		{
			Initialize();
		}
	}

	private void Initialize()
	{
		if (_entities != null) return;
		
		var client = GameManager.GameClient;
		if (client == null || client.Reactive == null) return;

		// Create ObservableList from ReactiveSystem
		// Querying for all entities with Transform2D component
		_entities = client.Reactive.ObservableList<Transform2D, EntityViewModel>(
			ctx => new EntityViewModel(ctx),
			_disposables
		);

		// Subscribe to list changes
		_entities.ObserveAdd().Subscribe(e => 
		{
			_vmLookup[e.Value.Entity.Id] = e.Value;
			OnEntityAdded(e.Value);
		}).AddTo(_disposables);
		
		_entities.ObserveRemove().Subscribe(e => 
		{
			_vmLookup.TryRemove(e.Value.Entity.Id, out _);
			OnEntityRemoved(e.Value);
		}).AddTo(_disposables);
		
		_entities.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);
		
		// Also observe component additions to update existing ViewModels
		// This handles cases where PlayerEntity/CoinComponent are added AFTER Transform2D
		client.Reactive.ObserveAdd<PlayerEntity>()
			.Subscribe(entity => UpdateViewModelType(entity, vm => 
                vm.InitializePlayer(new Context(client.State, entity, client))))
			.AddTo(_disposables);

		client.Reactive.ObserveAdd<CoinComponent>()
			.Subscribe(entity => UpdateViewModelType(entity, vm => 
                vm.InitializeCoin(new Context(client.State, entity, client))))
			.AddTo(_disposables);
		
		// Initialize existing entities
		foreach (var vm in _entities)
		{
			_vmLookup[vm.Entity.Id] = vm;
			OnEntityAdded(vm);
		}

		// Disable _Process as we don't need polling anymore
		SetProcess(false);
	}

	private void UpdateViewModelType(Entity entity, Action<EntityViewModel> updateAction)
	{
		// This runs on the Network Thread
		if (_vmLookup.TryGetValue(entity.Id, out var vm))
		{
			GD.Print($"[EntityVisualizer] Updating ViewModel Type for Entity {entity.Id}");
			updateAction(vm);
			
			// Schedule visual update on Main Thread
			Callable.From(() => ReSpawnEntityVisual(vm)).CallDeferred();
		}
	}
	
	private void ReSpawnEntityVisual(EntityViewModel vm)
	{
		// This runs on Main Thread via CallDeferred
		if (_spawnedEntities.TryGetValue(vm, out var oldNode))
		{
			// Remove old visual
			if (IsInstanceValid(oldNode))
			{
				oldNode.QueueFree();
			}
			_spawnedEntities.Remove(vm);
			
			// Add new visual
			var newNode = SpawnEntity(vm);
			if (newNode != null)
			{
				AddChild(newNode);
				_spawnedEntities[vm] = newNode;
				
				// Re-bind (positions are already bound in VM, but we need to update the node)
				// Actually, the subscriptions in OnEntityAdded are still active on the VM,
				// but they capture 'visualNode'.
				// We need to re-subscribe because the closure 'visualNode' refers to the OLD node.
				// Wait, OnEntityAdded sets up subscriptions.
				// We should probably just call OnEntityRemoved then OnEntityAdded.
			}
		}
		
		// Re-trigger OnEntityAdded to re-bind if not found or just to be safe
		// If it wasn't spawned yet, this will spawn it.
		OnEntityAdded(vm);
	}

	private void OnListReset()
	{
		foreach (var vm in _spawnedEntities.Keys)
		{
			OnEntityRemoved(vm);
		}
		_spawnedEntities.Clear();
	}

	private void OnEntityAdded(EntityViewModel vm)
	{
		// Marshal to main thread for Node operations
		Callable.From(() => 
		{
			// If already spawned, don't spawn again (unless we are forcing re-bind)
			// But UpdateViewModelType removes it first.
			if (_spawnedEntities.ContainsKey(vm)) return;

			// Log spawn attempt
			GD.Print($"[EntityVisualizer] Spawning Entity {vm.Entity.Id}. IsPlayer: {vm.IsPlayer}, IsCoin: {vm.IsCoin}");

			var visualNode = SpawnEntity(vm);
			
			if (visualNode != null)
			{
				AddChild(visualNode);
				_spawnedEntities[vm] = visualNode;
				
				// Bind Position
				vm.Transform.Position.Subscribe(pos => 
				{
					// Marshal property updates
					Callable.From(() => 
					{
						if (IsInstanceValid(visualNode))
						{
							visualNode.Position = new Vector2((float)pos.X, (float)pos.Y);
						}
					}).CallDeferred();
				}).AddTo(vm.Disposables);
				
				// Bind Rotation
				vm.Transform.Rotation.Subscribe(rot => 
				{
					// Marshal property updates
					Callable.From(() => 
					{
						if (IsInstanceValid(visualNode))
						{
							visualNode.Rotation = (float)rot;
						}
					}).CallDeferred();
				}).AddTo(vm.Disposables);
			}
            else
            {
                 GD.PrintErr($"[EntityVisualizer] Failed to spawn visual for Entity {vm.Entity.Id}");
            }
		}).CallDeferred();
	}

	private void OnEntityRemoved(EntityViewModel vm)
	{
		Callable.From(() => 
		{
			if (_spawnedEntities.TryGetValue(vm, out var node))
			{
				if (IsInstanceValid(node))
				{
					node.QueueFree();
				}
				_spawnedEntities.Remove(vm);
			}
		}).CallDeferred();
	}

	private Node2D SpawnEntity(EntityViewModel vm)
	{
		// Determine type
		if (vm.IsPlayer)
		{
			if (PlayerPrefab != null)
				return PlayerPrefab.Instantiate<Node2D>();
			
			// Fallback: Simple Circle
			return CreateFallbackVisual(Colors.Green, 20f);
		}
		else if (vm.IsCoin)
		{
			if (CoinPrefab != null)
				return CoinPrefab.Instantiate<Node2D>();
				
			// Fallback: Simple Circle
			return CreateFallbackVisual(Colors.Gold, 10f);
		}

		// Default
		return CreateFallbackVisual(Colors.White, 10f);
	}

	private Node2D CreateFallbackVisual(Color color, float radius)
	{
		var node = new Node2D();
		
		var polygon = new Polygon2D();
		var points = new Vector2[8];
		for (int i = 0; i < 8; i++)
		{
			float angle = i * Mathf.Tau / 8;
			points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
		}
		polygon.Polygon = points;
		polygon.Color = color;
		
		node.AddChild(polygon);
		return node;
	}

	public override void _ExitTree()
	{
		_disposables.Dispose();
		_entities = null;
		
		foreach (var node in _spawnedEntities.Values)
		{
			if (IsInstanceValid(node))
				node.QueueFree();
		}
		_spawnedEntities.Clear();
	}
}
