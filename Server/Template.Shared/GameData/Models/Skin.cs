namespace Template.Shared.GameData.Models;

public class Skin
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Weight { get; set; }
    public int Exhaust { get; set; }
}
