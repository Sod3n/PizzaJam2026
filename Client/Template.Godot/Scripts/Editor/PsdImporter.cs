using Godot;
using Ntreev.Library.Psd;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Template.Godot.Editor
{
    [Tool]
    public partial class PsdImporter : EditorScript
    {
        // Configuration
        private const string PsdPath = "res://sprites/Devochka.psd";
        private const string ExportRoot = "res://sprites/export";
        private const string TargetJsonPath = "res://sprites/Devochka.json";

        public override void _Run()
        {
            ImportPsd();
        }

        private void ImportPsd()
        {
            string globalPsdPath = ProjectSettings.GlobalizePath(PsdPath);
            string globalJsonPath = ProjectSettings.GlobalizePath(TargetJsonPath);
            
            if (!File.Exists(globalPsdPath))
            {
                GD.PrintErr($"PSD file not found at {globalPsdPath}");
                return;
            }

            GD.Print($"Importing PSD from: {globalPsdPath}");

            try
            {
                using (var document = PsdDocument.Create(globalPsdPath))
                {
                    GD.Print($"PSD Loaded. Size: {document.Width}x{document.Height}");

                    // Create Root Data
                    var rootData = new LayerData
                    {
                        Name = "Devochka",
                        Type = "Root",
                        Width = document.Width,
                        Height = document.Height,
                        X = 0,
                        Y = 0,
                        Opacity = 1.0f,
                        IsVisible = true
                    };

                    // Process Layers
                    ProcessLayers(document.Childs, rootData);

                    // Serialize to JSON
                    var options = new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    };
                    string jsonString = JsonSerializer.Serialize(rootData, options);
                    
                    File.WriteAllText(globalJsonPath, jsonString);
                    GD.Print($"Successfully saved JSON to {globalJsonPath}");
                    
                    // Refresh filesystem to show new file
                    EditorInterface.Singleton.GetResourceFilesystem().Scan();
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"Error importing PSD: {e.Message}\n{e.StackTrace}");
            }
        }

        private void ProcessLayers(IEnumerable<IPsdLayer> layers, LayerData parentData)
        {
            foreach (var layer in layers)
            {
                var childData = ProcessLayer(layer);
                if (childData != null)
                {
                    parentData.Children.Add(childData);
                }
            }
        }

        private LayerData ProcessLayer(IPsdLayer layer)
        {
            // Access properties safely
            string layerName = layer.Name;
            bool isVisible = true;
            float opacity = 1.0f;
            try {
                var visProp = layer.GetType().GetProperty("IsVisible");
                if (visProp != null) isVisible = (bool)visProp.GetValue(layer);
                
                var opacProp = layer.GetType().GetProperty("Opacity");
                if (opacProp != null) opacity = (float)opacProp.GetValue(layer);
            } catch {}

            int left = layer.Left;
            int top = layer.Top;
            int width = layer.Width;
            int height = layer.Height;
            
            // Check if it's a group (Folder)
            bool isGroup = layer.Childs.Length > 0;
            
            try 
            {
                var sectionTypeProp = layer.GetType().GetProperty("SectionType", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (sectionTypeProp != null)
                {
                     int sectionTypeVal = (int)sectionTypeProp.GetValue(layer);
                     if (sectionTypeVal == 1 || sectionTypeVal == 2) isGroup = true;
                }
            }
            catch {}

            var data = new LayerData
            {
                Name = SanitizeNodeName(layerName),
                X = left,
                Y = top,
                Width = width,
                Height = height,
                Opacity = opacity,
                IsVisible = isVisible,
                Type = isGroup ? "Group" : "Layer"
            };

            if (isGroup)
            {
                ProcessLayers(layer.Childs, data);
            }
            else
            {
                // Find Texture
                string texturePath = FindTexturePath(layer);
                if (!string.IsNullOrEmpty(texturePath))
                {
                    // Convert res:// path to something usable if needed, 
                    // or keep as res:// for Godot loader
                    data.TexturePath = texturePath;
                }
                else
                {
                    GD.Print($"No texture found for layer: '{layerName}' (Parent: '{layer.Parent?.Name}')");
                }
            }
            
            return data;
        }

        private class LayerData
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public float Opacity { get; set; }
            public bool IsVisible { get; set; }
            public string TexturePath { get; set; }
            public List<LayerData> Children { get; set; } = new List<LayerData>();
        }

        private string FindTexturePath(IPsdLayer layer)
        {
            // Strategy:
            // 1. Sanitize layer name (replace spaces/cyrillic with underscore)
            // 2. Look in ExportRoot/ParentName/LayerName.png
            // 3. Look in ExportRoot/LayerName.png
            
            string rawName = layer.Name;
            string parentName = layer.Parent?.Name ?? "";
            bool hasParent = !string.IsNullOrEmpty(parentName) && layer.Parent != layer.Document;

            // Krita Export sanitization logic observed:
            // - Spaces -> Underscore
            // - Cyrillic -> Underscore (or maybe just non-ascii?)
            // - "Back hair" -> "Back_hair"
            // - "Слой 118" -> "_____118"
            // - "1" -> "1"
            
            string sanitizedLayer = SanitizeKrita(rawName);
            string sanitizedParent = hasParent ? SanitizeKrita(parentName) : "";

            // Candidates to check
            List<string> candidates = new List<string>();

            if (hasParent)
            {
                // ExportRoot/Parent/Layer.png
                candidates.Add($"{ExportRoot}/{sanitizedParent}/{sanitizedLayer}.png");
                
                // Maybe parent name wasn't sanitized the same way? 
                // "Низ" -> "___"? 
                // "Face" -> "Face"
                // Let's try "All underscores" version of parent if generic sanitize fails
                string underscoredParent = AllUnderscores(parentName);
                candidates.Add($"{ExportRoot}/{underscoredParent}/{sanitizedLayer}.png");
            }
            else
            {
                // Root layer
                candidates.Add($"{ExportRoot}/{sanitizedLayer}.png");
            }

            foreach (var path in candidates)
            {
                if (ResourceLoader.Exists(path))
                    return path;
            }
            
            return null;
        }

        private string SanitizeNodeName(string name)
        {
            // Remove invalid chars for Godot nodes or keys
            return name.Replace(".", "").Replace(":", "").Replace("@", "").Replace("/", "").Replace("\"", "");
        }

        private string SanitizeKrita(string name)
        {
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ' ' || chars[i] > 127)
                {
                    chars[i] = '_';
                }
            }
            return new string(chars);
        }

        private string AllUnderscores(string name)
        {
            return SanitizeKrita(name);
        }
    }
}

