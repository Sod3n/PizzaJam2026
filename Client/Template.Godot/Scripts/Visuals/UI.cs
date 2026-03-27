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

    public override void _Ready()
    {
        // Cache nodes
        _grassLabel = GetNode<RichTextLabel>("Control/Grass");
        _milkLabel = GetNode<RichTextLabel>("Control/Milk");
        _coinLabel = GetNode<RichTextLabel>("Control/Coins");
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
        // Bind Grass
        vm.Resources.Grass.Subscribe(g => 
            Callable.From(() => _grassLabel.Text = $"{g}").CallDeferred()
        ).AddTo(_disposables);

        // Bind Milk
        vm.Resources.Milk.Subscribe(m => 
                Callable.From(() => _milkLabel.Text = $"{m}").CallDeferred()
        ).AddTo(_disposables);

        // Bind Coins
        vm.Resources.Coins.Subscribe(c => 
                Callable.From(() => _coinLabel.Text = $"{c}").CallDeferred()
        ).AddTo(_disposables);
    }

    public override void _ExitTree()
    {
        _disposables.Dispose();
    }
}
