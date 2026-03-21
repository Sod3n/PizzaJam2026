using System.Threading.Tasks;
using Template.Shared.GameData.Skins;

namespace Template.Shared.GameData;

public static partial class GD
{
    public static SkinsData SkinsData { get; } = new();
    
    private static partial Task LoadModels(string basePath)
    {
        return Task.WhenAll(
            Load(SkinsData, basePath)
        );
    }

    private static partial void LoadModelsSync(string basePath)
    {
        LoadSync(SkinsData, basePath);
    }
}
