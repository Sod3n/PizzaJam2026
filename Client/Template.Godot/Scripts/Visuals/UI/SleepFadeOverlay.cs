using Godot;
using R3;
using Deterministic.GameFramework.Reactive;
using Template.Godot.Core;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class SleepFadeOverlay : CanvasLayer
{
    private const float FadeInSeconds = 0.35f;
    private const float FadeOutSeconds = 0.6f;

    private static SleepFadeOverlay _current;
    private static readonly CompositeDisposable _watchers = new();
    private static bool _installed;

    private ColorRect _rect;
    private Tween _tween;

    public static void InstallWatcher()
    {
        if (_installed) return;
        _installed = true;

        ReactiveSystem.Instance.ObserveAdd<SleepingComponent>()
            .Subscribe(entity =>
            {
                var gm = GameManager.Instance;
                if (gm == null || entity.Id != gm.LocalPlayerId) return;
                Callable.From(() => ShowAndFadeIn(gm.GetTree())).CallDeferred();
            }).AddTo(_watchers);

        ReactiveSystem.Instance.ObserveRemove<SleepingComponent>()
            .Subscribe(entity =>
            {
                var gm = GameManager.Instance;
                if (gm == null || entity.Id != gm.LocalPlayerId) return;
                Callable.From(FadeOutAndRemove).CallDeferred();
            }).AddTo(_watchers);
    }

    private static void ShowAndFadeIn(SceneTree tree)
    {
        if (tree?.Root == null) return;
        if (_current != null && IsInstanceValid(_current)) { _current.FadeIn(); return; }

        var overlay = new SleepFadeOverlay { Layer = 1000 };
        _current = overlay;
        tree.Root.AddChild(overlay);
        overlay.BuildRect();
        overlay.FadeIn();
    }

    private static void FadeOutAndRemove()
    {
        if (_current == null || !IsInstanceValid(_current)) return;
        _current.FadeOut();
    }

    private void BuildRect()
    {
        _rect = new ColorRect
        {
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
    }

    private void FadeIn()
    {
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_rect, "color:a", 1.0f, FadeInSeconds)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
    }

    private void FadeOut()
    {
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_rect, "color:a", 0.0f, FadeOutSeconds)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _tween.TweenCallback(Callable.From(() =>
        {
            if (_current == this) _current = null;
            QueueFree();
        }));
    }
}
