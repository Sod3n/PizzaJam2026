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

        if (string.IsNullOrEmpty(basePath))
        {
            basePath = GameDataLoader.FindDataPathFromAssembly("GameData");
        }

        // This is for debugging in Godot, we use the logger if available
        Deterministic.GameFramework.Utils.Logging.ILogger.Log($"[GD] Loading GameData from: {basePath}");

        LoadModelsSync(basePath);

        _isLoaded = true;
    }

    public static void LoadFromJson(System.Collections.Generic.Dictionary<string, string> jsonMap)
    {
        if (_isLoaded)
        {
            return;
        }

        LoadModelsRaw(jsonMap);

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

    private static void LoadRaw<T>(GameData<T> model, string json)
    {
        GameDataLoader.LoadRaw(model, json, JsonSettingsHelper.DefaultSettings);
    }

    private static Task Load<T1, T2>(GameData<T1, T2> model, string basePath)
    {
        return GameDataLoader.LoadAsync(model, basePath, JsonSettingsHelper.DefaultSettings);
    }

    private static partial Task LoadModels(string basePath);
    private static partial void LoadModelsSync(string basePath);
    private static partial void LoadModelsRaw(System.Collections.Generic.Dictionary<string, string> jsonMap);

    public static async Task ReloadAsync(string? basePath = null)
    {
        _isLoaded = false;
        await LoadAsync(basePath);
    }

    public static bool IsLoaded => _isLoaded;
}
