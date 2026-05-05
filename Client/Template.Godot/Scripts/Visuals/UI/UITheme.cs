using Godot;

namespace Template.Godot.Visuals;

/// <summary>
/// Shared UI color constants matching the bottom panel style in Player.tscn.
/// </summary>
public static class UITheme
{
    // Panel background — soft teal/mint from Player.tscn StyleBoxFlat_money/food
    public static readonly Color PanelBg = new(0.522f, 0.737f, 0.698f, 1f);
    // Darker variant for overlay background dim
    public static readonly Color OverlayDim = new(0.05f, 0.08f, 0.07f, 0.85f);
    // Node/card background — lighter tint of the panel
    public static readonly Color CardBg = new(0.45f, 0.65f, 0.62f, 1f);
    // Border — black like Player.tscn pills
    public static readonly Color Border = new(0f, 0f, 0f, 1f);
    // Title text — black with white outline
    public static readonly Color Title = new(0.05f, 0.05f, 0.05f, 1f);
    // Subtitle/secondary text
    public static readonly Color Subtitle = new(0.15f, 0.15f, 0.15f, 1f);
    // Text outline color
    public static readonly Color TextOutline = new(1f, 1f, 1f, 1f);
    // Tree connection lines
    public static readonly Color Line = new(0.3f, 0.45f, 0.4f, 0.7f);
    // Cross-link lines (secondary parent)
    public static readonly Color CrossLink = new(0.4f, 0.55f, 0.5f, 0.5f);
    // Corner radius matching pills
    public const int CornerRadius = 12;
    // Border width
    public const int BorderWidth = 2;
    // Text outline size
    public const int TextOutlineSize = 6;

    /// <summary>Apply themed text style (white with black outline) to a Label.</summary>
    public static void StyleLabel(Label label, bool isTitle = true)
    {
        label.AddThemeColorOverride("font_color", isTitle ? Title : Subtitle);
        label.AddThemeColorOverride("font_outline_color", TextOutline);
        label.AddThemeConstantOverride("outline_size", TextOutlineSize);
    }
}
