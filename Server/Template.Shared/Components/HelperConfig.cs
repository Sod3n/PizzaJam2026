namespace Template.Shared.Components;

/// <summary>
/// Configuration for each helper type: unique key, display name, and base stats.
/// Upgrade pets target helpers by key.
/// </summary>
public static class HelperConfig
{
    public record HelperInfo(string Key, string Name, int BaseCapacity, int UpgradedCapacity, float BaseSpeed, float UpgradedSpeed);

    public static readonly HelperInfo Assistant = new("assistant", "Ame", 0, 0, 8f, 16f);
    public static readonly HelperInfo Gatherer = new("gatherer", "Lefantis", 15, 45, 8f, 16f);
    public static readonly HelperInfo Seller = new("seller", "Mochi", 50, 200, 8f, 16f);
    public static readonly HelperInfo Builder = new("builder", "Brix", 50, 200, 8f, 16f);

    public static HelperInfo GetByType(int helperType) => helperType switch
    {
        HelperType.Assistant => Assistant,
        HelperType.Gatherer => Gatherer,
        HelperType.Seller => Seller,
        HelperType.Builder => Builder,
        _ => Assistant,
    };

    public static HelperInfo GetByKey(string key) => key switch
    {
        "assistant" => Assistant,
        "gatherer" => Gatherer,
        "seller" => Seller,
        "builder" => Builder,
        _ => null,
    };
}
