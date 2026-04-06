using System.Text.Json;

namespace Template.Shared.GameData.Core;

public static class JsonSettingsHelper
{
    public static readonly JsonSerializerOptions DefaultSettings = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
