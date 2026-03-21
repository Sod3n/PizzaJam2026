using System;
using System.Threading.Tasks;
using Template.Shared.GameData.Core;

namespace Template.Shared.GameData;

public static partial class GD
{
    private static bool _isLoaded = false;
    
    public static async Task LoadAsync(string? basePath = null)
    {
        if (_isLoaded)
        {
            // Optional: Log warning or just return
            return;
        }
        
        basePath ??= GameDataLoader.FindDataPathFromAssembly("GameData");
        
        await LoadModels(basePath);
        
        _isLoaded = true;
    }

    public static void Load(string? basePath = null)
    {
        if (_isLoaded)
        {
            return;
        }
        
        basePath ??= GameDataLoader.FindDataPathFromAssembly("GameData");
        
        LoadModelsSync(basePath);
        
        _isLoaded = true;
    }
    
    private static Task Load<T>(GameData<T> model, string basePath)
    {
        return GameDataLoader.LoadAsync(model, basePath, JsonSettingsHelper.DefaultSettings);
    }

    private static void LoadSync<T>(GameData<T> model, string basePath)
    {
        GameDataLoader.Load(model, basePath, JsonSettingsHelper.DefaultSettings);
    }
    
    private static Task Load<T1, T2>(GameData<T1, T2> model, string basePath)
    {
        return GameDataLoader.LoadAsync(model, basePath, JsonSettingsHelper.DefaultSettings);
    }
    
    private static partial Task LoadModels(string basePath);
    private static partial void LoadModelsSync(string basePath);
    
    public static async Task ReloadAsync(string? basePath = null)
    {
        _isLoaded = false;
        await LoadAsync(basePath);
    }
    
    public static bool IsLoaded => _isLoaded;
}
