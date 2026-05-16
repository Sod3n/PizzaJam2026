using Godot;
using System.Collections.Generic;
using Template.Godot.Core;
using Template.Godot.GameResources;
using Template.Shared.Components;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using ObservableCollections;
using R3;
using Template.Godot.Visuals;

public partial class UI : CanvasLayer
{
    private CompositeDisposable _disposables = new();
    private bool _isInitialized = false;

    private readonly Dictionary<string, NumberRoller> _rollers = new();
    private Label _sessionTimeLabel;
    private HintPopup _hintPopup;
    private Resource _iconSet;

    public override void _Ready()
    {
        Setup("grass",      "%GrassValue",          "%GrassIcon");
        Setup("milk",       "%MilkValue",           "%MilkIcon");
        Setup("coin",       "%MoneyValue",          "%MoneyIcon");
        Setup("carrot",     "%CarrotValue",         "%CarrotIcon");
        Setup("apple",      "%AppleValue",          "%AppleIcon");
        Setup("mushroom",   "%MushroomValue",       "%MushroomIcon");
        Setup("milkshake",  "%CarrotMilkshakeValue","%CarrotMilkshakeIcon");
        Setup("vitamin",    "%VitaminMixValue",     "%VitaminMixIcon");
        Setup("potion",     "%PurplePotionValue",   "%PurplePotionIcon");

        Setup("houses",     "%HousesValue",         null);
        Setup("cows",       "%CowsValue",           null);
        Setup("helpers",    "%HelpersValue",        null);
        Setup("cumFood",    "%CumFoodValue",        null);
        Setup("cumMilk",    "%CumMilkValue",        null);
        Setup("cumCoins",   "%CumCoinsValue",       null);

        _sessionTimeLabel = GetNodeOrNull<Label>("%SessionTimeValue");
        _hintPopup = GetNodeOrNull<HintPopup>("%HintPopup");
        _iconSet = ResourceLoader.Load<Resource>("res://Resources/LandTypeIcons.tres");

        AddChild(new HelperRoleSelectorsController());
    }

    private void Setup(string key, string labelPath, string iconPath)
    {
        var label = GetNodeOrNull<Label>(labelPath);
        if (label == null) return;
        var icon = iconPath != null ? GetNodeOrNull<Control>(iconPath) : null;
        _rollers[key] = new NumberRoller(label, icon);
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

        var resources = client.Reactive.ObservableList<GlobalResourcesComponent, GlobalResourcesComponentViewModel>(
            ctx => new GlobalResourcesComponentViewModel(ctx),
            _disposables);
        resources.ObserveAdd().Subscribe(evt => BindResources(evt.Value)).AddTo(_disposables);
        foreach (var vm in resources) BindResources(vm);

        var metrics = client.Reactive.ObservableList<MetricsComponent, MetricsComponentViewModel>(
            ctx => new MetricsComponentViewModel(ctx),
            _disposables);
        metrics.ObserveAdd().Subscribe(evt => BindMetrics(evt.Value)).AddTo(_disposables);
        foreach (var vm in metrics) BindMetrics(vm);

        ReactiveSystem.Instance.ObserveAdd<InteractHighlightComponent>()
            .Subscribe(entity =>
            {
                _hintSubscription?.Dispose();
                var state = ReactiveSystem.Instance?.BoundState;
                if (state != null)
                {
                    _hintSubscription = ReactiveSystem.Instance.SubscribeComponent<InteractHighlightComponent>(
                        state, entity,
                        hl => Callable.From(() => ApplyHint(hl)).CallDeferred());
                }
            })
            .AddTo(_disposables);

        ReactiveSystem.Instance.ObserveRemove<InteractHighlightComponent>()
            .Subscribe(_ =>
            {
                _hintSubscription?.Dispose();
                _hintSubscription = null;
                _hasLastHint = false;
            })
            .AddTo(_disposables);
    }

    private System.IDisposable _hintSubscription;
    private LandType _lastHintType;
    private bool _hasLastHint;

    private void ApplyHint(InteractHighlightComponent hl)
    {
        if (_hintPopup == null) return;
        if (!hl.HasHintLandType) return;
        var type = hl.HintLandType;
        if (_hasLastHint && _lastHintType == type) return;
        if (!BuildingHints.TryGet(type, out var text)) return;

        _lastHintType = type;
        _hasLastHint = true;
        _hintPopup.SetText(text);
        if (_iconSet != null && LandTypeIcons.TryGet(_iconSet, type, out var icon))
            _hintPopup.SetIcon(icon);
    }

    private void BindResources(GlobalResourcesComponentViewModel vm)
    {
        Roll("grass",     vm.Resources.Grass);
        Roll("milk",      vm.Resources.Milk);
        Roll("coin",      vm.Resources.Coins);
        Roll("carrot",    vm.Resources.Carrot);
        Roll("apple",     vm.Resources.Apple);
        Roll("mushroom",  vm.Resources.Mushroom);
        Roll("milkshake", vm.Resources.CarrotMilkshake);
        Roll("vitamin",   vm.Resources.VitaminMix);
        Roll("potion",    vm.Resources.PurplePotion);
    }

    private void BindMetrics(MetricsComponentViewModel vm)
    {
        Roll("houses",   vm.Metrics.Houses);
        Roll("cows",     vm.Metrics.Cows);
        Roll("helpers",  vm.Metrics.Helpers);
        Roll("cumFood",  vm.Metrics.CumFood);
        Roll("cumMilk",  vm.Metrics.CumMilk);
        Roll("cumCoins", vm.Metrics.CumCoins);

        if (_sessionTimeLabel != null)
            vm.Metrics.ElapsedTicks.Subscribe(ticks =>
                Callable.From(() =>
                {
                    int totalSec = ticks / 60;
                    _sessionTimeLabel.Text = $"{totalSec / 60}:{totalSec % 60:D2}";
                }).CallDeferred()
            ).AddTo(_disposables);
    }

    private void Roll(string key, Observable<int> source)
    {
        if (!_rollers.TryGetValue(key, out var roller)) return;
        source.Subscribe(v =>
            Callable.From(() => roller.SetValue(v)).CallDeferred()
        ).AddTo(_disposables);
    }

    public override void _ExitTree() => _disposables.Dispose();
}
