using Godot;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

/// <summary>
/// Simple overlay that displays when a cow in love is interacted with.
/// Shows "Hey I really like {target cow name}!" and dismisses on any input.
/// </summary>
public partial class LovePopupOverlay : CanvasLayer
{
    private static LovePopupOverlay _current;

    /// <summary>True while the love popup is on screen. Used to block game input.</summary>
    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    private static readonly Texture2D _heartTexture =
        GD.Load<Texture2D>("res://sprites/heart.png");

    /// <summary>
    /// Show a love popup for the given cow entity.
    /// </summary>
    public static void Show(SceneTree tree, Entity loverEntity, string targetCowName)
    {
        // Dismiss any existing overlay
        if (_current != null && Node.IsInstanceValid(_current))
            _current.QueueFree();

        // Get the lover cow's name
        string loverName = "???";
        var state = ReactiveSystem.Instance.BoundState;
        if (state != null && state.HasComponent<NameComponent>(loverEntity))
            loverName = state.GetComponent<NameComponent>(loverEntity).Name.ToString();

        var overlay = new LovePopupOverlay();
        _current = overlay;
        overlay.Layer = 100;
        tree.Root.AddChild(overlay);
        overlay._Setup(loverName, targetCowName);
    }

    private void _Setup(string loverName, string targetName)
    {
        var panel = new PanelContainer();
        panel.Name = "Panel";
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(420, 0);

        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.15f, 0.05f, 0.1f, 0.92f);
        styleBox.CornerRadiusTopLeft = 16;
        styleBox.CornerRadiusTopRight = 16;
        styleBox.CornerRadiusBottomLeft = 16;
        styleBox.CornerRadiusBottomRight = 16;
        styleBox.ContentMarginLeft = 32;
        styleBox.ContentMarginRight = 32;
        styleBox.ContentMarginTop = 24;
        styleBox.ContentMarginBottom = 24;
        panel.AddThemeStyleboxOverride("panel", styleBox);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        // Heart icon row
        if (_heartTexture != null)
        {
            var heartRect = new TextureRect();
            heartRect.Texture = _heartTexture;
            heartRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            heartRect.CustomMinimumSize = new Vector2(64, 64);
            heartRect.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            vbox.AddChild(heartRect);
        }

        // Cow name header
        var nameLabel = new Label();
        nameLabel.Text = loverName;
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nameLabel.AddThemeFontSizeOverride("font_size", 28);
        nameLabel.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.8f));
        vbox.AddChild(nameLabel);

        // Love message
        var msgLabel = new Label();
        msgLabel.Text = $"Hey I really like {targetName}!";
        msgLabel.HorizontalAlignment = HorizontalAlignment.Center;
        msgLabel.AddThemeFontSizeOverride("font_size", 22);
        msgLabel.AddThemeColorOverride("font_color", Colors.White);
        msgLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(msgLabel);

        // Hint
        var hintLabel = new Label();
        hintLabel.Text = "Breed them together for a guaranteed upgrade!";
        hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        hintLabel.AddThemeFontSizeOverride("font_size", 14);
        hintLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.5f, 0.55f));
        hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(hintLabel);

        var dismissHint = new Label();
        dismissHint.Text = "Tap to dismiss";
        dismissHint.HorizontalAlignment = HorizontalAlignment.Center;
        dismissHint.AddThemeFontSizeOverride("font_size", 14);
        dismissHint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.55f));
        vbox.AddChild(dismissHint);

        // Full-screen background
        var background = new ColorRect();
        background.Color = new Color(0.1f, 0, 0.05f, 0.4f);
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(root);
        root.AddChild(background);
        root.AddChild(panel);

        // Fade in
        root.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(root, "modulate:a", 1f, 0.2f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    public override void _Input(InputEvent @event)
    {
        // Consume all input while active
        GetViewport().SetInputAsHandled();

        if (!IsAdvancePress(@event))
            return;

        _Dismiss();
    }

    private static bool IsAdvancePress(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            return true;

        if (@event is InputEventScreenTouch { Pressed: true })
            return true;

        if (@event.IsPressed() && !@event.IsEcho())
        {
            if (@event.IsAction("interact") ||
                @event.IsAction("ui_accept") ||
                @event.IsAction("gamepad_interact"))
                return true;
        }

        return false;
    }

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
