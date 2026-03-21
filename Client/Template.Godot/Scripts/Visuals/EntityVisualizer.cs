using Godot;
using System;
using System.Collections.Generic;
using Template.Godot.Core;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.DAR;
using ObservableCollections;
using R3;
using Template.Shared.Components;

// Alias to resolve ambiguity between Godot.Transform2D and Deterministic.GameFramework.TwoD.Transform2D
using DTransform2D = Deterministic.GameFramework.TwoD.Transform2D;
using GVector3 = Godot.Vector3;

namespace Template.Godot.Visuals;

public partial class EntityVisualizer : Node3D
{
	[Export] public PackedScene PlayerPrefab;
	[Export] public PackedScene CoinPrefab;
	[Export] public PackedScene CowPrefab;
	[Export] public PackedScene GrassPrefab;
	[Export] public PackedScene LandPrefab;
	[Export] public PackedScene HousePrefab;
	[Export] public PackedScene SellPointPrefab;

	private readonly CompositeDisposable _disposables = new();
	private readonly Dictionary<EntityViewModel, Node3D> _spawnedEntities = new();
	private bool _isInitialized;

	public override void _Process(double delta)
	{
		// 1. Wait for Game to Start
		if (_isInitialized) return;
		if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;

		Initialize();
	}

	private void Initialize()
	{
		_isInitialized = true;
		var client = GameManager.Instance.GameClient;

		// 2. Bind to Reactive Entity Lists
		// Separate lists for Players and Coins guarantees we only visualize what we expect
		
		// Players: Must have PlayerEntity and Transform2D
		var players = client.Reactive.ObservableList<Template.Shared.Components.PlayerEntity, DTransform2D, PlayerViewModel>(
			ctx => new PlayerViewModel(ctx),
			_disposables
		);
		
		// Coins: Must have CoinComponent and Transform2D
		var coins = client.Reactive.ObservableList<Template.Shared.Components.CoinComponent, DTransform2D, CoinViewModel>(
			ctx => new CoinViewModel(ctx),
			_disposables
		);

        // Cows: Must have CowComponent and Transform2D
        var cows = client.Reactive.ObservableList<Template.Shared.Components.CowComponent, DTransform2D, CowViewModel>(
            ctx => new CowViewModel(ctx),
            _disposables
        );

        // Grass: Must have GrassComponent and Transform2D
        var grass = client.Reactive.ObservableList<Template.Shared.Components.GrassComponent, DTransform2D, GrassViewModel>(
            ctx => new GrassViewModel(ctx),
            _disposables
        );

        // Land: Must have LandComponent and Transform2D
        var land = client.Reactive.ObservableList<Template.Shared.Components.LandComponent, DTransform2D, LandViewModel>(
            ctx => new LandViewModel(ctx),
            _disposables
        );

        // House: Must have HouseComponent and Transform2D
        var house = client.Reactive.ObservableList<Template.Shared.Components.HouseComponent, DTransform2D, HouseViewModel>(
            ctx => new HouseViewModel(ctx),
            _disposables
        );

        // SellPoint: Must have SellPointComponent and Transform2D
        var sellPoints = client.Reactive.ObservableList<Template.Shared.Components.SellPointComponent, DTransform2D, SellPointViewModel>(
            ctx => new SellPointViewModel(ctx),
            _disposables
        );

		// 3. Handle Add/Remove for Players
		players.ObserveAdd().Subscribe(e => OnEntityAdded(e.Value)).AddTo(_disposables);
		players.ObserveRemove().Subscribe(e => OnEntityRemoved(e.Value)).AddTo(_disposables);
		players.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);

		// 4. Handle Add/Remove for Coins
		coins.ObserveAdd().Subscribe(e => OnEntityAdded(e.Value)).AddTo(_disposables);
		coins.ObserveRemove().Subscribe(e => OnEntityRemoved(e.Value)).AddTo(_disposables);
		coins.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);

        // 5. Handle Add/Remove for New Entities
        cows.ObserveAdd().Subscribe(e => OnEntityAdded(e.Value)).AddTo(_disposables);
        cows.ObserveRemove().Subscribe(e => OnEntityRemoved(e.Value)).AddTo(_disposables);
        cows.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);

        grass.ObserveAdd().Subscribe(e => OnEntityAdded(e.Value)).AddTo(_disposables);
        grass.ObserveRemove().Subscribe(e => OnEntityRemoved(e.Value)).AddTo(_disposables);
        grass.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);

        land.ObserveAdd().Subscribe(e => OnEntityAdded(e.Value)).AddTo(_disposables);
        land.ObserveRemove().Subscribe(e => OnEntityRemoved(e.Value)).AddTo(_disposables);
        land.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);

        house.ObserveAdd().Subscribe(e => OnEntityAdded(e.Value)).AddTo(_disposables);
        house.ObserveRemove().Subscribe(e => OnEntityRemoved(e.Value)).AddTo(_disposables);
        house.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);

        sellPoints.ObserveAdd().Subscribe(e => OnEntityAdded(e.Value)).AddTo(_disposables);
        sellPoints.ObserveRemove().Subscribe(e => OnEntityRemoved(e.Value)).AddTo(_disposables);
        sellPoints.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);

		// Initialize any existing
		foreach (var vm in players) OnEntityAdded(vm);
		foreach (var vm in coins) OnEntityAdded(vm);
        foreach (var vm in cows) OnEntityAdded(vm);
        foreach (var vm in grass) OnEntityAdded(vm);
        foreach (var vm in land) OnEntityAdded(vm);
        foreach (var vm in house) OnEntityAdded(vm);
        foreach (var vm in sellPoints) OnEntityAdded(vm);
	}

	private void OnEntityAdded(EntityViewModel vm)
	{
		// Ensure we run on Main Thread
		Callable.From(() => SpawnEntityDeferred(vm)).CallDeferred();
	}

	private void OnEntityRemoved(EntityViewModel vm)
	{
		Callable.From(() => DespawnEntityDeferred(vm)).CallDeferred();
	}

	private void SpawnEntityDeferred(EntityViewModel vm)
	{
		if (_spawnedEntities.ContainsKey(vm)) return;

		// 1. Create Visual
		Node3D visualNode = null;
		
		if (vm is PlayerViewModel playerVm)
		{
			visualNode = PlayerPrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.Green, 0.5f);
			if (GameManager.Instance.LocalPlayerId != playerVm.Entity.Id) visualNode.GetNode<Camera3D>("Camera").QueueFree();

			// Handle Skins
			if (playerVm.Skin != null)
			{
				playerVm.Skin.Skins.Subscribe(skins => 
				{
					SkinVisualizer.UpdateSkins(visualNode, skins);
				}).AddTo(vm.Disposables);
			}
		}
		else if (vm is CoinViewModel coinVm)
		{
			visualNode = CoinPrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.Gold, 0.3f);
		}
        else if (vm is CowViewModel cowVm)
        {
            visualNode = CowPrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.SaddleBrown, 0.6f);
            
            // Handle Skins for Cow
            if (cowVm.Skin != null)
            {
                cowVm.Skin.Skins.Subscribe(skins => 
                {
                    SkinVisualizer.UpdateSkins(visualNode, skins);
                }).AddTo(vm.Disposables);
            }
        }
        else if (vm is GrassViewModel grassVm)
        {
            visualNode = GrassPrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.DarkGreen, 0.4f);
        }
        else if (vm is LandViewModel landVm)
        {
            visualNode = LandPrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.SandyBrown, 1.0f);
        }
        else if (vm is HouseViewModel houseVm)
        {
            visualNode = HousePrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.Red, 1.2f);
        }
        else if (vm is SellPointViewModel sellPointVm)
        {
            visualNode = SellPointPrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.Yellow, 0.5f);
        }
		else
		{
			// Generic Entity with Transform
			visualNode = CreateFallbackVisual(Colors.White, 0.3f);
		}

		// 2. Setup
		AddChild(visualNode);
		_spawnedEntities[vm] = visualNode;

		// 3. Bind Position (Reactive)
		vm.Transform.Position.Subscribe(pos => 
		{
			// Move visual
			// We use CallDeferred to ensure thread safety when coming from network thread
			Callable.From(() => 
			{
				if (IsInstanceValid(visualNode))
				{
					// Simple interpolation could go here, for now direct set with tween
					var tween = visualNode.CreateTween();
					tween.TweenProperty(visualNode, "position", new GVector3((float)pos.X, 0f, (float)pos.Y), 0.1f);
				}
			}).CallDeferred();
		}).AddTo(vm.Disposables);
	}

	private void DespawnEntityDeferred(EntityViewModel vm)
	{
		if (_spawnedEntities.TryGetValue(vm, out var node))
		{
			if (IsInstanceValid(node)) node.QueueFree();
			_spawnedEntities.Remove(vm);
		}
	}

	private void OnListReset()
	{
		// Clear all
		foreach (var vm in _spawnedEntities.Keys)
		{
			OnEntityRemoved(vm);
		}
	}

	private Node3D CreateFallbackVisual(Color color, float radius)
	{
		var node = new Node3D();
		
		// Create a simple mesh instance for 3D or Polygon for 2D? 
		// Since this is Node3D, let's make a MeshInstance3D with a Cylinder
		var meshInstance = new MeshInstance3D();
		var mesh = new CylinderMesh();
		mesh.TopRadius = radius;
		mesh.BottomRadius = radius;
		mesh.Height = 0.1f;
		
		var material = new StandardMaterial3D();
		material.AlbedoColor = color;
		mesh.Material = material;
		
		meshInstance.Mesh = mesh;
		node.AddChild(meshInstance);
		
		return node;
	}

	public override void _ExitTree()
	{
		_disposables.Dispose();
		_spawnedEntities.Clear();
	}
}
