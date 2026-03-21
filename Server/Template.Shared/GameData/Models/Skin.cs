using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Template.Shared.GameData.Models;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
public class Skin
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Weight { get; set; }
    public int Exhaust { get; set; }
}
