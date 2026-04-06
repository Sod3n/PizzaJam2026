using Godot;

namespace Template.Godot.Visuals;

/// <summary>
/// Full-screen settings overlay accessible via ESC key.
/// Contains Twitch integration settings and a placeholder Game section.
/// Blocks game input while visible. Loads its layout from SettingsOverlay.tscn.
/// </summary>
public partial class SettingsOverlay : CanvasLayer
{
    private static SettingsOverlay _current;

    private static readonly PackedScene _scene =
        GD.Load<PackedScene>("res://Scenes/SettingsOverlay.tscn");

    /// <summary>True while the settings overlay is on screen. Used to block game input.</summary>
    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    // UI references populated from the scene tree
    private Control _root;
    private Button _connectButton;
    private Label _statusLabel;
    private CheckButton _nameCowsToggle;
    private CheckButton _enableRewardsToggle;
    private HSlider _loveConfessionSlider;
    private Label _loveConfessionValueLabel;
    private HSlider _saySomethingSlider;
    private Label _saySomethingValueLabel;

    // Colors for connection status
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

        var overlay = _scene.Instantiate<SettingsOverlay>();
        _current = overlay;
        tree.Root.AddChild(overlay);
    }

    // ── Ready ───────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _root = GetNode<Control>("Root");

        // Connection row
        _statusLabel = GetNode<Label>("Root/ScrollContainer/CenterWrapper/Panel/Content/ConnectRow/StatusLabel");
        _connectButton = GetNode<Button>("Root/ScrollContainer/CenterWrapper/Panel/Content/ConnectRow/ConnectButton");
        _connectButton.Pressed += _OnConnectPressed;

        // Toggles
        _nameCowsToggle = GetNode<CheckButton>("Root/ScrollContainer/CenterWrapper/Panel/Content/NameCowsToggle");
        _nameCowsToggle.ButtonPressed = TwitchSettings.NameCowsFromChat;
        _nameCowsToggle.Toggled += on =>
        {
            TwitchSettings.NameCowsFromChat = on;
            TwitchSettings.Save();
        };

        _enableRewardsToggle = GetNode<CheckButton>("Root/ScrollContainer/CenterWrapper/Panel/Content/EnableRewardsToggle");
        _enableRewardsToggle.ButtonPressed = TwitchSettings.EnableRewards;
        _enableRewardsToggle.Toggled += on =>
        {
            TwitchSettings.EnableRewards = on;
            TwitchSettings.Save();
        };

        // Love Confession slider
        _loveConfessionValueLabel = GetNode<Label>("Root/ScrollContainer/CenterWrapper/Panel/Content/LoveConfessionRow/HeaderRow/ValueLabel");
        _loveConfessionSlider = GetNode<HSlider>("Root/ScrollContainer/CenterWrapper/Panel/Content/LoveConfessionRow/Slider");
        _loveConfessionSlider.Value = TwitchSettings.LoveConfessionCost;
        _loveConfessionValueLabel.Text = TwitchSettings.LoveConfessionCost.ToString();
        _loveConfessionSlider.ValueChanged += val =>
        {
            int rounded = RoundToStep((int)val, 50);
            _loveConfessionSlider.SetValueNoSignal(rounded);
            TwitchSettings.LoveConfessionCost = rounded;
            _loveConfessionValueLabel.Text = rounded.ToString();
            TwitchSettings.Save();
        };

        // Say Something slider
        _saySomethingValueLabel = GetNode<Label>("Root/ScrollContainer/CenterWrapper/Panel/Content/SaySomethingRow/HeaderRow/ValueLabel");
        _saySomethingSlider = GetNode<HSlider>("Root/ScrollContainer/CenterWrapper/Panel/Content/SaySomethingRow/Slider");
        _saySomethingSlider.Value = TwitchSettings.SaySomethingCost;
        _saySomethingValueLabel.Text = TwitchSettings.SaySomethingCost.ToString();
        _saySomethingSlider.ValueChanged += val =>
        {
            int rounded = RoundToStep((int)val, 25);
            _saySomethingSlider.SetValueNoSignal(rounded);
            TwitchSettings.SaySomethingCost = rounded;
            _saySomethingValueLabel.Text = rounded.ToString();
            TwitchSettings.Save();
        };

        // Set initial connection UI state
        _UpdateConnectionUI();

        // Fade in
        _root.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(_root, "modulate:a", 1f, 0.2f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

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

        var tween = CreateTween();
        tween.TweenProperty(_root, "modulate:a", 0f, 0.15f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            _current = null;
            QueueFree();
        }));
    }
}
