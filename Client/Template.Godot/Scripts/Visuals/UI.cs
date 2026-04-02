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

    // Nodes
    private RichTextLabel _grassLabel;
    private RichTextLabel _milkLabel;
    private RichTextLabel _coinLabel;
    private RichTextLabel _carrotLabel;
    private RichTextLabel _appleLabel;
    private RichTextLabel _mushroomLabel;
    private RichTextLabel _vitaminShakeLabel;
    private RichTextLabel _appleYogurtLabel;
    private RichTextLabel _purplePotionLabel;

    public override void _Ready()
    {
        // Cache nodes
        _grassLabel = GetNode<RichTextLabel>("Control/BottomBar/Grass");
        _milkLabel = GetNode<RichTextLabel>("Control/BottomBar/Milk");
        _coinLabel = GetNode<RichTextLabel>("Control/BottomBar/Coins");
        _carrotLabel = GetNode<RichTextLabel>("Control/BottomBar/Carrot");
        _appleLabel = GetNode<RichTextLabel>("Control/BottomBar/Apple");
        _mushroomLabel = GetNode<RichTextLabel>("Control/BottomBar/Mushroom");
        _vitaminShakeLabel = GetNode<RichTextLabel>("Control/BottomBar/VitaminShake");
        _appleYogurtLabel = GetNode<RichTextLabel>("Control/BottomBar/AppleYogurt");
        _purplePotionLabel = GetNode<RichTextLabel>("Control/BottomBar/PurplePotion");
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

        vm.Resources.VitaminShake.Subscribe(v =>
            Callable.From(() => _vitaminShakeLabel.Text = $"{v}").CallDeferred()
        ).AddTo(_disposables);

        vm.Resources.AppleYogurt.Subscribe(v =>
            Callable.From(() => _appleYogurtLabel.Text = $"{v}").CallDeferred()
        ).AddTo(_disposables);

        vm.Resources.PurplePotion.Subscribe(v =>
            Callable.From(() => _purplePotionLabel.Text = $"{v}").CallDeferred()
        ).AddTo(_disposables);
    }

    public override void _ExitTree()
    {
        _disposables.Dispose();
    }
}
