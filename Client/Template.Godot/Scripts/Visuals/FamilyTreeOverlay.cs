using Godot;
using System.Collections.Generic;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Shared.Components;
using Template.Godot.Twitch;

namespace Template.Godot.Visuals;

/// <summary>
/// Full-screen overlay that displays the breeding lineage of all cows as a family tree.
/// Opened via keyboard shortcut (F key). Blocks game input while visible.
/// </summary>
public partial class FamilyTreeOverlay : CanvasLayer
{
    private static FamilyTreeOverlay _current;
    public static bool IsActive => _current != null && Node.IsInstanceValid(_current);

    private static readonly PackedScene _scene =
        GD.Load<PackedScene>("res://Scenes/FamilyTreeOverlay.tscn");

    // Layout constants
    private const float NodeWidth = 180f;
    private const float NodeHeight = 160f;
    private const float ThumbnailSize = 100f;
    private const float HorizontalGap = 40f;
    private const float VerticalGap = 80f;
    private const float TreePadding = 40f;

    private static readonly PackedScene _characterScene =
        GD.Load<PackedScene>("res://templates/characters/Character.tscn");

    private static readonly Shader _smoothShader =
        GD.Load<Shader>("res://shaders/smooth_character.gdshader");

    // Food tier names and colors
    private static readonly string[] FoodNames = { "Grass", "Carrot", "Apple", "Mushroom" };
    private static readonly Color[] FoodColors =
    {
        new(0.25f, 0.55f, 0.15f),  // Grass - darker green (distinct from mint bg)
        new(0.9f, 0.55f, 0.15f),  // Carrot - orange
        new(0.85f, 0.25f, 0.25f), // Apple - red
        new(0.6f, 0.3f, 0.8f),    // Mushroom - purple
    };

    private static readonly Color LineColor = UITheme.Line;
    private static readonly Color BgColor = UITheme.OverlayDim;
    private static readonly Color NodeBgColor = UITheme.CardBg;
    private static readonly Color NodeBorderColor = UITheme.Border;
    private static readonly Color TitleColor = UITheme.Title;
    private static readonly Color SubtitleColor = UITheme.Subtitle;

    // Tree data
    private static readonly string[] HelperTypeNames = { "Assistant", "Gatherer", "Seller", "Builder", "Milker" };
    private static readonly Color HelperColor = new(0.3f, 0.8f, 0.9f); // cyan

    private struct CowNode
    {
        public Entity Entity;
        public string Name;
        public int PreferredFood; // -1 for helpers
        public string Subtitle;   // food name or helper type
        public Entity ParentA;
        public Entity ParentB;
        public List<Entity> Children;
        public SkinComponent? Skin;
        // Layout
        public float X;
        public float Y;
    }

    // ── Static API ───────────────────────────────────────────────────────

    public static void Toggle(SceneTree tree)
    {
        if (IsActive)
        {
            _current._Dismiss();
            return;
        }
        Show(tree);
    }

    public static void Show(SceneTree tree)
    {
        if (IsActive) return;
        if (_scene == null) return;

        var overlay = _scene.Instantiate<FamilyTreeOverlay>();
        _current = overlay;
        tree.Root.AddChild(overlay);
        overlay._Build();
    }

    // ── Build the tree ───────────────────────────────────────────────────

    private ScrollContainer _scroll;
    private Control _treeContainer;
    private readonly Dictionary<int, CowNode> _nodes = new();

    private void _Build()
    {
        var state = ReactiveSystem.Instance.BoundState;
        if (state == null) { _Dismiss(); return; }

        // 1. Collect all cows
        foreach (var entity in state.Filter<CowComponent>())
        {
            if (!state.HasComponent<CowComponent>(entity)) continue;
            var cow = state.GetComponent<CowComponent>(entity);

            string name = TwitchIntegration.GetDisplayName(entity);

            SkinComponent? skin = null;
            if (state.HasComponent<SkinComponent>(entity))
                skin = state.GetComponent<SkinComponent>(entity);

            int food = cow.PreferredFood;
            string subtitle = food >= 0 && food < FoodNames.Length ? FoodNames[food] : "Unknown";

            _nodes[entity.Id] = new CowNode
            {
                Entity = entity,
                Name = name,
                PreferredFood = food,
                Subtitle = subtitle,
                ParentA = cow.ParentA,
                ParentB = cow.ParentB,
                Children = new List<Entity>(),
                Skin = skin,
            };
        }

        // 1b. Collect all helpers
        foreach (var entity in state.Filter<HelperComponent>())
        {
            if (!state.HasComponent<HelperComponent>(entity)) continue;
            var helper = state.GetComponent<HelperComponent>(entity);

            string name = TwitchIntegration.GetDisplayName(entity);

            SkinComponent? skin = null;
            if (state.HasComponent<SkinComponent>(entity))
                skin = state.GetComponent<SkinComponent>(entity);

            int hType = helper.Type;
            string subtitle = hType >= 0 && hType < HelperTypeNames.Length ? HelperTypeNames[hType] : "Helper";

            Entity parentA = helper.ParentA;
            Entity parentB = helper.ParentB;

            _nodes[entity.Id] = new CowNode
            {
                Entity = entity,
                Name = name,
                PreferredFood = -1,
                Subtitle = subtitle,
                ParentA = parentA,
                ParentB = parentB,
                Children = new List<Entity>(),
                Skin = skin,
            };
        }

        // 2. Build child lists (each child goes under ParentA for layout; ParentB gets a cross-link line)
        foreach (var kvp in _nodes)
        {
            var node = kvp.Value;
            bool addedUnderA = false;
            if (node.ParentA.Id > 0 && node.ParentA != Entity.Null && _nodes.ContainsKey(node.ParentA.Id))
            {
                _nodes[node.ParentA.Id].Children.Add(node.Entity);
                addedUnderA = true;
            }
            // Only add under ParentB if not already under ParentA (keeps layout as a proper tree)
            if (!addedUnderA && node.ParentB.Id > 0 && node.ParentB != Entity.Null && _nodes.ContainsKey(node.ParentB.Id))
            {
                _nodes[node.ParentB.Id].Children.Add(node.Entity);
            }
        }

        // 3. Find roots (cows with no parents, or whose parents don't exist)
        var roots = new List<int>();
        foreach (var kvp in _nodes)
        {
            var node = kvp.Value;
            bool hasParentA = node.ParentA.Id > 0 && node.ParentA != Entity.Null && _nodes.ContainsKey(node.ParentA.Id);
            bool hasParentB = node.ParentB.Id > 0 && node.ParentB != Entity.Null && _nodes.ContainsKey(node.ParentB.Id);
            if (!hasParentA && !hasParentB)
                roots.Add(kvp.Key);
        }

        // Sort roots by entity ID for stable ordering
        roots.Sort();

        // Handle empty state
        if (_nodes.Count == 0)
        {
            _scroll = GetNode<ScrollContainer>("Background/ScrollContainer");
            _treeContainer = GetNode<Control>("Background/ScrollContainer/TreeContainer");
            var emptyLabel = new Label();
            emptyLabel.Text = "No cows yet! Tame some cows and start breeding.";
            emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            emptyLabel.AddThemeColorOverride("font_color", SubtitleColor);
            emptyLabel.AddThemeFontSizeOverride("font_size", 20);
            emptyLabel.Position = new Vector2(400, 200);
            _treeContainer.AddChild(emptyLabel);
            return;
        }

        // 4. Layout: assign X/Y positions using a simple depth-first approach
        //    Each root gets a column region. Children are placed below their parents.
        float currentX = TreePadding;
        foreach (var rootId in roots)
        {
            float subtreeWidth = CalculateSubtreeWidth(rootId);
            LayoutSubtree(rootId, currentX, TreePadding, subtreeWidth);
            currentX += subtreeWidth + HorizontalGap;
        }

        // 5. Determine total canvas size
        float maxX = 0, maxY = 0;
        foreach (var kvp in _nodes)
        {
            var n = kvp.Value;
            if (n.X + NodeWidth > maxX) maxX = n.X + NodeWidth;
            if (n.Y + NodeHeight > maxY) maxY = n.Y + NodeHeight;
        }

        // Cache scene nodes
        _scroll = GetNode<ScrollContainer>("Background/ScrollContainer");
        _treeContainer = GetNode<Control>("Background/ScrollContainer/TreeContainer");
        _treeContainer.CustomMinimumSize = new Vector2(maxX + TreePadding, maxY + TreePadding);

        // 6. Create visual nodes
        var lineDrawer = new TreeLineDrawer();
        _treeContainer.AddChild(lineDrawer);
        lineDrawer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Cross-link color (dashed look via lighter color) for secondary parent lines
        var crossLinkColor = UITheme.CrossLink;

        foreach (var kvp in _nodes)
        {
            var node = kvp.Value;
            _CreateNodeVisual(node);

            // Draw lines from this node to its children (primary parent links)
            foreach (var childEntity in node.Children)
            {
                if (_nodes.TryGetValue(childEntity.Id, out var child))
                {
                    lineDrawer.AddLine(
                        new Vector2(node.X + NodeWidth / 2f, node.Y + NodeHeight),
                        new Vector2(child.X + NodeWidth / 2f, child.Y),
                        LineColor
                    );
                }
            }

            // Draw cross-link from ParentB to this node (if ParentB exists and is different from layout parent)
            if (node.ParentB.Id > 0 && node.ParentB != Entity.Null
                && node.ParentB != node.ParentA
                && _nodes.TryGetValue(node.ParentB.Id, out var parentB))
            {
                // Only draw if this child is NOT in ParentB's children list (i.e., laid out under ParentA)
                bool isUnderParentB = false;
                foreach (var c in parentB.Children)
                {
                    if (c.Id == node.Entity.Id) { isUnderParentB = true; break; }
                }
                if (!isUnderParentB)
                {
                    lineDrawer.AddLine(
                        new Vector2(parentB.X + NodeWidth / 2f, parentB.Y + NodeHeight),
                        new Vector2(node.X + NodeWidth / 2f, node.Y),
                        crossLinkColor
                    );
                }
            }
        }

        // Move line drawer behind node panels
        _treeContainer.MoveChild(lineDrawer, 0);

        // Fade in
        var bg = GetNode<ColorRect>("Background");
        bg.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(bg, "modulate:a", 1f, 0.2f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    private float CalculateSubtreeWidth(int nodeId)
    {
        var node = _nodes[nodeId];
        // Deduplicate children (a child with two parents in the tree appears in both lists)
        var uniqueChildren = DeduplicateChildren(node.Children);

        if (uniqueChildren.Count == 0)
            return NodeWidth;

        float total = 0;
        for (int i = 0; i < uniqueChildren.Count; i++)
        {
            if (i > 0) total += HorizontalGap;
            total += CalculateSubtreeWidth(uniqueChildren[i].Id);
        }
        return Mathf.Max(total, NodeWidth);
    }

    private void LayoutSubtree(int nodeId, float regionX, float y, float regionWidth)
    {
        var node = _nodes[nodeId];
        // Center node in its region
        node.X = regionX + (regionWidth - NodeWidth) / 2f;
        node.Y = y;
        _nodes[nodeId] = node;

        var uniqueChildren = DeduplicateChildren(node.Children);
        if (uniqueChildren.Count == 0) return;

        // Calculate total children width
        float totalChildWidth = 0;
        var childWidths = new float[uniqueChildren.Count];
        for (int i = 0; i < uniqueChildren.Count; i++)
        {
            childWidths[i] = CalculateSubtreeWidth(uniqueChildren[i].Id);
            totalChildWidth += childWidths[i];
            if (i > 0) totalChildWidth += HorizontalGap;
        }

        float childY = y + NodeHeight + VerticalGap;
        float childX = regionX + (regionWidth - totalChildWidth) / 2f;

        for (int i = 0; i < uniqueChildren.Count; i++)
        {
            LayoutSubtree(uniqueChildren[i].Id, childX, childY, childWidths[i]);
            childX += childWidths[i] + HorizontalGap;
        }
    }

    /// <summary>Returns unique child entities, preferring the first occurrence.</summary>
    private static List<Entity> DeduplicateChildren(List<Entity> children)
    {
        var seen = new HashSet<int>();
        var result = new List<Entity>();
        foreach (var c in children)
        {
            if (seen.Add(c.Id))
                result.Add(c);
        }
        return result;
    }

    private void _CreateNodeVisual(CowNode node)
    {
        var panel = new PanelContainer();
        panel.Position = new Vector2(node.X, node.Y);
        panel.Size = new Vector2(NodeWidth, NodeHeight);

        // Style the panel
        int food = node.PreferredFood;
        Color foodColor = food >= 0 && food < FoodColors.Length ? FoodColors[food]
            : food == -1 ? HelperColor : SubtitleColor;

        var style = new StyleBoxFlat();
        style.BgColor = NodeBgColor;
        style.BorderColor = foodColor;
        style.SetBorderWidthAll(2);
        style.BorderWidthLeft = 4;
        style.SetCornerRadiusAll(6);
        style.ContentMarginLeft = 4;
        style.ContentMarginRight = 4;
        style.ContentMarginTop = 4;
        style.ContentMarginBottom = 4;
        panel.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        panel.AddChild(vbox);

        // Cow thumbnail via SubViewport
        if (node.Skin.HasValue && _characterScene != null)
        {
            var container = new SubViewportContainer();
            container.Stretch = true;
            container.CustomMinimumSize = new Vector2(ThumbnailSize, ThumbnailSize);
            container.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

            var viewport = new SubViewport();
            viewport.OwnWorld3D = true;
            viewport.TransparentBg = true;
            viewport.HandleInputLocally = false;
            viewport.Size = new Vector2I(200, 200);
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            container.AddChild(viewport);

            // Environment with ambient light
            var env = new global::Godot.Environment();
            env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
            env.AmbientLightColor = Colors.White;
            env.AmbientLightEnergy = 2f;
            var worldEnv = new WorldEnvironment();
            worldEnv.Environment = env;
            viewport.AddChild(worldEnv);

            // Camera
            var camera = new Camera3D();
            camera.Projection = Camera3D.ProjectionType.Orthogonal;
            camera.Size = 3f;
            camera.Transform = new Transform3D(
                Basis.Identity, new Vector3(0, 5, 4)
            ).LookingAt(new Vector3(0, 1.5f, 0), Vector3.Up);
            viewport.AddChild(camera);

            // Character instance
            var charNode = _characterScene.Instantiate<Node3D>();
            viewport.AddChild(charNode);
            Callable.From(() =>
            {
                if (!IsInstanceValid(charNode)) return;
                charNode.Call("stop_idle");
                SkinVisualizer.UpdateSkins(charNode, node.Skin.Value.Skins);
                SkinVisualizer.UpdateColors(charNode, node.Skin.Value.Colors);
                _StripPixelShaders(charNode);
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            }).CallDeferred();

            vbox.AddChild(container);
        }

        // Cow name
        var nameLabel = new Label();
        nameLabel.Text = node.Name;
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(nameLabel);
        nameLabel.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(nameLabel);

        // Subtitle (food tier or helper type)
        var foodLabel = new Label();
        foodLabel.Text = node.Subtitle;
        foodLabel.HorizontalAlignment = HorizontalAlignment.Center;
        foodLabel.AddThemeColorOverride("font_color", foodColor);
        foodLabel.AddThemeColorOverride("font_outline_color", UITheme.TextOutline);
        foodLabel.AddThemeConstantOverride("outline_size", UITheme.TextOutlineSize);
        foodLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(foodLabel);

        _treeContainer.AddChild(panel);
    }

    private static void _StripPixelShaders(Node node)
    {
        if (node is GeometryInstance3D geo && geo.MaterialOverride is ShaderMaterial mat)
        {
            var smooth = (ShaderMaterial)mat.Duplicate();
            smooth.Shader = _smoothShader;
            geo.MaterialOverride = smooth;
        }
        if (node is SpriteBase3D sprite)
            sprite.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
        foreach (var child in node.GetChildren())
            _StripPixelShaders(child);
    }

    // ── Input ────────────────────────────────────────────────────────────

    public override void _Input(InputEvent @event)
    {
        // Block all input from propagating while overlay is active
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.IsEcho())
        {
            // Escape or F to close
            if (keyEvent.Keycode == Key.Escape || keyEvent.Keycode == Key.F)
            {
                GetViewport().SetInputAsHandled();
                _Dismiss();
                return;
            }
        }

        // Consume mouse clicks so they don't reach the game
        if (@event is InputEventMouseButton { Pressed: true })
        {
            // Let scroll container handle scrolling, but block propagation to game
            // Don't call SetInputAsHandled here so scroll works
        }
    }

    // ── Dismiss ──────────────────────────────────────────────────────────

    private void _Dismiss()
    {
        if (!IsInsideTree()) return;

        var bg = GetNode<ColorRect>("Background");
        var tween = CreateTween();
        tween.TweenProperty(bg, "modulate:a", 0f, 0.15f);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            _current = null;
            QueueFree();
        }));
    }
}

/// <summary>Custom Control that draws connecting lines between tree nodes.</summary>
public partial class TreeLineDrawer : Control
{
    private readonly List<(Vector2 from, Vector2 to, Color color)> _lines = new();

    public void AddLine(Vector2 from, Vector2 to, Color color)
    {
        _lines.Add((from, to, color));
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var (from, to, color) in _lines)
        {
            // Draw an elbow connector: down from parent, then horizontal, then down to child
            float midY = (from.Y + to.Y) / 2f;
            DrawLine(from, new Vector2(from.X, midY), color, 2f, true);
            DrawLine(new Vector2(from.X, midY), new Vector2(to.X, midY), color, 2f, true);
            DrawLine(new Vector2(to.X, midY), to, color, 2f, true);
        }
    }
}
