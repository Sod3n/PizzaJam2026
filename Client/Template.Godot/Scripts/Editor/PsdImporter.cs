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
    public partial class PsdImporter : Node
    {
        // Configuration
        private const string SpritesRoot = "res://sprites";
        private const string ExportRoot = "res://sprites/export";

        public void _Run()
        {
            string globalSpritesRoot = ProjectSettings.GlobalizePath(SpritesRoot);
            var psdFiles = Directory.GetFiles(globalSpritesRoot, "*.psd", SearchOption.TopDirectoryOnly);

            if (psdFiles.Length == 0)
            {
                GD.Print("No PSD files found in sprites/");
                return;
            }

            GD.Print($"Found {psdFiles.Length} PSD file(s) to import");
            foreach (var psdFile in psdFiles)
            {
                ImportPsd(psdFile);
            }

            EditorInterface.Singleton.GetResourceFilesystem().Scan();
        }

        private void ImportPsd(string globalPsdPath)
        {
            string psdName = Path.GetFileNameWithoutExtension(globalPsdPath);
            string globalJsonPath = Path.Combine(Path.GetDirectoryName(globalPsdPath), psdName + ".json");
            string globalExportRoot = ProjectSettings.GlobalizePath(ExportRoot + "/" + psdName);

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
                        Name = psdName,
                        Type = "Root",
                        Width = document.Width,
                        Height = document.Height,
                        X = 0,
                        Y = 0,
                        Opacity = 1.0f,
                        IsVisible = true
                    };

                    // Process Layers — generate metadata and extract images
                    ProcessLayers(document.Childs, rootData, globalExportRoot, psdName);

                    // Serialize to JSON
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    };
                    string jsonString = JsonSerializer.Serialize(rootData, options);

                    File.WriteAllText(globalJsonPath, jsonString);
                    GD.Print($"Successfully saved JSON to {globalJsonPath}");
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"Error importing PSD: {e.Message}\n{e.StackTrace}");
            }
        }

        private void ProcessLayers(IEnumerable<IPsdLayer> layers, LayerData parentData, string globalExportRoot, string psdName)
        {
            foreach (var layer in layers)
            {
                var childData = ProcessLayer(layer, globalExportRoot, psdName);
                if (childData != null)
                {
                    parentData.Children.Add(childData);
                }
            }
        }

        private LayerData ProcessLayer(IPsdLayer layer, string globalExportRoot, string psdName)
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
            // Use actual layer bounds, not document dimensions
            int layerWidth = layer.Right - layer.Left;
            int layerHeight = layer.Bottom - layer.Top;

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
                Width = layerWidth > 0 ? layerWidth : layer.Width,
                Height = layerHeight > 0 ? layerHeight : layer.Height,
                Opacity = opacity,
                IsVisible = isVisible,
                Type = isGroup ? "Group" : "Layer"
            };

            if (isGroup)
            {
                ProcessLayers(layer.Childs, data, globalExportRoot, psdName);
            }
            else
            {
                // Build texture path and extract image, get cropped bounds
                string texturePath = BuildTexturePath(layer, psdName);
                if (!string.IsNullOrEmpty(texturePath))
                {
                    data.TexturePath = texturePath;
                    var bounds = ExtractLayerImage(layer, texturePath, globalExportRoot);
                    if (bounds != null)
                    {
                        data.X = bounds[0];
                        data.Y = bounds[1];
                        data.Width = bounds[2];
                        data.Height = bounds[3];
                    }
                }
                else
                {
                    GD.Print($"No texture path for layer: '{layerName}' (Parent: '{layer.Parent?.Name}')");
                }
            }

            return data;
        }

        /// <summary>Returns [x, y, width, height] of the content bounds, or null on failure.</summary>
        private int[] ExtractLayerImage(IPsdLayer layer, string resTexturePath, string globalExportRoot)
        {
            bool hasImage = false;
            try
            {
                var hasImageProp = layer.GetType().GetProperty("HasImage");
                if (hasImageProp != null) hasImage = (bool)hasImageProp.GetValue(layer);
            }
            catch {}

            if (!hasImage)
            {
                GD.Print($"Skipping layer '{layer.Name}': HasImage=false (Left={layer.Left} Top={layer.Top} Right={layer.Right} Bottom={layer.Bottom})");
                return null;
            }

            try
            {
                byte[] bgra = Extensions.MergeChannels((IImageSource)layer);
                if (bgra == null || bgra.Length == 0) return null;

                // Infer full image dimensions from data (assume BGRA = 4 bytes/pixel)
                int totalPixels = bgra.Length / 4;
                int fullW = layer.Width;
                int fullH = layer.Height;
                if (totalPixels != fullW * fullH)
                {
                    // Try layer bounds
                    int lw = layer.Right - layer.Left;
                    int lh = layer.Bottom - layer.Top;
                    if (lw > 0 && lh > 0 && totalPixels == lw * lh)
                    {
                        fullW = lw;
                        fullH = lh;
                    }
                    else
                    {
                        // Last resort: try document dimensions from the root
                        int docW = layer.Document?.Width ?? 0;
                        int docH = layer.Document?.Height ?? 0;
                        if (docW > 0 && docH > 0 && totalPixels == docW * docH)
                        {
                            fullW = docW;
                            fullH = docH;
                            GD.Print($"Layer '{layer.Name}' has zero bounds, using document dimensions {docW}x{docH}");
                        }
                        else return null;
                    }
                }

                // Convert ABGR (Ntreev MergeChannels output) to RGBA
                byte[] rgba = new byte[bgra.Length];
                for (int i = 0; i < bgra.Length; i += 4)
                {
                    rgba[i + 0] = bgra[i + 3]; // R
                    rgba[i + 1] = bgra[i + 2]; // G
                    rgba[i + 2] = bgra[i + 1]; // B
                    rgba[i + 3] = bgra[i + 0]; // A
                }

                // Find non-transparent content bounds
                int minX = fullW, minY = fullH, maxX = -1, maxY = -1;
                for (int y = 0; y < fullH; y++)
                {
                    for (int x = 0; x < fullW; x++)
                    {
                        int alpha = rgba[(y * fullW + x) * 4 + 3];
                        if (alpha > 0)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }

                if (maxX < 0 || maxY < 0) return null; // fully transparent

                int cropX = minX;
                int cropY = minY;
                int cropW = maxX - minX + 1;
                int cropH = maxY - minY + 1;

                // Crop to content bounds
                byte[] cropped = new byte[cropW * cropH * 4];
                for (int row = 0; row < cropH; row++)
                {
                    int srcOffset = ((cropY + row) * fullW + cropX) * 4;
                    int dstOffset = row * cropW * 4;
                    System.Buffer.BlockCopy(rgba, srcOffset, cropped, dstOffset, cropW * 4);
                }

                var image = Image.CreateFromData(cropW, cropH, false, Image.Format.Rgba8, cropped);

                // Build output path — globalise the res:// texture path directly
                string globalPath = ProjectSettings.GlobalizePath(resTexturePath);

                string dir = Path.GetDirectoryName(globalPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var err = image.SavePng(globalPath);
                if (err != Error.Ok)
                {
                    GD.PrintErr($"Failed to save PNG for '{layer.Name}' to {globalPath}: {err}");
                    return null;
                }

                // Account for layer.Left/Top offset (if library provides non-zero bounds)
                int finalX = layer.Left + cropX;
                int finalY = layer.Top + cropY;

                GD.Print($"Extracted: {layer.Name} -> {globalPath} ({cropW}x{cropH} at {finalX},{finalY})");
                return new int[] { finalX, finalY, cropW, cropH };
            }
            catch (Exception e)
            {
                GD.PrintErr($"Error extracting layer '{layer.Name}': {e.Message}\n{e.StackTrace}");
                return null;
            }
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

        private string BuildTexturePath(IPsdLayer layer, string psdName)
        {
            string rawName = layer.Name;
            string parentName = layer.Parent?.Name ?? "";
            bool hasParent = !string.IsNullOrEmpty(parentName) && layer.Parent != layer.Document;

            string sanitizedLayer = SanitizeKrita(rawName);
            string sanitizedParent = hasParent ? SanitizeKrita(parentName) : "";

            if (hasParent)
            {
                return $"{ExportRoot}/{psdName}/{sanitizedParent}/{sanitizedLayer}.png";
            }
            else
            {
                return $"{ExportRoot}/{psdName}/{sanitizedLayer}.png";
            }
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
    }
}
