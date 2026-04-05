namespace Template.Shared.Components;

/// <summary>
/// Configuration for each helper type: unique key, display name, and base stats.
/// Upgrade pets target helpers by key.
/// </summary>
public static class HelperConfig
{
    public record HelperInfo(string Key, string Name, int BaseCapacity, int UpgradedCapacity, float BaseSpeed, float UpgradedSpeed);

    public static readonly HelperInfo Assistant = new("assistant", "Ame", 0, 0, 5f, 10f);
    public static readonly HelperInfo Gatherer = new("gatherer", "Lefantis", 75, 120, 2f, 6f);
    public static readonly HelperInfo Seller = new("seller", "Mochi", 500, 1000, 2f, 6f);
    public static readonly HelperInfo Builder = new("builder", "Brix", 500, 1000, 2f, 6f);
    public static readonly HelperInfo Milker = new("milker", "Daisy", 125, 250, 2f, 6f);

    public static HelperInfo GetByType(int helperType) => helperType switch
    {
        HelperType.Assistant => Assistant,
        HelperType.Gatherer => Gatherer,
        HelperType.Seller => Seller,
        HelperType.Builder => Builder,
        HelperType.Milker => Milker,
        _ => Assistant,
    };

    public static HelperInfo GetByKey(string key) => key switch
    {
        "assistant" => Assistant,
        "gatherer" => Gatherer,
        "seller" => Seller,
        "builder" => Builder,
        "milker" => Milker,
        _ => null,
    };
}
