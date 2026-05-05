using Godot;
using System;
using System.Threading.Tasks;
using Template.Godot.Core;
using Template.Shared.Components;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.DAR;
using ObservableCollections;
using R3;
using Template.Godot.Visuals;

public partial class UI : CanvasLayer
{
    private CompositeDisposable _disposables = new();
    private bool _isInitialized = false;

    // Resource nodes
    private RichTextLabel _grassLabel;
    private RichTextLabel _milkLabel;
    private RichTextLabel _coinLabel;
    private RichTextLabel _carrotLabel;
    private RichTextLabel _appleLabel;
    private RichTextLabel _mushroomLabel;
    private RichTextLabel _carrotMilkshakeLabel;
    private RichTextLabel _vitaminMixLabel;
    private RichTextLabel _purplePotionLabel;

    // Metrics nodes (optional — only bound if present in scene tree)
    private RichTextLabel _housesLabel;
    private RichTextLabel _cowsLabel;
    private RichTextLabel _helpersLabel;
    private RichTextLabel _cumFoodLabel;
    private RichTextLabel _cumMilkLabel;
    private RichTextLabel _cumCoinsLabel;
    private RichTextLabel _sessionTimeLabel;

    public override void _Ready()
    {
        // Cache nodes
        _grassLabel = GetNode<RichTextLabel>("Control/BottomBar/Grass");
        _milkLabel = GetNode<RichTextLabel>("Control/BottomBar/Milk");
        _coinLabel = GetNode<RichTextLabel>("Control/BottomBar/Coins");
        _carrotLabel = GetNode<RichTextLabel>("Control/BottomBar/Carrot");
        _appleLabel = GetNode<RichTextLabel>("Control/BottomBar/Apple");
        _mushroomLabel = GetNode<RichTextLabel>("Control/BottomBar/Mushroom");
        _carrotMilkshakeLabel = GetNode<RichTextLabel>("Control/BottomBar/CarrotMilkshake");
        _vitaminMixLabel = GetNode<RichTextLabel>("Control/BottomBar/VitaminMix");
        _purplePotionLabel = GetNode<RichTextLabel>("Control/BottomBar/PurplePotion");

        // Metrics labels (optional — null if not in scene)
        _housesLabel = GetNodeOrNull<RichTextLabel>("Control/BottomBar/Houses");
        _cowsLabel = GetNodeOrNull<RichTextLabel>("Control/BottomBar/Cows");
        _helpersLabel = GetNodeOrNull<RichTextLabel>("Control/BottomBar/Helpers");
        _cumFoodLabel = GetNodeOrNull<RichTextLabel>("Control/BottomBar/CumFood");
        _cumMilkLabel = GetNodeOrNull<RichTextLabel>("Control/BottomBar/CumMilk");
        _cumCoinsLabel = GetNodeOrNull<RichTextLabel>("Control/BottomBar/CumCoins");
        _sessionTimeLabel = GetNodeOrNull<RichTextLabel>("Control/BottomBar/SessionTime");
    }

    public override void _Process(double delta)
    {
        if (_isInitialized) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;

        _isInitialized = true;
        Initialize();
    }

    private void Initialize()
    {
        var client = GameManager.Instance.GameClient;
        var collection = client.Reactive.ObservableList<GlobalResourcesComponent, GlobalResourcesComponentViewModel>(
            ctx => new GlobalResourcesComponentViewModel(ctx),
            _disposables
            );

        // 1. Handle Future Adds
        collection.ObserveAdd().Subscribe(evt =>
        {
            BindResources(evt.Value);
        }).AddTo(_disposables);

        // 2. Handle Existing
        foreach (var vm in collection)
        {
            BindResources(vm);
        }

        // Bind metrics (entity counts, cumulative production, session time)
        var metricsCollection = client.Reactive.ObservableList<MetricsComponent, MetricsComponentViewModel>(
            ctx => new MetricsComponentViewModel(ctx),
            _disposables
        );

        metricsCollection.ObserveAdd().Subscribe(evt =>
        {
            BindMetrics(evt.Value);
        }).AddTo(_disposables);

        foreach (var vm in metricsCollection)
        {
            BindMetrics(vm);
        }
    }

    private void BindResources(GlobalResourcesComponentViewModel vm)
    {
        vm.Resources.Grass.Subscribe(g =>
            Callable.From(() => _grassLabel.Text = $"{g}").CallDeferred()
        ).AddTo(_disposables);

        vm.Resources.Milk.Subscribe(m =>
                Callable.From(() => _milkLabel.Text = $"{m}").CallDeferred()
        ).AddTo(_disposables);

        vm.Resources.Coins.Subscribe(c =>
                Callable.From(() => _coinLabel.Text = $"{c}").CallDeferred()
        ).AddTo(_disposables);

        vm.Resources.Carrot.Subscribe(v =>
            Callable.From(() => _carrotLabel.Text = $"{v}").CallDeferred()
        ).AddTo(_disposables);

        vm.Resources.Apple.Subscribe(v =>
            Callable.From(() => _appleLabel.Text = $"{v}").CallDeferred()
        ).AddTo(_disposables);

        vm.Resources.Mushroom.Subscribe(v =>
            Callable.From(() => _mushroomLabel.Text = $"{v}").CallDeferred()
        ).AddTo(_disposables);

        vm.Resources.CarrotMilkshake.Subscribe(v =>
            Callable.From(() => _carrotMilkshakeLabel.Text = $"{v}").CallDeferred()
        ).AddTo(_disposables);

        vm.Resources.VitaminMix.Subscribe(v =>
            Callable.From(() => _vitaminMixLabel.Text = $"{v}").CallDeferred()
        ).AddTo(_disposables);

        vm.Resources.PurplePotion.Subscribe(v =>
            Callable.From(() => _purplePotionLabel.Text = $"{v}").CallDeferred()
        ).AddTo(_disposables);
    }

    private void BindMetrics(MetricsComponentViewModel vm)
    {
        if (_housesLabel != null)
            vm.Metrics.Houses.Subscribe(v =>
                Callable.From(() => _housesLabel.Text = $"{v}").CallDeferred()
            ).AddTo(_disposables);

        if (_cowsLabel != null)
            vm.Metrics.Cows.Subscribe(v =>
                Callable.From(() => _cowsLabel.Text = $"{v}").CallDeferred()
            ).AddTo(_disposables);

        if (_helpersLabel != null)
            vm.Metrics.Helpers.Subscribe(v =>
                Callable.From(() => _helpersLabel.Text = $"{v}").CallDeferred()
            ).AddTo(_disposables);

        if (_cumFoodLabel != null)
            vm.Metrics.CumFood.Subscribe(v =>
                Callable.From(() => _cumFoodLabel.Text = $"{v}").CallDeferred()
            ).AddTo(_disposables);

        if (_cumMilkLabel != null)
            vm.Metrics.CumMilk.Subscribe(v =>
                Callable.From(() => _cumMilkLabel.Text = $"{v}").CallDeferred()
            ).AddTo(_disposables);

        if (_cumCoinsLabel != null)
            vm.Metrics.CumCoins.Subscribe(v =>
                Callable.From(() => _cumCoinsLabel.Text = $"{v}").CallDeferred()
            ).AddTo(_disposables);

        if (_sessionTimeLabel != null)
            vm.Metrics.ElapsedTicks.Subscribe(ticks =>
                Callable.From(() =>
                {
                    int totalSec = ticks / 60;
                    int min = totalSec / 60;
                    int sec = totalSec % 60;
                    _sessionTimeLabel.Text = $"{min}:{sec:D2}";
                }).CallDeferred()
            ).AddTo(_disposables);
    }

    public override void _ExitTree()
    {
        _disposables.Dispose();
    }
}
