using System;
using System.Collections.Generic;
using Godot;
using R3;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Template.Godot.Core;
using Template.Shared.Components;
using DTransform2D = Deterministic.GameFramework.TwoD.Transform2D;

namespace Template.Godot.Visuals;

public partial class HornyOffscreenIndicator : Control
{
    private static readonly PackedScene _indicatorScene =
        GD.Load<PackedScene>("res://Scenes/HornyIndicator.tscn");

    private const float EdgeInset = 24f;
    private const float PulseAmplitude = 0.15f;
    private const float PulseSpeed = 6f;

    private sealed class Indicator
    {
        public TextureRect Rect;
        public ShaderMaterial Mat;
    }

    private readonly Dictionary<int, Indicator> _indicators = new();
    private readonly HashSet<int> _alerting = new();
    private readonly Dictionary<int, IDisposable> _subs = new();
    private DisposableBag _bag;
    private float _time;

    public override void _EnterTree()
    {
        ReactiveSystem.Instance.ObserveAdd<CowComponent>()
            .Subscribe(TrackCow).AddTo(ref _bag);
        ReactiveSystem.Instance.ObserveRemove<CowComponent>()
            .Subscribe(UntrackCow).AddTo(ref _bag);

        var state = ReactiveSystem.Instance.BoundState;
        if (state != null)
            foreach (var e in state.Filter<CowComponent>()) TrackCow(e);
    }

    public override void _ExitTree()
    {
        _bag.Dispose();
        foreach (var sub in _subs.Values) sub.Dispose();
        _subs.Clear();
        _alerting.Clear();
        foreach (var ind in _indicators.Values)
            if (IsInstanceValid(ind.Rect)) ind.Rect.QueueFree();
        _indicators.Clear();
    }

    private void TrackCow(Entity entity)
    {
        if (_subs.ContainsKey(entity.Id)) return;
        var reactive = ReactiveSystem.Instance;
        var state = reactive.BoundState;
        if (state == null || !state.HasComponent<CowComponent>(entity)) return;

        int id = entity.Id;
        _subs[id] = reactive.SubscribeComponent<CowComponent>(state, entity, comp =>
        {
            if (comp.IsHornyAlerting) _alerting.Add(id);
            else _alerting.Remove(id);
        });
    }

    private void UntrackCow(Entity entity)
    {
        if (_subs.TryGetValue(entity.Id, out var sub))
        {
            sub.Dispose();
            _subs.Remove(entity.Id);
        }
        _alerting.Remove(entity.Id);
        if (_indicators.TryGetValue(entity.Id, out var ind))
        {
            if (IsInstanceValid(ind.Rect)) ind.Rect.QueueFree();
            _indicators.Remove(entity.Id);
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;

        var gm = GameManager.Instance;
        if (gm == null || !gm.IsGameRunning) { HideAll(); return; }

        var state = ReactiveSystem.Instance.BoundState;
        if (state == null) { HideAll(); return; }

        var cam = GetViewport().GetCamera3D();
        if (cam == null || !IsInstanceValid(cam)) { HideAll(); return; }

        var rect = GetViewportRect();
        var center = rect.Size * 0.5f;
        float pulseScale = 1f + Mathf.Sin(_time * PulseSpeed) * PulseAmplitude;

        var seen = new HashSet<int>();

        foreach (var id in _alerting)
        {
            var entity = new Entity(id);
            if (!state.HasComponent<CowComponent>(entity)) continue;
            if (state.HasComponent<HelperComponent>(entity)) continue;
            if (!state.HasComponent<DTransform2D>(entity)) continue;

            var cow = state.GetComponent<CowComponent>(entity);
            if (cow.MaxHorny <= 0) continue;

            var pos = state.GetComponent<DTransform2D>(entity).Position;
            var worldPos = new Vector3((float)pos.X, 0, (float)pos.Y);

            bool behind = cam.IsPositionBehind(worldPos);
            var screen = cam.UnprojectPosition(worldPos);
            bool offscreen = behind
                || screen.X < 0 || screen.X > rect.Size.X
                || screen.Y < 0 || screen.Y > rect.Size.Y;

            if (!offscreen)
            {
                if (_indicators.TryGetValue(id, out var existing))
                    existing.Rect.Visible = false;
                continue;
            }

            Vector2 target = screen;
            if (behind) target = (center - screen) + center;

            var dir = target - center;
            if (dir.LengthSquared() < 0.0001f) dir = new Vector2(1, 0);

            var clamped = ClampToEdge(center, dir, rect.Size, EdgeInset);

            var ind = GetOrCreate(id);
            ind.Mat.SetShaderParameter("fill", cow.Horny / (float)cow.MaxHorny);
            ind.Rect.Position = clamped - ind.Rect.Size * 0.5f;
            ind.Rect.Scale = new Vector2(pulseScale, pulseScale);
            ind.Rect.Visible = true;
            seen.Add(id);
        }

        foreach (var (id, ind) in _indicators)
            if (!seen.Contains(id)) ind.Rect.Visible = false;
    }

    private Indicator GetOrCreate(int entityId)
    {
        if (_indicators.TryGetValue(entityId, out var existing) && IsInstanceValid(existing.Rect))
            return existing;

        var rectNode = _indicatorScene.Instantiate<TextureRect>();
        var mat = (rectNode.Material as ShaderMaterial)?.Duplicate() as ShaderMaterial;
        if (mat == null)
        {
            GD.PrintErr("[HornyOffscreenIndicator] HornyIndicator.tscn missing ShaderMaterial");
            mat = new ShaderMaterial();
        }
        rectNode.Material = mat;
        AddChild(rectNode);

        var ind = new Indicator { Rect = rectNode, Mat = mat };
        _indicators[entityId] = ind;
        return ind;
    }

    private static Vector2 ClampToEdge(Vector2 center, Vector2 dir, Vector2 size, float inset)
    {
        float halfW = size.X * 0.5f - inset;
        float halfH = size.Y * 0.5f - inset;
        if (halfW <= 0 || halfH <= 0) return center;

        float ax = Mathf.Abs(dir.X);
        float ay = Mathf.Abs(dir.Y);
        float scale = (ax * halfH > ay * halfW)
            ? halfW / Mathf.Max(ax, 0.0001f)
            : halfH / Mathf.Max(ay, 0.0001f);
        return center + dir * scale;
    }

    private void HideAll()
    {
        foreach (var ind in _indicators.Values)
            if (IsInstanceValid(ind.Rect)) ind.Rect.Visible = false;
    }
}
