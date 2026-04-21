// ViewSmoother - Phase 4 of networking refactor.
// VIEW-LAYER SMOOTHING ONLY. Never mutates ECS components.
//
// Why this exists:
//   Game state is authoritative and ticks at a fixed rate (60hz). When a server
//   delta arrives and the client reconciles, component values can snap to a new
//   authoritative position. Without smoothing, entity visuals would teleport.
//   ViewSmoother interpolates its own *visual* transform each render frame
//   toward the authoritative ECS value, so the game looks smooth while the
//   underlying simulation stays deterministic.
//
// Philosophy:
//   - Game logic (ECS) stays 100% untouched — deterministic, fixed-point.
//   - The smoother only READS components via GetComponent<T>() — never assigns.
//   - Each render frame (not tick!), we lerp the visual value toward the current
//     ECS value with frame-rate-independent exponential smoothing:
//         visual = Lerp(visual, target, 1 - exp(-dt / tau))
//     where tau is a time constant (~80ms for position, ~120ms for rotation).
//   - Works in BOTH GGPO and Delta sync strategies — smoother has no dependency
//     on which sync mode produced the authoritative value.
//
// Lifecycle:
//   - Register a tracker with Track(...) when the visual node is spawned.
//   - Call Reset(entity) after a FullState apply so the visual snaps to the
//     authoritative value instead of lerping from a stale point.
//   - Dispose the IDisposable returned by Track(...) when the node is freed.
//
// Non-allocation:
//   - Update() reuses an internal pooled list with no per-frame allocations.
//   - Trackers are reference types (small) allocated once at Track() time.

using System;
using System.Collections.Generic;
using Godot;
using DTransform2D = Deterministic.GameFramework.TwoD.Transform2D;
using Deterministic.GameFramework.ECS;

namespace Template.Godot.Visuals;

/// <summary>
/// Per-frame visual smoother. Lerps view-layer transforms toward the
/// authoritative ECS value with frame-rate-independent exponential smoothing.
///
/// Does NOT mutate ECS components — selectors read only, appliers write to
/// Godot nodes (or any view-only target).
/// </summary>
public class ViewSmoother : IDisposable
{
    // ── Tracker interface ─────────────────────────────────────────────
    // A Tracker is the smallest unit of smoothing: reads a value from ECS,
    // lerps its internal "visual" state, and applies the lerped value to
    // a view-layer target each render frame.
    private abstract class Tracker : IDisposable
    {
        public Entity Entity;
        public ViewSmoother Owner;
        public bool Disposed;

        /// <summary>
        /// Called once per render frame. Reads the authoritative target,
        /// lerps visual state, and applies to the view target.
        /// Returns false if the tracker should be removed (e.g. entity destroyed).
        /// </summary>
        public abstract bool Update(float deltaSeconds);

        /// <summary>Snap visual value to the current ECS value (no lerp).</summary>
        public abstract void Reset();

        public virtual void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Owner?.Unregister(this);
        }
    }

    // Generic tracker with selector/applier/lerp trio.
    private sealed class Tracker<TValue> : Tracker
    {
        public Func<EntityWorld, Entity, TValue> Selector;
        public Action<TValue> Applier;
        public Func<TValue, TValue, float, TValue> Lerp;
        public Func<EntityWorld, Entity, bool> Exists;
        public float Tau;
        public TValue Visual;
        public bool Initialized;

        public override bool Update(float deltaSeconds)
        {
            var state = Owner._state;
            if (state == null) return true; // State not yet bound — skip.

            // Entity existence check — if the entity or required component is gone,
            // signal removal. We never mutate ECS here.
            if (Exists != null && !Exists(state, Entity))
            {
                return false;
            }

            TValue target;
            try
            {
                target = Selector(state, Entity);
            }
            catch
            {
                // Entity may have lost the component between frames during delta
                // apply — drop the tracker silently.
                return false;
            }

            if (!Initialized)
            {
                Visual = target;
                Initialized = true;
            }
            else
            {
                // Exponential smoothing: t = 1 - exp(-dt / tau).
                // Large dt (frame drop) saturates toward 1, so visual catches up
                // without overshoot. Zero tau = snap. Huge tau = no movement.
                float t = Tau <= 0f ? 1f : 1f - MathF.Exp(-deltaSeconds / Tau);
                if (t > 1f) t = 1f;
                Visual = Lerp(Visual, target, t);
            }

            try
            {
                Applier(Visual);
            }
            catch
            {
                // Applier failed (e.g. Godot node disposed mid-frame) — drop tracker.
                return false;
            }

            return true;
        }

        public override void Reset()
        {
            var state = Owner._state;
            if (state == null) return;
            try
            {
                if (Exists != null && !Exists(state, Entity)) return;
                Visual = Selector(state, Entity);
                Initialized = true;
                // Apply immediately so the node snaps to the authoritative value.
                Applier(Visual);
            }
            catch
            {
                // Entity not ready — leave uninitialized; next Update() handles it.
            }
        }
    }

    // ── State ─────────────────────────────────────────────────────────

    private readonly EntityWorld _state;
    private readonly List<Tracker> _trackers = new();
    // Per-entity lookup for quick Reset(entity) and bulk dispose.
    private readonly Dictionary<int, List<Tracker>> _byEntity = new();
    // Reused buffer for safe iteration (we may remove during Update).
    private readonly List<Tracker> _updateBuffer = new();
    private bool _disposed;

    public int TrackerCount => _trackers.Count;
    public EntityWorld State => _state;

    public ViewSmoother(EntityWorld state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    // ── Generic tracking ──────────────────────────────────────────────

    /// <summary>
    /// Track a value. Reads `selector(state, entity)` each frame, lerps internal
    /// visual state toward it via `lerp`, and writes the lerped value via `applier`.
    ///
    /// The smoother NEVER mutates the entity world — selector must be read-only.
    /// Dispose the returned handle to stop tracking.
    /// </summary>
    /// <param name="tau">Smoothing time constant in seconds (e.g., 0.08 for position, 0.12 for rotation).</param>
    /// <param name="exists">Optional predicate; return false when the entity / component is gone so the tracker auto-unregisters.</param>
    public IDisposable Track<TValue>(
        Entity entity,
        Func<EntityWorld, Entity, TValue> selector,
        Action<TValue> applier,
        Func<TValue, TValue, float, TValue> lerp,
        float tau = 0.08f,
        Func<EntityWorld, Entity, bool> exists = null)
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        if (applier == null) throw new ArgumentNullException(nameof(applier));
        if (lerp == null) throw new ArgumentNullException(nameof(lerp));

        var tracker = new Tracker<TValue>
        {
            Entity = entity,
            Owner = this,
            Selector = selector,
            Applier = applier,
            Lerp = lerp,
            Tau = tau,
            Exists = exists,
        };
        _trackers.Add(tracker);
        if (!_byEntity.TryGetValue(entity.Id, out var list))
        {
            list = new List<Tracker>(2);
            _byEntity[entity.Id] = list;
        }
        list.Add(tracker);
        return tracker;
    }

    // ── Common shorthands ─────────────────────────────────────────────

    /// <summary>
    /// Track the Transform2D GlobalPosition of an entity, applying to a Godot Node3D.
    /// Z-axis of the Node3D receives the 2D Y (the project uses ground-plane XY).
    /// Y-axis (height) is left untouched so vertical offsets set by views still work.
    /// </summary>
    public IDisposable TrackPosition3D(Entity entity, Node3D node, float tau = 0.08f, float yOffset = 0f)
    {
        return Track<Vector3>(
            entity,
            selector: (s, e) =>
            {
                ref var t = ref s.GetComponent<DTransform2D>(e);
                var p = t.GlobalPosition;
                return new Vector3((float)p.X, yOffset, (float)p.Y);
            },
            applier: v =>
            {
                if (GodotObject.IsInstanceValid(node))
                    node.Position = v;
            },
            lerp: (a, b, t) => a.Lerp(b, t),
            tau: tau,
            exists: (s, e) => s.HasComponent<DTransform2D>(e));
    }

    /// <summary>
    /// Track the Transform2D GlobalPosition as a 2D Godot Vector2 (XY).
    /// Useful for Node2D targets.
    /// </summary>
    public IDisposable TrackPosition2D(Entity entity, Node2D node, float tau = 0.08f)
    {
        return Track<Vector2>(
            entity,
            selector: (s, e) =>
            {
                ref var t = ref s.GetComponent<DTransform2D>(e);
                var p = t.GlobalPosition;
                return new Vector2((float)p.X, (float)p.Y);
            },
            applier: v =>
            {
                if (GodotObject.IsInstanceValid(node))
                    node.Position = v;
            },
            lerp: LerpVector2,
            tau: tau,
            exists: (s, e) => s.HasComponent<DTransform2D>(e));
    }

    /// <summary>Track Transform2D rotation (2D angle) applied to a Node3D's Y-axis rotation.</summary>
    public IDisposable TrackRotationY3D(Entity entity, Node3D node, float tau = 0.12f)
    {
        return Track<float>(
            entity,
            selector: (s, e) =>
            {
                ref var t = ref s.GetComponent<DTransform2D>(e);
                return (float)t.GlobalRotation;
            },
            applier: v =>
            {
                if (GodotObject.IsInstanceValid(node))
                {
                    var r = node.Rotation;
                    node.Rotation = new Vector3(r.X, v, r.Z);
                }
            },
            lerp: LerpAngle,
            tau: tau,
            exists: (s, e) => s.HasComponent<DTransform2D>(e));
    }

    // ── Update loop ───────────────────────────────────────────────────

    /// <summary>
    /// Call from the render loop each frame (e.g. Godot's _Process(double delta))
    /// with elapsed seconds since last frame. Zero per-frame allocations.
    /// </summary>
    public void Update(float deltaSeconds)
    {
        if (_disposed || _trackers.Count == 0) return;

        // Clamp deltaSeconds to avoid huge jumps after a breakpoint / stall.
        // 0.25s cap means one visible frame of catch-up; anything larger likely
        // indicates the game was paused and we don't want to rocket the visual.
        if (deltaSeconds < 0f) deltaSeconds = 0f;
        else if (deltaSeconds > 0.25f) deltaSeconds = 0.25f;

        // Copy into reused buffer to allow Dispose() during iteration.
        _updateBuffer.Clear();
        _updateBuffer.AddRange(_trackers);

        for (int i = 0; i < _updateBuffer.Count; i++)
        {
            var tracker = _updateBuffer[i];
            if (tracker.Disposed) continue;
            bool alive = tracker.Update(deltaSeconds);
            if (!alive) tracker.Dispose();
        }
    }

    /// <summary>Snap all trackers for the given entity to the current ECS value.</summary>
    public void Reset(Entity entity)
    {
        if (_byEntity.TryGetValue(entity.Id, out var list))
        {
            for (int i = 0; i < list.Count; i++)
            {
                var tracker = list[i];
                if (!tracker.Disposed) tracker.Reset();
            }
        }
    }

    /// <summary>Snap ALL trackers to current ECS value. Call after FullState apply.</summary>
    public void ResetAll()
    {
        for (int i = 0; i < _trackers.Count; i++)
        {
            var tracker = _trackers[i];
            if (!tracker.Disposed) tracker.Reset();
        }
    }

    /// <summary>Remove every tracker for the given entity (e.g. on despawn).</summary>
    public void UnregisterEntity(Entity entity)
    {
        if (!_byEntity.TryGetValue(entity.Id, out var list)) return;
        // Copy-iterate because Dispose mutates _byEntity/_trackers.
        for (int i = list.Count - 1; i >= 0; i--)
        {
            list[i].Dispose();
        }
        _byEntity.Remove(entity.Id);
    }

    private void Unregister(Tracker tracker)
    {
        _trackers.Remove(tracker);
        if (_byEntity.TryGetValue(tracker.Entity.Id, out var list))
        {
            list.Remove(tracker);
            if (list.Count == 0) _byEntity.Remove(tracker.Entity.Id);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Snapshot before disposing (Dispose mutates _trackers).
        var snapshot = _trackers.ToArray();
        foreach (var t in snapshot) t.Dispose();
        _trackers.Clear();
        _byEntity.Clear();
    }

    // ── Lerp helpers ──────────────────────────────────────────────────

    public static Vector2 LerpVector2(Vector2 a, Vector2 b, float t)
        => new Vector2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public static Vector3 LerpVector3(Vector3 a, Vector3 b, float t)
        => new Vector3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    public static float LerpFloat(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Lerp between angles in radians, taking the shortest path around the unit circle.
    /// Handles wraparound so going from 179deg to -179deg rotates 2deg, not 358deg.
    /// </summary>
    public static float LerpAngle(float a, float b, float t)
    {
        float diff = b - a;
        // Wrap diff into [-pi, pi]
        while (diff > MathF.PI) diff -= 2f * MathF.PI;
        while (diff < -MathF.PI) diff += 2f * MathF.PI;
        return a + diff * t;
    }
}
