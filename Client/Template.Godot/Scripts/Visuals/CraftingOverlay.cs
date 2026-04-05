using Godot;

namespace Template.Godot.Visuals;

/// <summary>
/// Full-screen overlay showing all milking-chain crafting recipes.
/// Toggled via the C key. Blocks game input while visible.
/// Dismisses on Escape or pressing C again.
/// </summary>
public partial class CraftingOverlay : CanvasLayer
{
    private static CraftingOverlay _current;

    /// <summary>True while the crafting overlay is on screen. Used to block game input.</summary>
    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    // Icon paths (matching the existing convention from BreedResultOverlay._foodIcons)
    private static readonly string IconGrass     = "res://sprites/export/icons/Grass_/1.png";
    private static readonly string IconCarrot    = "res://sprites/export/icons/Carrot_/1.png";
    private static readonly string IconApple     = "res://sprites/export/icons/Apply_/1.png";
    private static readonly string IconMushroom  = "res://sprites/export/icons/Mashroom/1.png";
    private static readonly string IconMilk      = "res://sprites/export/icons/Milky_/1.png";
    private static readonly string IconCoins     = "res://sprites/export/icons/Money_/1.png";

    // Product colors (tint the milk icon to distinguish products)
    private static readonly Color ColorMilk             = Colors.White;
    private static readonly Color ColorCarrotMilkshake   = new(1f, 0.7f, 0.3f);    // warm orange
    private static readonly Color ColorVitaminMix        = new(0.4f, 0.9f, 0.4f);   // bright green
    private static readonly Color ColorPurplePotion      = new(0.7f, 0.35f, 0.95f); // vivid purple

    // Recipe data
    private struct Recipe
    {
        public string Name;
        public string[] IngredientIcons;
        public Color[] IngredientTints;
        public string[] IngredientLabels;
        public string ResultIcon;
        public Color ResultTint;
        public string ResultLabel;
        public int CoinValue;
    }

    private static readonly Recipe[] Recipes =
    {
        new()
        {
            Name = "Milk",
            IngredientIcons  = new[] { IconGrass },
            IngredientTints  = new[] { Colors.White },
            IngredientLabels = new[] { "Grass" },
            ResultIcon  = IconMilk,
            ResultTint  = ColorMilk,
            ResultLabel = "Milk",
            CoinValue   = 1,
        },
        new()
        {
            Name = "Carrot Milkshake",
            IngredientIcons  = new[] { IconMilk, IconCarrot },
            IngredientTints  = new[] { ColorMilk, Colors.White },
            IngredientLabels = new[] { "Milk", "Carrot" },
            ResultIcon  = IconMilk,
            ResultTint  = ColorCarrotMilkshake,
            ResultLabel = "Carrot Milkshake",
            CoinValue   = 6,
        },
        new()
        {
            Name = "Vitamin Mix",
            IngredientIcons  = new[] { IconMilk, IconApple },
            IngredientTints  = new[] { ColorCarrotMilkshake, Colors.White },
            IngredientLabels = new[] { "Carrot Milkshake", "Apple" },
            ResultIcon  = IconMilk,
            ResultTint  = ColorVitaminMix,
            ResultLabel = "Vitamin Mix",
            CoinValue   = 20,
        },
        new()
        {
            Name = "Purple Potion",
            IngredientIcons  = new[] { IconMilk, IconMushroom },
            IngredientTints  = new[] { ColorVitaminMix, Colors.White },
            IngredientLabels = new[] { "Vitamin Mix", "Mushroom" },
            ResultIcon  = IconMilk,
            ResultTint  = ColorPurplePotion,
            ResultLabel = "Purple Potion",
            CoinValue   = 200,
        },
    };

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

        var overlay = new CraftingOverlay();
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

        // Centered panel
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
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
        root.AddChild(panel);

        var outerVBox = new VBoxContainer();
        outerVBox.AddThemeConstantOverride("separation", 16);
        panel.AddChild(outerVBox);

        // Title
        var title = new Label();
        title.Text = "Crafting Recipes";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 28);
        UITheme.StyleLabel(title);
        outerVBox.AddChild(title);

        // Subtitle
        var subtitle = new Label();
        subtitle.Text = "Milking Chain";
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.AddThemeFontSizeOverride("font_size", 16);
        UITheme.StyleLabel(subtitle, false);
        outerVBox.AddChild(subtitle);

        // Separator
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 8);
        outerVBox.AddChild(sep);

        // Recipe rows
        foreach (var recipe in Recipes)
        {
            outerVBox.AddChild(_CreateRecipeRow(recipe));
        }

        // Separator before hint
        var sep2 = new HSeparator();
        sep2.AddThemeConstantOverride("separation", 4);
        outerVBox.AddChild(sep2);

        // Dismiss hint
        var hint = new Label();
        hint.Text = "Press C or Escape to close";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 14);
        UITheme.StyleLabel(hint, false);
        outerVBox.AddChild(hint);

        // Fade in
        root.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(root, "modulate:a", 1f, 0.2f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    private Control _CreateRecipeRow(Recipe recipe)
    {
        // Row container with dark background
        var rowPanel = new PanelContainer();
        var rowStyle = new StyleBoxFlat();
        rowStyle.BgColor = UITheme.CardBg;
        rowStyle.CornerRadiusTopLeft = UITheme.CornerRadius;
        rowStyle.CornerRadiusTopRight = UITheme.CornerRadius;
        rowStyle.CornerRadiusBottomLeft = UITheme.CornerRadius;
        rowStyle.CornerRadiusBottomRight = UITheme.CornerRadius;
        rowStyle.ContentMarginLeft = 12;
        rowStyle.ContentMarginRight = 12;
        rowStyle.ContentMarginTop = 8;
        rowStyle.ContentMarginBottom = 8;
        rowPanel.AddThemeStyleboxOverride("panel", rowStyle);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        rowPanel.AddChild(hbox);

        // Ingredients
        for (int i = 0; i < recipe.IngredientIcons.Length; i++)
        {
            if (i > 0)
            {
                var plus = new Label();
                plus.Text = "+";
                plus.AddThemeFontSizeOverride("font_size", 22);
                plus.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                plus.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                hbox.AddChild(plus);
            }

            hbox.AddChild(_CreateIconWithLabel(
                recipe.IngredientIcons[i],
                recipe.IngredientTints[i],
                recipe.IngredientLabels[i]
            ));
        }

        // Arrow
        var arrow = new Label();
        arrow.Text = "  >>  ";
        arrow.AddThemeFontSizeOverride("font_size", 20);
        arrow.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.4f));
        arrow.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        hbox.AddChild(arrow);

        // Result
        hbox.AddChild(_CreateIconWithLabel(
            recipe.ResultIcon,
            recipe.ResultTint,
            recipe.ResultLabel
        ));

        // Coin value
        var coinBox = new VBoxContainer();
        coinBox.Alignment = BoxContainer.AlignmentMode.Center;
        coinBox.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        var coinIcon = _CreateIcon(IconCoins, Colors.White, 24);
        coinIcon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        coinBox.AddChild(coinIcon);

        var coinLabel = new Label();
        coinLabel.Text = $"{recipe.CoinValue}";
        coinLabel.HorizontalAlignment = HorizontalAlignment.Center;
        coinLabel.AddThemeFontSizeOverride("font_size", 12);
        coinLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        coinBox.AddChild(coinLabel);

        hbox.AddChild(coinBox);

        return rowPanel;
    }

    private Control _CreateIconWithLabel(string iconPath, Color tint, string labelText)
    {
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);
        vbox.Alignment = BoxContainer.AlignmentMode.Center;

        var icon = _CreateIcon(iconPath, tint, 40);
        icon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        vbox.AddChild(icon);

        var label = new Label();
        label.Text = labelText;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", 11);
        UITheme.StyleLabel(label, false);
        vbox.AddChild(label);

        return vbox;
    }

    private static TextureRect _CreateIcon(string path, Color tint, float size)
    {
        var tex = GD.Load<Texture2D>(path);
        var rect = new TextureRect();
        rect.Texture = tex;
        rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        rect.CustomMinimumSize = new Vector2(size, size);
        rect.Modulate = tint;
        return rect;
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
