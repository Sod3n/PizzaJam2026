using Godot;
using Deterministic.GameFramework.ECS;
using Template.Godot.Twitch;

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

    private static readonly PackedScene _scene =
        GD.Load<PackedScene>("res://Scenes/LovePopupOverlay.tscn");

    // Cached node references
    private Control _root;
    private Label _nameLabel;
    private Label _messageLabel;
    private Label _hintLabel;

    /// <summary>
    /// Show a love popup for the given cow entity.
    /// </summary>
    public static void Show(SceneTree tree, Entity loverEntity, string targetCowName)
    {
        // Dismiss any existing overlay
        if (_current != null && Node.IsInstanceValid(_current))
            _current.QueueFree();

        // Get the lover cow's name (prefer Twitch override if available)
        string loverName = TwitchIntegration.GetDisplayName(loverEntity);

        var overlay = _scene.Instantiate<LovePopupOverlay>();
        _current = overlay;
        tree.Root.AddChild(overlay);
        overlay._Setup(loverName, targetCowName);
    }

    private void _Setup(string loverName, string targetName)
    {
        // Cache node references from scene
        _root = GetNode<Control>("Root");
        _nameLabel = GetNode<Label>("Root/Panel/VBoxContainer/NameLabel");
        _messageLabel = GetNode<Label>("Root/Panel/VBoxContainer/MessageLabel");
        _hintLabel = GetNode<Label>("Root/Panel/VBoxContainer/HintLabel");

        // Set dynamic text content
        _nameLabel.Text = loverName;
        _messageLabel.Text = $"Hey I really like {targetName}!";
        _hintLabel.Text = "Breed them together for a guaranteed upgrade!";

        // Fade in
        _root.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(_root, "modulate:a", 1f, 0.2f)
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

        var tween = CreateTween();
        tween.TweenProperty(_root, "modulate:a", 0f, 0.15f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            _current = null;
            QueueFree();
        }));
    }
}
