using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Template.Shared.GameData.Core;

public static class GameDataLoader
{
    public static async Task LoadAsync<T>(GameData<T> gameData, string basePath, JsonSerializerSettings settings)
    {
        var filePath = System.IO.Path.Combine(basePath, gameData.Path);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"GameData file not found: {filePath}");
        }

        var json = await File.ReadAllTextAsync(filePath);
        var entries = JsonConvert.DeserializeObject<Dictionary<string, T>>(json, settings);

        if (entries == null)
        {
            entries = new Dictionary<string, T>();
        }

        gameData.Load(entries);
    }

    public static void Load<T>(GameData<T> gameData, string basePath, JsonSerializerSettings settings)
    {
        var filePath = System.IO.Path.Combine(basePath, gameData.Path);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"GameData file not found: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        var entries = JsonConvert.DeserializeObject<Dictionary<string, T>>(json, settings);

        if (entries == null)
        {
            entries = new Dictionary<string, T>();
        }

        gameData.Load(entries);
    }

    public static void LoadRaw<T>(GameData<T> gameData, string json, JsonSerializerSettings settings)
    {
        var entries = JsonConvert.DeserializeObject<Dictionary<string, T>>(json, settings);

        if (entries == null)
        {
            entries = new Dictionary<string, T>();
        }

        gameData.Load(entries);
    }

    public static string FindDataPathFromAssembly(string relativePath)
    {
        string? assemblyDir = null;
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;

        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            assemblyDir = System.IO.Path.GetDirectoryName(assemblyLocation);
        }

        if (string.IsNullOrEmpty(assemblyDir))
        {
            assemblyDir = AppContext.BaseDirectory;
        }

        // Try to find the path relative to the assembly
        var path = System.IO.Path.Combine(assemblyDir!, relativePath);
        if (Directory.Exists(path))
        {
            return path;
        }

        // Fallback: try to find it in the project structure (useful for development)
        // Adjust this logic based on your project structure
        var currentDir = new DirectoryInfo(assemblyDir!);
        while (currentDir != null && currentDir.Exists)
        {
            var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(currentDir.FullName, "Server", "Template.Shared", relativePath));
            if (Directory.Exists(target))
            {
                return target;
            }

             target = System.IO.Path.GetFullPath(System.IO.Path.Combine(currentDir.FullName, relativePath));
             if (Directory.Exists(target))
            {
                return target;
            }

            currentDir = currentDir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find data path '{relativePath}' starting from '{assemblyDir}' (Full path: {System.IO.Path.GetFullPath(relativePath)})");
    }
}
