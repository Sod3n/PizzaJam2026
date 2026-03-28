using Godot;
using Deterministic.GameFramework.Types;
using SharedGD = Template.Shared.GameData.GD;
using Vector2 = Godot.Vector2;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;

namespace Template.Godot.Visuals;

public static class SkinVisualizer
{
    private static Dictionary<string, LayerData> _texturePathToLayer;
    private static LayerData _bodyLayerData; // Cache the "Body" layer specifically
    
    private class LayerData
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string TexturePath { get; set; }
        public List<LayerData> Children { get; set; }
        
        [System.Text.Json.Serialization.JsonIgnore]
        public LayerData Parent { get; set; }
    }

    private static void EnsureJsonLoaded()
    {
        if (_texturePathToLayer != null) return;

        _texturePathToLayer = new Dictionary<string, LayerData>();
        
        // Use FileAccess for Godot resource paths
        using var jsonFile = global::Godot.FileAccess.Open("res://sprites/Devochka.json", global::Godot.FileAccess.ModeFlags.Read);
        if (jsonFile == null)
        {
            GD.PrintErr("[SkinVisualizer] Could not open Devochka.json");
            return;
        }

        string jsonText = jsonFile.GetAsText();
        
        try 
        {
            var rootData = JsonSerializer.Deserialize<LayerData>(jsonText);
            if (rootData != null)
            {
                IndexLayers(rootData, null);
            }
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[SkinVisualizer] Failed to parse JSON: {e.Message}");
        }
    }

    private static void IndexLayers(LayerData node, LayerData parent)
    {
        node.Parent = parent;
        
        if (!string.IsNullOrEmpty(node.TexturePath))
        {
            // Normalize path to ensure matching
            _texturePathToLayer[node.TexturePath] = node;
        }
        
        // Identify Body Layer - assuming Name is "Body" from JSON
        if (node.Name == "Body")
        {
            _bodyLayerData = node;
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                IndexLayers(child, node);
            }
        }
    }

    public static void UpdateSkins(Node3D visualNode, Dictionary16<FixedString32, int> skins)
    {
        EnsureJsonLoaded();

        if (!GodotObject.IsInstanceValid(visualNode)) return;

        // 1. Locate Container and Body Anchor
        // Try to find ScaleAnchor/Skin container
        Node3D skinContainer = visualNode.GetNodeOrNull<Node3D>("ScaleAnchor/Skin");
        if (skinContainer == null)
        {
            skinContainer = visualNode.GetNodeOrNull<Node3D>("Character/ScaleAnchor/Skin");
        }

        if (skinContainer == null)
        {
            GD.PrintErr("[SkinVisualizer] Could not find 'ScaleAnchor/Skin' container.");
            return;
        }

        // Find Body Node to use as Anchor
        AnimatedSprite3D bodyNode = skinContainer.GetNodeOrNull<AnimatedSprite3D>("Body");
        
        // Establish Anchor Properties
        Vector2 anchorOffset = Vector2.Zero;
        Vector2 anchorPos3D = Vector2.Zero; // 3D Position (X, Y) part
        bool hasAnchor = false;

        if (bodyNode != null)
        {
            // Cache the initial offset set in Editor to avoid drift
            if (!bodyNode.HasMeta("base_offset"))
            {
                bodyNode.SetMeta("base_offset", bodyNode.Offset);
            }
            anchorOffset = bodyNode.GetMeta("base_offset").AsVector2();
            anchorPos3D = new Vector2(bodyNode.Position.X, bodyNode.Position.Y);
            hasAnchor = true;
        }
        else
        {
             // If Body node doesn't exist yet, we can't get editor offset. Use Zero.
             // We will create it in the loop if "Body" is in skins.
        }

        // Iterate through the skins dictionary
        for (int i = 0; i < skins.Count; i++)
        {
            var slotName = skins.Keys[i].ToString();
            var skinId = skins.Values[i];
            
            // Get or Create Sprite Node
            var spriteNode = skinContainer.GetNodeOrNull<AnimatedSprite3D>(slotName);
            
            if (spriteNode == null)
            {
                // Dynamic Creation
                spriteNode = new AnimatedSprite3D();
                spriteNode.Name = slotName;
                skinContainer.AddChild(spriteNode);
                spriteNode.Owner = visualNode.Owner; // Propagate ownership if needed (mainly for editor)
                
                // Copy basic properties from Body if available (PixelSize, Centered, etc)
                if (bodyNode != null)
                {
                    spriteNode.PixelSize = bodyNode.PixelSize;
                    spriteNode.Centered = bodyNode.Centered;
                    spriteNode.Billboard = bodyNode.Billboard;
                    spriteNode.TextureFilter = bodyNode.TextureFilter;
                    spriteNode.Position = bodyNode.Position; // Align 3D position
                }
                else
                {
                    // Default fallback
                    spriteNode.Centered = true; // Default to centered usually
                }
            }
            else
            {
                // Align existing node 3D position to anchor if it exists
                if (hasAnchor && spriteNode != bodyNode)
                {
                    spriteNode.Position = new global::Godot.Vector3(anchorPos3D.X, anchorPos3D.Y, spriteNode.Position.Z);
                }
            }

            ApplySkinTexture(spriteNode, skinId, bodyNode, anchorOffset);
        }
    }

    private static void ApplySkinTexture(AnimatedSprite3D sprite, int skinId, AnimatedSprite3D bodyNode, Vector2 anchorOffset)
    {
        // Handle "None" skin ID
        if (skinId == -1)
        {
            sprite.Visible = false;
            return;
        }

        var skinDef = SharedGD.SkinsData.Get(skinId);
        if (skinDef == null)
        {
            return; 
        }

        // Assume path in GameData is relative to "res://sprites/export/" 
        var texturePath = $"res://sprites/export/{skinDef.Path}.png";
        var texture = ResourceLoader.Load<Texture2D>(texturePath);

        if (texture != null)
        {
            // Setup SpriteFrames
            var frames = sprite.SpriteFrames?.Duplicate() as SpriteFrames;
            if (frames == null)
            {
                frames = new SpriteFrames();
                frames.AddAnimation("default");
            }
            
            if (!frames.HasAnimation("default"))
            {
                frames.AddAnimation("default");
            }

            if (frames.GetFrameCount("default") > 0)
            {
                frames.SetFrame("default", 0, texture);
            }
            else
            {
                frames.AddFrame("default", texture);
            }

            sprite.SpriteFrames = frames;
            sprite.Visible = true;

            // Apply Relative Positioning from JSON
            if (_texturePathToLayer != null && _texturePathToLayer.TryGetValue(texturePath, out var layerData))
            {
                // If we have a valid Body Anchor Layer and Node
                if (_bodyLayerData != null && bodyNode != null)
                {
                    // Calculate Difference in PSD space
                    float diffX = layerData.X - _bodyLayerData.X;
                    float diffY = layerData.Y - _bodyLayerData.Y;
                    
                    // We want to calculate the correct Offset for the Target Sprite
                    // Formula derived:
                    // TargetOffset = AnchorOffset + (DiffSizeCorrection) + (VisualShift)
                    // VisualShift = (DiffX, -DiffY) [Invert Y for Godot]
                    // DiffSizeCorrection:
                    //   If Centered: Origin is Center.
                    //   TargetTopLeft = TargetCenter + (-TargetW/2, TargetH/2)
                    //   AnchorTopLeft = AnchorCenter + (-AnchorW/2, AnchorH/2)
                    //   TargetTopLeft = AnchorTopLeft + (VisualShift)
                    //   TargetCenter + (-TargetW/2, TargetH/2) = AnchorCenter + (-AnchorW/2, AnchorH/2) + VisualShift
                    //   TargetOffset + ...                     = AnchorOffset + ...
                    //   TargetOffset = AnchorOffset + (TargetW/2 - AnchorW/2, AnchorH/2 - TargetH/2) + VisualShift
                    
                    float targetW = texture.GetWidth();
                    float targetH = texture.GetHeight();
                    // Need reference width/height. Can use _bodyLayerData dims or current Body Texture dims?
                    // Use _bodyLayerData for consistency with "Anchor" concept from JSON.
                    float anchorW = _bodyLayerData.Width;
                    float anchorH = _bodyLayerData.Height;
                    
                    Vector2 visualShift = new Vector2(diffX, -diffY);
                    
                    Vector2 finalOffset = anchorOffset + visualShift;
                    
                    if (sprite.Centered)
                    {
                        float sizeDiffX = (targetW / 2.0f) - (anchorW / 2.0f);
                        float sizeDiffY = (anchorH / 2.0f) - (targetH / 2.0f);
                        finalOffset += new Vector2(sizeDiffX, sizeDiffY);
                    }
                    
                    sprite.Offset = finalOffset;
                }
                else
                {
                     // Fallback if no body anchor found (e.g. only rendering one part?)
                     // Just center it or leave default offset?
                }
            }
        }
        else
        {
            GD.PrintErr($"[SkinVisualizer] Failed to load texture: {texturePath}");
        }
    }

    public static void UpdateColors(Node3D visualNode, Dictionary16<FixedString32, int> colors)
    {
        if (!GodotObject.IsInstanceValid(visualNode)) return;

        Node3D skinContainer = visualNode.GetNodeOrNull<Node3D>("ScaleAnchor/Skin");
        if (skinContainer == null)
        {
            skinContainer = visualNode.GetNodeOrNull<Node3D>("Character/ScaleAnchor/Skin");
        }

        if (skinContainer == null) return;

        for (int i = 0; i < colors.Count; i++)
        {
            var slotName = colors.Keys[i].ToString();
            var packed = colors.Values[i];

            var spriteNode = skinContainer.GetNodeOrNull<AnimatedSprite3D>(slotName);
            if (spriteNode == null) continue;

            float r = ((packed >> 16) & 0xFF) / 255f;
            float g = ((packed >> 8) & 0xFF) / 255f;
            float b = (packed & 0xFF) / 255f;
            spriteNode.Modulate = new Color(r, g, b);
        }
    }
}
