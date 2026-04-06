using Godot;
using System.Collections.Generic;
using Template.Shared;

namespace Template.Godot.Visuals;

/// <summary>
/// Simple overlay that displays a building name and description.
/// Dismisses on any input (tap/click/key). While active, blocks game inputs.
/// </summary>
public partial class BuildingInfoOverlay : CanvasLayer
{
    private static BuildingInfoOverlay _current;

    /// <summary>True while the info overlay is on screen. Used to block game input.</summary>
    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    // Building info lookup: param key -> (Name, Description)
    private static readonly Dictionary<string, (string Name, string Desc)> BuildingInfoMap = new()
    {
        { StateKeys.InfoSellPoint,        ("Sell Point",        "Sell your milk products for coins here") },
        { StateKeys.InfoHouse,            ("Cow House",         "Assign a cow here to start milking") },
        { StateKeys.InfoLoveHouse,        ("Love Hotel",        "Bring two cows here to breed") },
        { StateKeys.InfoCarrotFarm,       ("Carrot Farm",       "With it carrots will grow in the world") },
        { StateKeys.InfoAppleOrchard,     ("Apple Orchard",     "With it apples will grow in the world") },
        { StateKeys.InfoMushroomCave,     ("Mushroom Cave",     "With it mushrooms will grow in the world") },
        { StateKeys.InfoHelperAssistant,  ("Assistant House",   "Home of your assistant helper") },
        { StateKeys.InfoUpgradeGatherer,  ("Gatherer Upgrade",  "Upgraded your gatherer helper") },
        { StateKeys.InfoUpgradeBuilder,   ("Builder Upgrade",   "Upgraded your builder helper") },
        { StateKeys.InfoUpgradeSeller,    ("Seller Upgrade",    "Upgraded your seller helper") },
        { StateKeys.InfoUpgradeAssistant, ("Assistant Upgrade", "Upgraded your assistant helper") },
        { StateKeys.InfoDecoration,       ("Decoration",        "A nice decoration for your farm") },
        { StateKeys.InfoWarehouse,        ("Warehouse",         "Stores your items and resources") },
        { StateKeys.InfoDepressed,        ("Sad Cow",            "Sad about a bad breeding experience. Needs some time to rest.") },
    };

    private static readonly PackedScene _scene =
        GD.Load<PackedScene>("res://Scenes/BuildingInfoOverlay.tscn");

    // Cached nodes
    private Control _root;
    private Label _nameLabel;
    private Label _descLabel;

    /// <summary>
    /// Show a building info popup. If one is already active, it gets replaced.
    /// </summary>
    public static void Show(SceneTree tree, string buildingInfoKey)
    {
        if (!BuildingInfoMap.TryGetValue(buildingInfoKey, out var info))
            return;

        // Dismiss any existing overlay
        if (_current != null && Node.IsInstanceValid(_current))
            _current.QueueFree();

        if (_scene == null) return;

        var overlay = _scene.Instantiate<BuildingInfoOverlay>();
        _current = overlay;
        tree.Root.AddChild(overlay);
        overlay._Setup(info.Name, info.Desc);
    }

    private void _Setup(string buildingName, string description)
    {
        // Get node references from the scene
        _root = GetNode<Control>("Control");
        _nameLabel = GetNode<Label>("Control/Panel/VBoxContainer/NameLabel");
        _descLabel = GetNode<Label>("Control/Panel/VBoxContainer/DescLabel");

        // Set text
        _nameLabel.Text = buildingName;
        _descLabel.Text = description;

        // Animate in: fade from transparent
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
