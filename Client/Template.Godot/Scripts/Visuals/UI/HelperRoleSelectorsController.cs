using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using R3;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Godot.Core;
using Template.Godot.Visuals;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Godot.Visuals;

public partial class HelperRoleSelectorsController : Node
{
    private static readonly PackedScene _characterScene =
        GD.Load<PackedScene>("res://templates/characters/Character.tscn");

    private static readonly Shader _smoothShader =
        GD.Load<Shader>("res://shaders/smooth_character.gdshader");

    private static readonly Dictionary<int, string> RoleIconPaths = new()
    {
        { HelperType.Assistant, "res://sprites/export/icons/Money_/1.png" },
        { HelperType.Gatherer, "res://sprites/export/icons/Carrot_/1.png" },
        { HelperType.Seller, "res://sprites/export/icons/Money_/1.png" },
        { HelperType.Builder, "res://sprites/export/icons/Money_/1.png" },
        { HelperType.Milker, "res://sprites/export/icons/Milky_/1.png" },
    };

    private static readonly Texture2D[] _roleTextureCache = new Texture2D[5];

    private static readonly Color UnassignedTint = new(0.45f, 0.45f, 0.45f, 1f);
    private static readonly Color AssignedTint = Colors.White;

    private readonly struct SlotDef
    {
        public readonly string SelectorName;
        public readonly string Hint;
        public readonly Key Key;
        public SlotDef(string s, string h, Key k) { SelectorName = s; Hint = h; Key = k; }
    }

    private static readonly SlotDef[] _slotDefs =
    {
        new("HelperRoleSelector",  "Z", Key.Z),
        new("HelperRoleSelector2", "X", Key.X),
        new("HelperRoleSelector3", "C", Key.C),
        new("HelperRoleSelector4", "V", Key.V),
    };

    private class Slot
    {
        public Control Root;
        public TextureRect HelperIcon;
        public SubViewportContainer HeadContainer;
        public TextureRect RoleIcon;
        public Control SleepIcon;
        public Key Key;
        public int CurrentHelperId;
        public int CurrentRole;
        public bool? CurrentHasHouse;
        public bool? CurrentSleeping;
    }

    private Slot[] _slots;

    // Reactive state mirrors — populated by component-subscription callbacks so the view
    // never derives game logic itself.
    private readonly SortedSet<int> _helperIds = new();
    private readonly Dictionary<int, (int Type, bool IsSleeping)> _helperData = new();
    private readonly Dictionary<int, IDisposable> _helperSubs = new();
    private readonly Dictionary<int, int> _houseToHelper = new();
    private readonly Dictionary<int, IDisposable> _houseSubs = new();
    private readonly CompositeDisposable _disposables = new();
    private bool _initialized;
    private bool _refreshScheduled;

    public override void _Ready()
    {
        var ui = GetParent();
        _slots = new Slot[_slotDefs.Length];
        for (int i = 0; i < _slotDefs.Length; i++)
        {
            var sel = ui.GetNodeOrNull<Control>($"%{_slotDefs[i].SelectorName}");
            if (sel == null) continue;

            var helperIcon = sel.GetNodeOrNull<TextureRect>("CharacterPreview/PanelContainer/HelperIcon");
            var roleIcon = sel.GetNodeOrNull<TextureRect>("RolePreview/RoleIcon");
            var hint = sel.GetNodeOrNull<Label>("RolePreview/ButtonHint");
            if (hint != null) hint.Text = _slotDefs[i].Hint;
            sel.Visible = false;

            SubViewportContainer head = null;
            if (helperIcon != null && helperIcon.GetParent() is Control parent)
            {
                head = new SubViewportContainer
                {
                    Stretch = true,
                    OffsetLeft = helperIcon.OffsetLeft,
                    OffsetTop = helperIcon.OffsetTop,
                    OffsetRight = helperIcon.OffsetRight,
                    OffsetBottom = helperIcon.OffsetBottom,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                parent.AddChild(head);
                helperIcon.Visible = false;
            }

            var sleepIcon = sel.GetNodeOrNull<Control>("CharacterPreview/PanelContainer/Sleep");
            if (sleepIcon != null) sleepIcon.Visible = false;

            _slots[i] = new Slot
            {
                Root = sel,
                HelperIcon = helperIcon,
                HeadContainer = head,
                RoleIcon = roleIcon,
                SleepIcon = sleepIcon,
                Key = _slotDefs[i].Key,
                CurrentHelperId = 0,
                CurrentRole = -1,
                CurrentHasHouse = null,
                CurrentSleeping = null,
            };
        }
    }

    public override void _Process(double delta)
    {
        if (_initialized) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;
        if (ReactiveSystem.Instance?.BoundState == null) return;
        _initialized = true;
        InitializeReactive();
    }

    private void InitializeReactive()
    {
        var reactive = ReactiveSystem.Instance;
        var state = reactive.BoundState;

        // Helpers — seed with existing, then watch add/remove. Each helper gets a
        // per-component subscription that mirrors Type/IsSleeping into _helperData.
        foreach (var e in state.Filter<HelperComponent>()) AddHelper(e);
        reactive.ObserveAdd<HelperComponent>().Subscribe(AddHelper).AddTo(_disposables);
        reactive.ObserveRemove<HelperComponent>().Subscribe(RemoveHelper).AddTo(_disposables);

        // Houses — watch House.HelperId so the "has-house" tint can react to (un)assignment.
        foreach (var e in state.Filter<HouseComponent>()) AddHouse(e);
        reactive.ObserveAdd<HouseComponent>().Subscribe(AddHouse).AddTo(_disposables);
        reactive.ObserveRemove<HouseComponent>().Subscribe(RemoveHouse).AddTo(_disposables);
    }

    private void AddHelper(Entity e)
    {
        int id = e.Id;
        _helperIds.Add(id);
        var sub = ReactiveSystem.Instance.SubscribeComponent<HelperComponent>(
            ReactiveSystem.Instance.BoundState, e, comp =>
            {
                _helperData[id] = (comp.Type, comp.IsSleeping);
                ScheduleRefresh();
            });
        _helperSubs[id] = sub;
        ScheduleRefresh();
    }

    private void RemoveHelper(Entity e)
    {
        int id = e.Id;
        _helperIds.Remove(id);
        _helperData.Remove(id);
        if (_helperSubs.TryGetValue(id, out var sub))
        {
            sub.Dispose();
            _helperSubs.Remove(id);
        }
        ScheduleRefresh();
    }

    private void AddHouse(Entity e)
    {
        int id = e.Id;
        var sub = ReactiveSystem.Instance.SubscribeComponent<HouseComponent>(
            ReactiveSystem.Instance.BoundState, e, comp =>
            {
                int helperId = (int)comp.HelperId;
                _houseToHelper.TryGetValue(id, out var prev);
                _houseToHelper[id] = helperId;
                if (helperId != prev) ScheduleRefresh();
            });
        _houseSubs[id] = sub;
    }

    private void RemoveHouse(Entity e)
    {
        int id = e.Id;
        _houseToHelper.Remove(id);
        if (_houseSubs.TryGetValue(id, out var sub))
        {
            sub.Dispose();
            _houseSubs.Remove(id);
        }
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (_refreshScheduled) return;
        _refreshScheduled = true;
        Callable.From(() =>
        {
            _refreshScheduled = false;
            RefreshSlots();
        }).CallDeferred();
    }

    private void RefreshSlots()
    {
        if (_slots == null) return;
        var ordered = _helperIds.ToArray();
        var housedHelpers = new HashSet<int>(_houseToHelper.Values.Where(v => v != 0));
        var state = ReactiveSystem.Instance?.BoundState;

        for (int i = 0; i < _slots.Length; i++)
        {
            var s = _slots[i];
            if (s == null || s.Root == null) continue;

            if (i >= ordered.Length)
            {
                if (s.CurrentHelperId != 0)
                {
                    s.CurrentHelperId = 0;
                    s.CurrentRole = -1;
                    s.CurrentHasHouse = null;
                    s.CurrentSleeping = null;
                    s.Root.Visible = false;
                    ClearHead(s.HeadContainer);
                }
                continue;
            }

            int id = ordered[i];
            if (!_helperData.TryGetValue(id, out var data)) continue;
            s.Root.Visible = true;

            if (id != s.CurrentHelperId)
            {
                s.CurrentHelperId = id;
                if (state != null) RenderHelperHead(s.HeadContainer, new Entity(id), state);
            }
            if (data.Type != s.CurrentRole)
            {
                s.CurrentRole = data.Type;
                if (s.RoleIcon != null && RoleIconPaths.TryGetValue(data.Type, out var path))
                    s.RoleIcon.Texture = LoadRoleTexture(data.Type, path);
            }
            bool hasHouse = housedHelpers.Contains(id);
            if (s.CurrentHasHouse != hasHouse)
            {
                s.CurrentHasHouse = hasHouse;
                s.Root.Modulate = hasHouse ? AssignedTint : UnassignedTint;
            }
            if (s.CurrentSleeping != data.IsSleeping)
            {
                s.CurrentSleeping = data.IsSleeping;
                if (s.SleepIcon != null) s.SleepIcon.Visible = data.IsSleeping;
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (BreedResultOverlay.IsActive || FamilyTreeOverlay.IsActive
            || LovePopupOverlay.IsActive || SettingsOverlay.IsActive) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;
        if (_slots == null) return;

        for (int i = 0; i < _slots.Length; i++)
        {
            var s = _slots[i];
            if (s == null) continue;
            if (s.Key == key.Keycode && s.CurrentHelperId != 0)
            {
                SendCycle(s.CurrentHelperId);
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }

    public override void _ExitTree()
    {
        foreach (var sub in _helperSubs.Values) sub.Dispose();
        foreach (var sub in _houseSubs.Values) sub.Dispose();
        _disposables.Dispose();
    }

    private static void SendCycle(int helperId)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        var action = new SetHelperRoleAction
        {
            UserId = gm.OfflineMode ? gm.OfflineUserId : gm.GameClient.PlayerId,
            HelperEntityId = helperId,
        };
        if (gm.OfflineMode)
            gm.ScheduleOfflineAction(action, gm.LocalPlayerId);
        else
            gm.GameClient.Execute(action, gm.LocalPlayerId);
    }

    private static Texture2D LoadRoleTexture(int role, string path)
    {
        if (role < 0 || role >= _roleTextureCache.Length) return GD.Load<Texture2D>(path);
        return _roleTextureCache[role] ??= GD.Load<Texture2D>(path);
    }

    private static void ClearHead(SubViewportContainer container)
    {
        if (container == null) return;
        foreach (var child in container.GetChildren())
            child.QueueFree();
    }

    private static void RenderHelperHead(SubViewportContainer container, Entity helperEntity, EntityWorld state)
    {
        if (container == null || _characterScene == null) return;
        ClearHead(container);

        var viewport = new SubViewport
        {
            OwnWorld3D = true,
            TransparentBg = true,
            HandleInputLocally = false,
            Size = new Vector2I(160, 160),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        container.AddChild(viewport);

        var env = new global::Godot.Environment
        {
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
            AmbientLightEnergy = 2f,
        };
        viewport.AddChild(new WorldEnvironment { Environment = env });

        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 1.4f,
            Transform = new Transform3D(Basis.Identity, new Vector3(0, 2.5f, 3))
                .LookingAt(new Vector3(0, 2.4f, 0), Vector3.Up),
        };
        viewport.AddChild(camera);

        var charNode = _characterScene.Instantiate<Node3D>();
        viewport.AddChild(charNode);

        Callable.From(() =>
        {
            if (!IsInstanceValid(charNode) || !IsInstanceValid(viewport)) return;
            charNode.Call("stop_idle");
            if (state.HasComponent<SkinComponent>(helperEntity))
            {
                var skin = state.GetComponent<SkinComponent>(helperEntity);
                SkinVisualizer.UpdateSkins(charNode, skin.Skins);
                SkinVisualizer.UpdateColors(charNode, skin.Colors);
            }
            StripPixelShaders(charNode);
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }).CallDeferred();
    }

    private static void StripPixelShaders(Node node)
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
            StripPixelShaders(child);
    }
}
