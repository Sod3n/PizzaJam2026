using Godot;
using System;
using System.Collections.Generic;
using Template.Godot.Core;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.DAR;
using ObservableCollections;
using R3;
using Template.Shared;
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
    [Export] public PackedScene FinalStructurePrefab;
    [Export] public PackedScene NotEnoughResourcePrefab;
    [Export] public PackedScene WallPrefab;

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

        // FinalStructure: Must have FinalStructureComponent and Transform2D
        var finalStructures = client.Reactive.ObservableList<Template.Shared.Components.FinalStructureComponent, DTransform2D, FinalStructureViewModel>(
            ctx => new FinalStructureViewModel(ctx),
            _disposables
        );

        // Walls: Must have WallComponent and Transform2D
        var walls = client.Reactive.ObservableList<Template.Shared.Components.WallComponent, DTransform2D, WallViewModel>(
            ctx => new WallViewModel(ctx),
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

        finalStructures.ObserveAdd().Subscribe(e => OnEntityAdded(e.Value)).AddTo(_disposables);
        finalStructures.ObserveRemove().Subscribe(e => OnEntityRemoved(e.Value)).AddTo(_disposables);
        finalStructures.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);

        walls.ObserveAdd().Subscribe(e => OnEntityAdded(e.Value)).AddTo(_disposables);
        walls.ObserveRemove().Subscribe(e => OnEntityRemoved(e.Value)).AddTo(_disposables);
        walls.ObserveReset().Subscribe(_ => OnListReset()).AddTo(_disposables);

        // Initialize any existing
        foreach (var vm in players) OnEntityAdded(vm);
        foreach (var vm in coins) OnEntityAdded(vm);
        foreach (var vm in cows) OnEntityAdded(vm);
        foreach (var vm in grass) OnEntityAdded(vm);
        foreach (var vm in land) OnEntityAdded(vm);
        foreach (var vm in house) OnEntityAdded(vm);
        foreach (var vm in sellPoints) OnEntityAdded(vm);
        foreach (var vm in finalStructures) OnEntityAdded(vm);
        foreach (var vm in walls) OnEntityAdded(vm);
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

            // Create a FlipPivot node and reparent Character under it
            var characterNode = visualNode.GetNodeOrNull<Node3D>("Character");
            var flipPivot = new Node3D { Name = "FlipPivot" };
            if (characterNode != null)
            {
                var charTransform = characterNode.Transform;
                visualNode.RemoveChild(characterNode);
                visualNode.AddChild(flipPivot);
                flipPivot.Transform = charTransform;
                characterNode.Transform = global::Godot.Transform3D.Identity;
                flipPivot.AddChild(characterNode);
            }

            // Handle Skins
            if (playerVm.Skin != null)
            {
                playerVm.Skin.Skins.Subscribe(skins =>
                {
                    SkinVisualizer.UpdateSkins(visualNode, skins);
                }).AddTo(vm.Disposables);
            }

            playerVm.Player.CharacterBody2D.Velocity.Subscribe(v =>
            {
                Callable.From(() =>
                {
                    var isMoving = !v.IsZeroApprox();
                    characterNode?.SetDeferred("enable_bounce", isMoving);

                    // Flip on X based on horizontal movement direction
                    if (v.X < 0)
                        flipPivot.Scale = new GVector3(-Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
                    else if (v.X > 0)
                        flipPivot.Scale = new GVector3(Mathf.Abs(flipPivot.Scale.X), flipPivot.Scale.Y, flipPivot.Scale.Z);
                }).CallDeferred();
            }).AddTo(vm.Disposables);

            playerVm.IsHidden.Subscribe(hidden =>
            {
                if (IsInstanceValid(visualNode)) visualNode.SetDeferred("visible", !hidden);
            }).AddTo(vm.Disposables);
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

            cowVm.IsHidden.Subscribe(hidden =>
            {
                if (IsInstanceValid(visualNode)) visualNode.SetDeferred("visible", !hidden);
            }).AddTo(vm.Disposables);
        }
        else if (vm is GrassViewModel grassVm)
        {
            visualNode = GrassPrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.DarkGreen, 0.4f);
        }
        else if (vm is LandViewModel landVm)
        {
            visualNode = LandPrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.SandyBrown, 1.0f);

            landVm.Remaining.Subscribe(x =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(visualNode)) return;

                    var remainingLabel = visualNode.GetNodeOrNull<Label3D>("Remaining");
                    remainingLabel?.SetDeferred("text", x.ToString());
                }).CallDeferred();
            }).AddTo(vm.Disposables);
        }
        else if (vm is HouseViewModel houseVm)
        {
            visualNode = HousePrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.Red, 1.2f);

            houseVm.Capacity.Subscribe(c =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(visualNode)) return;

                    var capacityLabel = visualNode.GetNodeOrNull<Label3D>("Capacity");
                    capacityLabel?.SetDeferred("text", c);
                }).CallDeferred();
            }).AddTo(vm.Disposables);
        }
        else if (vm is SellPointViewModel sellPointVm)
        {
            visualNode = SellPointPrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.Yellow, 0.5f);
        }
        else if (vm is FinalStructureViewModel finalStructureVm)
        {
            visualNode = FinalStructurePrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.Blue, 1.0f);
            finalStructureVm.Remaining.Subscribe(x =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(visualNode)) return;

                    if (x <= 0)
                    {
                        var node = visualNode.GetNodeOrNull<Node3D>("Node3D");
                        var finale = visualNode.GetNodeOrNull<Node3D>("Finale");
                        node.SetDeferred("visible", false);
                        finale.SetDeferred("visible", true);
                        var global = GetTree().Root.GetNode("Global");
                        global.EmitSignal("on_finale");
                        return;
                    }

                    var remainingLabel = visualNode.GetNodeOrNull<Label3D>("Node3D/Remaining");
                    remainingLabel?.SetDeferred("text", x.ToString());
                }).CallDeferred();
            }).AddTo(vm.Disposables);
        }
        else if (vm is WallViewModel wallVm)
        {
            visualNode = WallPrefab?.Instantiate<Node3D>() ?? CreateFallbackVisual(Colors.DarkGray, 0.5f);
        }
        else
        {
            // Generic Entity with Transform
            visualNode = CreateFallbackVisual(Colors.White, 0.3f);
        }

        // 2. Setup
        AddChild(visualNode);
        _spawnedEntities[vm] = visualNode;
        var pos = vm.Transform.Position.CurrentValue;
        visualNode.SetDeferred("position", new GVector3((float)pos.X, 0f, (float)pos.Y));

        // 3. Bind Position (Reactive)
        vm.Transform.Position.Subscribe(p =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(visualNode)) return;

                var tween = visualNode.CreateTween();
                var nextPos = new GVector3((float)p.X, 0f, (float)p.Y);
                tween.TweenProperty(visualNode, "position", nextPos, 0.1f);
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        vm.OnInteract.Subscribe(_ =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(visualNode)) return;
                var animate_node = visualNode;
                if (vm is HouseViewModel houseVm)
                {
                    animate_node = animate_node.GetNodeOrNull<Node3D>("AnimatedSprite3D");
                }
                if (animate_node.GetMeta("scale_tween").Obj is Tween tw) tw.SetSpeedScale(100000f);

                var tween = animate_node.CreateTween();

                var origScale = animate_node.GetMeta("orig_scale").Obj is Vector3 s ? s : animate_node.Scale;
                if (animate_node.GetMeta("orig_scale").Obj == null) animate_node.SetMeta("orig_scale", animate_node.Scale);
                tween.SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);

                tween.TweenProperty(animate_node, "scale", new Vector3(origScale.X * 1.4f, origScale.Y * 0.7f, origScale.Z), 0.1f);
                tween.Chain().TweenProperty(animate_node, "scale", origScale, 0.1f);
                animate_node.SetMeta("scale_tween", tween);
            }).CallDeferred();
        }).AddTo(vm.Disposables);

        vm.OnNotEnoughResource.Subscribe(resourceKey =>
        {
            Callable.From(() =>
            {
                if (!IsInstanceValid(visualNode)) return;
                if (NotEnoughResourcePrefab == null) return;

                var instance = NotEnoughResourcePrefab.Instantiate<NotEnoughResourceView>();
                AddChild(instance);
                instance.Position = visualNode.Position;
                instance.Setup(resourceKey);
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
