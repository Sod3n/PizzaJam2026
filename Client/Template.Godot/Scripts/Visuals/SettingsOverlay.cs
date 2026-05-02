using System;
using Godot;
using Template.Godot.Core;

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

    // Lobby UI
    private Label _lobbyCodeLabel;
    private Button _lobbyCopyButton;
    private Label _lobbyStatusLabel;
    private LineEdit _lobbyCodeInput;
    private Button _lobbyJoinButton;
    private Button _lobbyCreateButton;
    private Guid _lastSeenLobbyId;
    private string _lastSeenStatus;
    private Action<string> _statusHandler;
    private Action<Guid> _lobbyCreatedHandler;

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

        // Lobby section
        _lobbyCodeLabel = GetNode<Label>("Root/ScrollContainer/CenterWrapper/Panel/Content/LobbyCodeRow/CodeLabel");
        _lobbyCopyButton = GetNode<Button>("Root/ScrollContainer/CenterWrapper/Panel/Content/LobbyCodeRow/CopyButton");
        _lobbyStatusLabel = GetNode<Label>("Root/ScrollContainer/CenterWrapper/Panel/Content/LobbyStatusLabel");
        _lobbyCodeInput = GetNode<LineEdit>("Root/ScrollContainer/CenterWrapper/Panel/Content/LobbyJoinRow/CodeInput");
        _lobbyJoinButton = GetNode<Button>("Root/ScrollContainer/CenterWrapper/Panel/Content/LobbyJoinRow/JoinButton");
        _lobbyCreateButton = GetNode<Button>("Root/ScrollContainer/CenterWrapper/Panel/Content/LobbyActionsRow/CreateButton");

        _lobbyCopyButton.Pressed += _OnCopyLobbyPressed;
        _lobbyJoinButton.Pressed += _OnJoinLobbyPressed;
        _lobbyCreateButton.Pressed += _OnCreateLobbyPressed;

        var gm = GameManager.Instance;
        if (gm != null)
        {
            _statusHandler = (status) => CallDeferred(nameof(_OnLobbyStatusDeferred), status);
            _lobbyCreatedHandler = (_) => CallDeferred(nameof(_RefreshLobbyUI));
            gm.OnStatusChanged += _statusHandler;
            gm.OnLobbyCreated += _lobbyCreatedHandler;
        }
        _RefreshLobbyUI();

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

    // ── Lobby Logic ─────────────────────────────────────────────────────

    private void _RefreshLobbyUI()
    {
        var gm = GameManager.Instance;
        if (gm == null)
        {
            _lobbyCodeLabel.Text = "Offline";
            _lobbyCodeLabel.AddThemeColorOverride("font_color", DisconnectedColor);
            _lobbyCopyButton.Disabled = true;
            _lobbyCreateButton.Disabled = true;
            _lobbyJoinButton.Disabled = true;
            return;
        }

        _lastSeenLobbyId = gm.CurrentLobbyId;
        bool hasLobby = gm.CurrentLobbyId != Guid.Empty;
        bool offline = gm.OfflineMode;

        if (hasLobby)
        {
            _lobbyCodeLabel.Text = gm.CurrentLobbyId.ToString();
            _lobbyCodeLabel.AddThemeColorOverride("font_color", ConnectedColor);
            _lobbyCopyButton.Disabled = false;
        }
        else
        {
            _lobbyCodeLabel.Text = offline ? "Offline" : "Not connected";
            _lobbyCodeLabel.AddThemeColorOverride("font_color", DisconnectedColor);
            _lobbyCopyButton.Disabled = true;
        }

        _lobbyCreateButton.Disabled = false;
        _lobbyJoinButton.Disabled = false;
    }

    private void _OnLobbyStatusDeferred(string status)
    {
        _lastSeenStatus = status;
        if (_lobbyStatusLabel != null && IsInsideTree())
            _lobbyStatusLabel.Text = status;
        _RefreshLobbyUI();
    }

    private void _OnCopyLobbyPressed()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.CurrentLobbyId == Guid.Empty) return;
        DisplayServer.ClipboardSet(gm.CurrentLobbyId.ToString());
        _lobbyCopyButton.Text = "Copied!";
        GetTree().CreateTimer(1.5).Timeout += () =>
        {
            if (Node.IsInstanceValid(_lobbyCopyButton))
                _lobbyCopyButton.Text = "Copy";
        };
    }

    private void _OnJoinLobbyPressed()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        var text = _lobbyCodeInput.Text?.Trim() ?? "";
        if (!Guid.TryParse(text, out var lobbyId))
        {
            _lobbyStatusLabel.Text = "Invalid lobby code.";
            return;
        }
        _lobbyStatusLabel.Text = "Joining...";
        _lobbyJoinButton.Disabled = true;
        _lobbyCreateButton.Disabled = true;
        _ = gm.JoinLobby(lobbyId);
    }

    private void _OnCreateLobbyPressed()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        _lobbyStatusLabel.Text = "Creating lobby...";
        _lobbyCreateButton.Disabled = true;
        _lobbyJoinButton.Disabled = true;
        _ = gm.CreateLobby("Game Lobby");
    }

    // ── Input ──────────────────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.Escape)
            {
                _Dismiss();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // LineEdit consumes its own GUI input first; only swallow leftovers so
        // gameplay (WASD etc.) doesn't fire while the overlay is open.
        if (_lobbyCodeInput != null && _lobbyCodeInput.HasFocus()) return;
        GetViewport().SetInputAsHandled();
    }

    // ── Dismiss ────────────────────────────────────────────────────────

    private void _Dismiss()
    {
        if (!IsInsideTree()) return;

        var gm = GameManager.Instance;
        if (gm != null)
        {
            if (_statusHandler != null) gm.OnStatusChanged -= _statusHandler;
            if (_lobbyCreatedHandler != null) gm.OnLobbyCreated -= _lobbyCreatedHandler;
        }

        var tween = CreateTween();
        tween.TweenProperty(_root, "modulate:a", 0f, 0.15f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            _current = null;
            QueueFree();
        }));
    }
}
