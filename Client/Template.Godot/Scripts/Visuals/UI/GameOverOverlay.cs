using Godot;
using Deterministic.GameFramework.Reactive;
using R3;
using Template.Godot.Core;
using Template.Shared;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class GameOverOverlay : CanvasLayer
{
    private static GameOverOverlay _current;

    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    private static readonly PackedScene _scene =
        GD.Load<PackedScene>("res://Scenes/GameOverOverlay.tscn");

    private static readonly CompositeDisposable _watchers = new();
    private static bool _installed;

    private Label _reasonLabel;
    private Button _returnBtn;

    public static void InstallWatcher()
    {
        if (_installed) return;
        _installed = true;
        ReactiveSystem.Instance.ObserveAdd<EnterStateComponent>()
            .Subscribe(entity =>
            {
                var s = ReactiveSystem.Instance.BoundState;
                if (s == null || !s.HasComponent<EnterStateComponent>(entity)) return;
                var st = s.GetComponent<EnterStateComponent>(entity);
                if (st.Key != StateKeys.GameOver) return;
                var gm = GameManager.Instance;
                if (gm == null || entity.Id != gm.LocalPlayerId) return;
                var tree = gm.GetTree();
                if (tree == null) return;
                string reason = st.Param.ToString() ?? "";
                Callable.From(() => Show(tree, reason)).CallDeferred();
            }).AddTo(_watchers);
    }

    public static void Show(SceneTree tree, string reason)
    {
        if (_current != null && Node.IsInstanceValid(_current)) return;
        if (_scene == null) return;

        var overlay = _scene.Instantiate<GameOverOverlay>();
        _current = overlay;
        overlay.ProcessMode = ProcessModeEnum.Always;
        tree.Root.AddChild(overlay);
        overlay._Setup(reason);

        tree.Paused = true;
    }

    private void _Setup(string reason)
    {
        _reasonLabel = GetNode<Label>("%ReasonLabel");
        _returnBtn = GetNode<Button>("%ReturnBtn");

        _reasonLabel.Text = string.IsNullOrEmpty(reason) ? "" : ReasonText(reason);
        _returnBtn.Pressed += OnReturnPressed;
    }

    private static string ReasonText(string reason) => reason switch
    {
        "caught" => "A cow caught you.",
        "caught_sleeping" => "A cow caught you sleeping.",
        _ => reason,
    };

    private void OnReturnPressed()
    {
        var tree = GetTree();
        if (tree != null) tree.Paused = false;
        _current = null;

        var gm = GameManager.Instance;
        if (gm != null) gm.EndSessionAndReturnToLobbyMenu();

        QueueFree();
    }
}
