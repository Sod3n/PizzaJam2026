namespace Template.Shared.GameData.Models;

public class Skin
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Weight { get; set; }
    public int Exhaust { get; set; }

    // ';'-separated type names this piece hides (e.g. "Bottom1;Bottom2"). null = no effect.
    public string? Empty { get; set; }

    // ','-separated piece ids this piece cannot coexist with. Bidirectional — mirrored on save by the editor. null = no constraint.
    public string? Incompatible { get; set; }
}
