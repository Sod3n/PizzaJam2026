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

    // Cached nodes
    private ColorRect _background;
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

        var overlay = new BuildingInfoOverlay();
        _current = overlay;
        overlay.Layer = 100; // Above game, same priority as BreedResultOverlay
        tree.Root.AddChild(overlay);
        overlay._Setup(info.Name, info.Desc);
    }

    private void _Setup(string buildingName, string description)
    {
        // Build UI in code (no scene file needed for this simple overlay)
        var panel = new PanelContainer();
        panel.Name = "Panel";
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.CustomMinimumSize = new Vector2(400, 0);

        // Theme override for rounded dark background
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.92f);
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

        _nameLabel = new Label();
        _nameLabel.Text = buildingName;
        _nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _nameLabel.AddThemeFontSizeOverride("font_size", 32);
        _nameLabel.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(_nameLabel);

        _descLabel = new Label();
        _descLabel.Text = description;
        _descLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _descLabel.AddThemeFontSizeOverride("font_size", 20);
        _descLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        _descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_descLabel);

        var hint = new Label();
        hint.Text = "Tap to dismiss";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 14);
        hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.55f));
        vbox.AddChild(hint);

        // Full-screen background to catch clicks
        _background = new ColorRect();
        _background.Color = new Color(0, 0, 0, 0.4f);
        _background.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // Add background first, then panel on top
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(root);
        root.AddChild(_background);
        root.AddChild(panel);

        // Animate in: fade from transparent
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
