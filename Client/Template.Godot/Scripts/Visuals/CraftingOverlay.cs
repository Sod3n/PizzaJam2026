using Godot;

namespace Template.Godot.Visuals;

/// <summary>
/// Full-screen overlay showing all milking-chain crafting recipes.
/// Toggled via the C key. Blocks game input while visible.
/// Dismisses on Escape or pressing C again.
/// Layout is fully static in the .tscn scene file.
/// </summary>
public partial class CraftingOverlay : CanvasLayer
{
    private static CraftingOverlay _current;

    /// <summary>True while the crafting overlay is on screen. Used to block game input.</summary>
    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    private static readonly PackedScene _scene =
        GD.Load<PackedScene>("res://Scenes/CraftingOverlay.tscn");

    // ── Static API ─────────────────────────────────────────────────────

    public static void Toggle(SceneTree tree)
    {
        if (IsActive)
        {
            _current._Dismiss();
            return;
        }
        Show(tree);
    }

    public static void Show(SceneTree tree)
    {
        if (IsActive) return;
        if (_scene == null) return;

        var overlay = _scene.Instantiate<CraftingOverlay>();
        _current = overlay;
        tree.Root.AddChild(overlay);
        overlay._FadeIn();
    }

    // ── Fade In ─────────────────────────────────────────────────────────

    private void _FadeIn()
    {
        var root = GetChild(0);
        if (root is CanvasItem ci)
        {
            ci.Modulate = new Color(1, 1, 1, 0);
            var tween = CreateTween();
            tween.TweenProperty(ci, "modulate:a", 1f, 0.2f)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
        }
    }

    // ── Input ──────────────────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        // Consume all input while active to block game
        GetViewport().SetInputAsHandled();

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.Escape || key.Keycode == Key.C)
            {
                _Dismiss();
                return;
            }
        }
    }

    // ── Dismiss ────────────────────────────────────────────────────────

    private void _Dismiss()
    {
        if (!IsInsideTree()) return;

        var root = GetChild(0);
        var tween = CreateTween();
        tween.TweenProperty(root, "modulate:a", 0f, 0.15f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            _current = null;
            QueueFree();
        }));
    }
}
