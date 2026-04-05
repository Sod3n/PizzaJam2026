using System.Collections.Generic;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Godot;
using Template.Godot.Visuals;
using Template.Shared.Components;

namespace Template.Godot.Twitch;

/// <summary>
/// Bridges TwitchService events to in-game mechanics:
///   - Names cows after chatters (client-side override)
///   - Love Confession reward: shows a love popup for the chatter's cow
///   - Say Something reward: shows a speech bubble above the chatter's cow
///
/// This is a static helper; no scene node is required. Call Initialize() once
/// after the game starts (e.g., from GameManager.OnGameStarted).
/// </summary>
public static class TwitchIntegration
{
    // ---- Client-side name overrides ----
    // Maps entity ID -> display name override (chatter username).
    // All systems that read NameComponent for display should check here first.
    private static readonly Dictionary<int, string> _nameOverrides = new();

    // Reverse lookup: chatter username (lowercase) -> entity ID
    private static readonly Dictionary<string, int> _chatterToEntity = new();

    private static bool _initialized;

    /// <summary>
    /// Call once when the game starts to hook up Twitch event subscriptions.
    /// Safe to call multiple times; only the first call takes effect.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        TwitchService.OnLoveConfession += HandleLoveConfession;
        TwitchService.OnSayMessage += HandleSayMessage;

        GD.Print("[TwitchIntegration] Initialized");
    }

    /// <summary>
    /// Clean up subscriptions (e.g., when the game ends).
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;

        TwitchService.OnLoveConfession -= HandleLoveConfession;
        TwitchService.OnSayMessage -= HandleSayMessage;

        _nameOverrides.Clear();
        _chatterToEntity.Clear();

        GD.Print("[TwitchIntegration] Shutdown");
    }

    // ---- Cow Naming ----

    /// <summary>
    /// Try to assign a Twitch chatter name to the given cow entity.
    /// Returns the assigned name, or null if no chatter name was available.
    /// Call this from CowView.OnSpawned.
    /// </summary>
    public static string TryAssignChatterName(Entity entity)
    {
        if (!TwitchSettings.IsConnected) return null;

        // Don't re-assign if this entity already has an override
        if (_nameOverrides.ContainsKey(entity.Id)) return _nameOverrides[entity.Id];

        var chatterName = TwitchService.GetNextChatterName();
        if (chatterName == null) return null;

        _nameOverrides[entity.Id] = chatterName;
        _chatterToEntity[chatterName.ToLowerInvariant()] = entity.Id;

        GD.Print($"[TwitchIntegration] Cow {entity.Id} named after chatter: {chatterName}");
        return chatterName;
    }

    /// <summary>
    /// Get the display name for a cow entity. Returns the Twitch override if one exists,
    /// otherwise falls back to the NameComponent from state.
    /// </summary>
    public static string GetDisplayName(Entity entity)
    {
        if (_nameOverrides.TryGetValue(entity.Id, out var overrideName))
            return overrideName;

        var state = ReactiveSystem.Instance.BoundState;
        if (state != null && state.HasComponent<NameComponent>(entity))
            return state.GetComponent<NameComponent>(entity).Name.ToString();

        return $"Cow #{entity.Id}";
    }

    /// <summary>
    /// Check if a cow entity has a Twitch chatter name override.
    /// </summary>
    public static bool HasNameOverride(int entityId)
    {
        return _nameOverrides.ContainsKey(entityId);
    }

    /// <summary>
    /// Remove the name override for an entity (e.g., when a cow is removed).
    /// </summary>
    public static void RemoveNameOverride(int entityId)
    {
        if (_nameOverrides.TryGetValue(entityId, out var name))
        {
            _nameOverrides.Remove(entityId);
            _chatterToEntity.Remove(name.ToLowerInvariant());
        }
    }

    // ---- Cow Lookup ----

    /// <summary>
    /// Find a cow entity ID by chatter username. Returns -1 if not found.
    /// </summary>
    private static int FindCowByChatter(string username)
    {
        if (string.IsNullOrEmpty(username)) return -1;
        var key = username.ToLowerInvariant();

        // First check our override map
        if (_chatterToEntity.TryGetValue(key, out var entityId))
            return entityId;

        // Fallback: search NameComponent values in state
        var state = ReactiveSystem.Instance.BoundState;
        if (state == null) return -1;

        foreach (var entity in state.Filter<CowComponent>())
        {
            if (!state.HasComponent<NameComponent>(entity)) continue;
            var name = state.GetComponent<NameComponent>(entity).Name.ToString();
            if (name.Equals(username, System.StringComparison.OrdinalIgnoreCase))
                return entity.Id;
        }

        return -1;
    }

    // ---- Love Confession ----

    private static void HandleLoveConfession(string username)
    {
        var entityId = FindCowByChatter(username);
        if (entityId < 0)
        {
            GD.Print($"[TwitchIntegration] Love confession from {username} but no cow found with that name");
            ShowFloatingMessage($"{username}'s love confession! (No cow found)");
            return;
        }

        var entity = new Entity(entityId);

        // Show a heart above the cow
        if (EntityViewModel.EntityVisualNodes.TryGetValue(entityId, out var visualNode)
            && Node.IsInstanceValid(visualNode))
        {
            ShowLoveEffect(visualNode, username);
        }

        // Show the love popup overlay
        var tree = ((SceneTree)Engine.GetMainLoop());
        LoveConfessionPopup.Show(tree, username, GetDisplayName(entity));

        GD.Print($"[TwitchIntegration] Love confession: {username}'s cow ({entityId}) received love!");
    }

    private static void ShowLoveEffect(Node3D visualNode, string username)
    {
        // Create a temporary floating heart with the username
        var heartLabel = new Label3D();
        heartLabel.Text = "<3";
        heartLabel.FontSize = 128;
        heartLabel.Modulate = new Color(1f, 0.3f, 0.5f, 1f);
        heartLabel.OutlineModulate = new Color(0.5f, 0f, 0.2f, 1f);
        heartLabel.OutlineSize = 6;
        heartLabel.Position = new Vector3(0, 3.5f, 0);
        heartLabel.NoDepthTest = true;
        heartLabel.RenderPriority = 10;
        heartLabel.OutlineRenderPriority = 9;
        heartLabel.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        visualNode.AddChild(heartLabel);

        // Animate upward and fade out over 3 seconds
        var tween = visualNode.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(heartLabel, "position:y", 5.0f, 3.0f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(heartLabel, "modulate:a", 0f, 3.0f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            if (Node.IsInstanceValid(heartLabel)) heartLabel.QueueFree();
        }));
    }

    // ---- Say Something ----

    private static void HandleSayMessage(string username, string message)
    {
        var entityId = FindCowByChatter(username);

        if (entityId >= 0 && EntityViewModel.EntityVisualNodes.TryGetValue(entityId, out var visualNode)
            && Node.IsInstanceValid(visualNode))
        {
            ShowSpeechBubble(visualNode, username, message);
        }
        else
        {
            // No cow found for this chatter -- show as a general floating message
            GD.Print($"[TwitchIntegration] Say message from {username} but no cow found, showing floating message");
            ShowFloatingMessage($"{username}: {message}");
        }
    }

    /// <summary>
    /// Show a speech bubble (Label3D) above a cow with the given message.
    /// The bubble auto-removes after the specified duration.
    /// </summary>
    public static void ShowSpeechBubble(Node3D visualNode, string username, string message, float duration = 6.0f)
    {
        // Remove any existing speech bubble on this node
        var existing = visualNode.GetNodeOrNull<Node3D>("SpeechBubble");
        if (existing != null && Node.IsInstanceValid(existing))
            existing.QueueFree();

        // Container node for the bubble
        var bubbleRoot = new Node3D();
        bubbleRoot.Name = "SpeechBubble";
        bubbleRoot.Position = new Vector3(0, 3.2f, 0);

        // Message text
        var label = new Label3D();
        label.Text = message;
        label.FontSize = 64;
        label.Modulate = Colors.White;
        label.OutlineModulate = new Color(0.1f, 0.1f, 0.1f, 1f);
        label.OutlineSize = 8;
        label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        label.NoDepthTest = true;
        label.RenderPriority = 12;
        label.OutlineRenderPriority = 11;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Bottom;
        // Wrap long messages
        label.Width = 300;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.Position = Vector3.Zero;
        bubbleRoot.AddChild(label);

        // Username label (smaller, above the message)
        var nameLabel = new Label3D();
        nameLabel.Text = username;
        nameLabel.FontSize = 40;
        nameLabel.Modulate = new Color(0.7f, 0.9f, 1f, 0.9f);
        nameLabel.OutlineModulate = new Color(0.1f, 0.1f, 0.2f, 1f);
        nameLabel.OutlineSize = 4;
        nameLabel.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        nameLabel.NoDepthTest = true;
        nameLabel.RenderPriority = 12;
        nameLabel.OutlineRenderPriority = 11;
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nameLabel.Position = new Vector3(0, 0.8f, 0);
        bubbleRoot.AddChild(nameLabel);

        visualNode.AddChild(bubbleRoot);

        // Fade in
        bubbleRoot.Scale = new Vector3(0.5f, 0.5f, 0.5f);
        var showTween = visualNode.CreateTween();
        showTween.TweenProperty(bubbleRoot, "scale", Vector3.One, 0.2f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        // Auto-remove after duration with fade out
        var timer = new Timer();
        timer.WaitTime = duration;
        timer.OneShot = true;
        timer.Autostart = true;
        timer.Timeout += () =>
        {
            if (!Node.IsInstanceValid(bubbleRoot)) return;
            var fadeTween = visualNode.CreateTween();
            fadeTween.TweenProperty(bubbleRoot, "scale", new Vector3(0.5f, 0.5f, 0.5f), 0.3f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
            fadeTween.TweenCallback(Callable.From(() =>
            {
                if (Node.IsInstanceValid(bubbleRoot)) bubbleRoot.QueueFree();
            }));
        };
        bubbleRoot.AddChild(timer);
    }

    // ---- Floating message (no cow target) ----

    private static void ShowFloatingMessage(string message)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        if (tree?.Root == null) return;

        // Create a simple CanvasLayer overlay with the message
        var overlay = new CanvasLayer();
        overlay.Layer = 90;

        var label = new Label();
        label.Text = message;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", 24);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);

        var container = new CenterContainer();
        container.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        container.MouseFilter = Control.MouseFilterEnum.Ignore;
        container.AddChild(label);

        overlay.AddChild(container);
        tree.Root.AddChild(overlay);

        // Fade in, wait, fade out
        container.Modulate = new Color(1, 1, 1, 0);
        var tween = overlay.CreateTween();
        tween.TweenProperty(container, "modulate:a", 1f, 0.3f);
        tween.TweenInterval(4.0f);
        tween.TweenProperty(container, "modulate:a", 0f, 0.5f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (Node.IsInstanceValid(overlay)) overlay.QueueFree();
        }));
    }
}
