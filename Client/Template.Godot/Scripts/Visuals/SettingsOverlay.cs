using Godot;

namespace Template.Godot.Visuals;

/// <summary>
/// Full-screen settings overlay accessible via ESC key.
/// Contains Twitch integration settings and a placeholder Game section.
/// Blocks game input while visible. Follows the same CanvasLayer pattern
/// as CraftingOverlay / BuildingInfoOverlay.
/// </summary>
public partial class SettingsOverlay : CanvasLayer
{
    private static SettingsOverlay _current;

    /// <summary>True while the settings overlay is on screen. Used to block game input.</summary>
    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    // UI references for dynamic updates
    private Button _connectButton;
    private Label _statusLabel;
    private CheckButton _nameCowsToggle;
    private CheckButton _enableRewardsToggle;
    private HSlider _loveConfessionSlider;
    private Label _loveConfessionValueLabel;
    private HSlider _saySomethingSlider;
    private Label _saySomethingValueLabel;

    // Colors (structural UI colors from shared theme)
    private static readonly Color TitleColor = UITheme.Title;
    private static readonly Color SectionColor = UITheme.Title;
    private static readonly Color LabelColor = UITheme.Subtitle;
    private static readonly Color HintColor = UITheme.Subtitle;
    private static readonly Color ConnectedColor = new(0.4f, 0.9f, 0.4f);
    private static readonly Color DisconnectedColor = new(0.7f, 0.7f, 0.7f);

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

        TwitchSettings.Load();

        var overlay = new SettingsOverlay();
        _current = overlay;
        overlay.Layer = 100;
        tree.Root.AddChild(overlay);
        overlay._Build();
    }

    // ── Build ──────────────────────────────────────────────────────────

    private void _Build()
    {
        // Full-screen root that blocks input
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(root);

        // Dim background
        var bg = new ColorRect();
        bg.Color = UITheme.OverlayDim;
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(bg);

        // Scroll container for the panel so content can overflow on small screens
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        root.AddChild(scroll);

        // A centering wrapper inside the scroll
        var centerWrapper = new CenterContainer();
        centerWrapper.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        centerWrapper.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        centerWrapper.CustomMinimumSize = new Vector2(0, 0);
        scroll.AddChild(centerWrapper);

        // Main panel
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(520, 0);

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = UITheme.PanelBg;
        panelStyle.CornerRadiusTopLeft = UITheme.CornerRadius;
        panelStyle.CornerRadiusTopRight = UITheme.CornerRadius;
        panelStyle.CornerRadiusBottomLeft = UITheme.CornerRadius;
        panelStyle.CornerRadiusBottomRight = UITheme.CornerRadius;
        panelStyle.ContentMarginLeft = 28;
        panelStyle.ContentMarginRight = 28;
        panelStyle.ContentMarginTop = 20;
        panelStyle.ContentMarginBottom = 20;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        centerWrapper.AddChild(panel);

        var outerVBox = new VBoxContainer();
        outerVBox.AddThemeConstantOverride("separation", 14);
        panel.AddChild(outerVBox);

        // Title
        _AddLabel(outerVBox, "Settings", 28, TitleColor, HorizontalAlignment.Center);

        // ── Twitch Integration Section ─────────────────────────────────
        _AddSeparator(outerVBox);
        _AddLabel(outerVBox, "Twitch Integration", 22, SectionColor, HorizontalAlignment.Left);

        // Connection row: status + button
        var connectRow = new HBoxContainer();
        connectRow.AddThemeConstantOverride("separation", 12);
        outerVBox.AddChild(connectRow);

        _statusLabel = new Label();
        _statusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _statusLabel.AddThemeFontSizeOverride("font_size", 16);
        connectRow.AddChild(_statusLabel);

        _connectButton = new Button();
        _connectButton.CustomMinimumSize = new Vector2(160, 36);
        _StyleButton(_connectButton);
        _connectButton.Pressed += _OnConnectPressed;
        connectRow.AddChild(_connectButton);

        _UpdateConnectionUI();

        // Toggle: Name cows from chat
        _nameCowsToggle = new CheckButton();
        _nameCowsToggle.Text = "Name cows from chat";
        _nameCowsToggle.ButtonPressed = TwitchSettings.NameCowsFromChat;
        _nameCowsToggle.AddThemeFontSizeOverride("font_size", 16);
        _nameCowsToggle.AddThemeColorOverride("font_color", LabelColor);
        _nameCowsToggle.AddThemeColorOverride("font_outline_color", UITheme.TextOutline);
        _nameCowsToggle.AddThemeConstantOverride("outline_size", UITheme.TextOutlineSize);
        _nameCowsToggle.Toggled += on =>
        {
            TwitchSettings.NameCowsFromChat = on;
            TwitchSettings.Save();
        };
        outerVBox.AddChild(_nameCowsToggle);

        // Toggle: Enable channel point rewards
        _enableRewardsToggle = new CheckButton();
        _enableRewardsToggle.Text = "Enable channel point rewards";
        _enableRewardsToggle.ButtonPressed = TwitchSettings.EnableRewards;
        _enableRewardsToggle.AddThemeFontSizeOverride("font_size", 16);
        _enableRewardsToggle.AddThemeColorOverride("font_color", LabelColor);
        _enableRewardsToggle.AddThemeColorOverride("font_outline_color", UITheme.TextOutline);
        _enableRewardsToggle.AddThemeConstantOverride("outline_size", UITheme.TextOutlineSize);
        _enableRewardsToggle.Toggled += on =>
        {
            TwitchSettings.EnableRewards = on;
            TwitchSettings.Save();
        };
        outerVBox.AddChild(_enableRewardsToggle);

        // Slider: Love Confession cost
        _loveConfessionValueLabel = _AddSliderRow(
            outerVBox,
            "Love Confession cost",
            100, 10000,
            TwitchSettings.LoveConfessionCost,
            500,
            out _loveConfessionSlider
        );
        _loveConfessionSlider.ValueChanged += val =>
        {
            int rounded = RoundToStep((int)val, 50);
            _loveConfessionSlider.SetValueNoSignal(rounded);
            TwitchSettings.LoveConfessionCost = rounded;
            _loveConfessionValueLabel.Text = rounded.ToString();
            TwitchSettings.Save();
        };

        // Slider: Say Something cost
        _saySomethingValueLabel = _AddSliderRow(
            outerVBox,
            "Say Something cost",
            50, 5000,
            TwitchSettings.SaySomethingCost,
            50,
            out _saySomethingSlider
        );
        _saySomethingSlider.ValueChanged += val =>
        {
            int rounded = RoundToStep((int)val, 25);
            _saySomethingSlider.SetValueNoSignal(rounded);
            TwitchSettings.SaySomethingCost = rounded;
            _saySomethingValueLabel.Text = rounded.ToString();
            TwitchSettings.Save();
        };

        // ── Game Section (placeholder) ─────────────────────────────────
        _AddSeparator(outerVBox);
        _AddLabel(outerVBox, "Game", 22, SectionColor, HorizontalAlignment.Left);
        _AddLabel(outerVBox, "More settings coming soon...", 14, HintColor, HorizontalAlignment.Left);

        // ── Dismiss hint ───────────────────────────────────────────────
        _AddSeparator(outerVBox);
        _AddLabel(outerVBox, "Press Escape to close", 14, HintColor, HorizontalAlignment.Center);

        // Fade in
        root.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(root, "modulate:a", 1f, 0.2f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    // ── UI Helpers ─────────────────────────────────────────────────────

    private static Label _AddLabel(Control parent, string text, int fontSize, Color color, HorizontalAlignment align)
    {
        var label = new Label();
        label.Text = text;
        label.HorizontalAlignment = align;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", UITheme.TextOutline);
        label.AddThemeConstantOverride("outline_size", UITheme.TextOutlineSize);
        parent.AddChild(label);
        return label;
    }

    private static void _AddSeparator(Control parent)
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 6);
        parent.AddChild(sep);
    }

    private static Label _AddSliderRow(Control parent, string labelText, int min, int max, int current, int step, out HSlider slider)
    {
        var rowVBox = new VBoxContainer();
        rowVBox.AddThemeConstantOverride("separation", 4);
        parent.AddChild(rowVBox);

        var headerRow = new HBoxContainer();
        rowVBox.AddChild(headerRow);

        var label = new Label();
        label.Text = labelText;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", LabelColor);
        label.AddThemeColorOverride("font_outline_color", UITheme.TextOutline);
        label.AddThemeConstantOverride("outline_size", UITheme.TextOutlineSize);
        headerRow.AddChild(label);

        var valueLabel = new Label();
        valueLabel.Text = current.ToString();
        valueLabel.AddThemeFontSizeOverride("font_size", 16);
        valueLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        valueLabel.CustomMinimumSize = new Vector2(60, 0);
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        headerRow.AddChild(valueLabel);

        slider = new HSlider();
        slider.MinValue = min;
        slider.MaxValue = max;
        slider.Step = step;
        slider.Value = current;
        slider.CustomMinimumSize = new Vector2(0, 24);
        rowVBox.AddChild(slider);

        // Range labels
        var rangeRow = new HBoxContainer();
        rowVBox.AddChild(rangeRow);

        var minLabel = new Label();
        minLabel.Text = min.ToString();
        minLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        minLabel.AddThemeFontSizeOverride("font_size", 12);
        minLabel.AddThemeColorOverride("font_color", HintColor);
        rangeRow.AddChild(minLabel);

        var maxLabel = new Label();
        maxLabel.Text = max.ToString();
        maxLabel.HorizontalAlignment = HorizontalAlignment.Right;
        maxLabel.AddThemeFontSizeOverride("font_size", 12);
        maxLabel.AddThemeColorOverride("font_color", HintColor);
        rangeRow.AddChild(maxLabel);

        return valueLabel;
    }

    private static void _StyleButton(Button button)
    {
        var normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = UITheme.CardBg;
        normalStyle.SetCornerRadiusAll(UITheme.CornerRadius);
        normalStyle.ContentMarginLeft = 12;
        normalStyle.ContentMarginRight = 12;
        normalStyle.ContentMarginTop = 6;
        normalStyle.ContentMarginBottom = 6;
        button.AddThemeStyleboxOverride("normal", normalStyle);

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = UITheme.CardBg.Lightened(0.15f);
        hoverStyle.SetCornerRadiusAll(UITheme.CornerRadius);
        hoverStyle.ContentMarginLeft = 12;
        hoverStyle.ContentMarginRight = 12;
        hoverStyle.ContentMarginTop = 6;
        hoverStyle.ContentMarginBottom = 6;
        button.AddThemeStyleboxOverride("hover", hoverStyle);

        var pressedStyle = new StyleBoxFlat();
        pressedStyle.BgColor = UITheme.CardBg.Darkened(0.15f);
        pressedStyle.SetCornerRadiusAll(UITheme.CornerRadius);
        pressedStyle.ContentMarginLeft = 12;
        pressedStyle.ContentMarginRight = 12;
        pressedStyle.ContentMarginTop = 6;
        pressedStyle.ContentMarginBottom = 6;
        button.AddThemeStyleboxOverride("pressed", pressedStyle);

        button.AddThemeFontSizeOverride("font_size", 16);
    }

    private static int RoundToStep(int value, int step)
    {
        return ((value + step / 2) / step) * step;
    }

    // ── Connection Logic ───────────────────────────────────────────────

    private void _UpdateConnectionUI()
    {
        if (TwitchSettings.IsConnected)
        {
            _statusLabel.Text = $"Connected as {TwitchSettings.ChannelName}";
            _statusLabel.AddThemeColorOverride("font_color", ConnectedColor);
            _connectButton.Text = "Disconnect";
        }
        else
        {
            _statusLabel.Text = "Not connected";
            _statusLabel.AddThemeColorOverride("font_color", DisconnectedColor);
            _connectButton.Text = "Connect Twitch";
        }
    }

    private void _OnConnectPressed()
    {
        if (TwitchSettings.IsConnected)
        {
            TwitchService.Disconnect();
        }
        else
        {
            TwitchService.Connect();
        }
        _UpdateConnectionUI();
    }

    // ── Input ──────────────────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        // Consume all input while active to block game
        GetViewport().SetInputAsHandled();

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.Escape)
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
