using Godot;

namespace Template.Godot.Twitch;

/// <summary>
/// Popup overlay shown when a Twitch viewer redeems the Love Confession channel point reward.
/// Displays "{username}'s cow has fallen in love!" and dismisses on tap/click.
/// </summary>
public partial class LoveConfessionPopup : CanvasLayer
{
    private static LoveConfessionPopup _current;

    /// <summary>True while the popup is on screen.</summary>
    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    private static readonly Texture2D _heartTexture =
        GD.Load<Texture2D>("res://sprites/heart.png");

    public static void Show(SceneTree tree, string username, string cowName)
    {
        // Dismiss any existing overlay
        if (_current != null && Node.IsInstanceValid(_current))
            _current.QueueFree();

        var overlay = new LoveConfessionPopup();
        _current = overlay;
        overlay.Layer = 100;
        tree.Root.AddChild(overlay);
        overlay._Setup(username, cowName);
    }

    private void _Setup(string username, string cowName)
    {
        var panel = new PanelContainer();
        panel.Name = "Panel";
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(420, 0);

        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.15f, 0.05f, 0.15f, 0.92f);
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

        // Heart icon
        if (_heartTexture != null)
        {
            var heartRect = new TextureRect();
            heartRect.Texture = _heartTexture;
            heartRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            heartRect.CustomMinimumSize = new Vector2(64, 64);
            heartRect.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            vbox.AddChild(heartRect);
        }

        // Twitch badge
        var twitchLabel = new Label();
        twitchLabel.Text = "Twitch Love Confession";
        twitchLabel.HorizontalAlignment = HorizontalAlignment.Center;
        twitchLabel.AddThemeFontSizeOverride("font_size", 16);
        twitchLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.4f, 0.8f));
        vbox.AddChild(twitchLabel);

        // Username header
        var nameLabel = new Label();
        nameLabel.Text = username;
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nameLabel.AddThemeFontSizeOverride("font_size", 28);
        nameLabel.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.8f));
        vbox.AddChild(nameLabel);

        // Love message
        var msgLabel = new Label();
        msgLabel.Text = $"{cowName} has fallen in love!";
        msgLabel.HorizontalAlignment = HorizontalAlignment.Center;
        msgLabel.AddThemeFontSizeOverride("font_size", 22);
        msgLabel.AddThemeColorOverride("font_color", Colors.White);
        msgLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(msgLabel);

        // Dismiss hint
        var dismissHint = new Label();
        dismissHint.Text = "Tap to dismiss";
        dismissHint.HorizontalAlignment = HorizontalAlignment.Center;
        dismissHint.AddThemeFontSizeOverride("font_size", 14);
        dismissHint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.55f));
        vbox.AddChild(dismissHint);

        // Full-screen background
        var background = new ColorRect();
        background.Color = new Color(0.1f, 0, 0.1f, 0.4f);
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
        GetViewport().SetInputAsHandled();

        if (!IsAdvancePress(@event)) return;
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
