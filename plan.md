# Networking Architecture Refactor: GGPO + Delta Sync

## Goal

Refactor the existing deterministic game framework to support **two switchable networking modes**:

1. **GGPO Mode** (existing): Deterministic lockstep with rollback/resimulation
2. **Delta Mode** (new): Server-authoritative with state delta sync and client prediction

Both modes should share core infrastructure (ECS, transport, serialization) but differ in sync strategy.

---

## Current Architecture Summary

### Current Data Flow (GGPO)

```
Client → Server:  Actions (inputs with tick)
Server → Client:  Actions (broadcast to all)

Both sides:
  - Full deterministic simulation
  - Rollback + resim on late inputs
  - State hash comparison for desync detection
```

---

## Target Architecture

### Shared Infrastructure (Both Modes)

### Mode A: GGPO (Keep Existing)

```
├── Full deterministic simulation on client
├── StateHistory for rollback
├── Resimulation on late inputs
├── Action replay (client ↔ server)
└── State hash verification
```

### Mode B: Delta Sync (New)

```
├── Server: authoritative simulation, generates deltas
├── Client: applies deltas, local prediction for own inputs
├── Delta comparison for correction (no rollback)
├── Smooth visual blending
└── No determinism requirement (optional)
```

---

## Delta Sync Detailed Design

### Server Side

1. **Track dirty components** per tick
2. **Generate delta** by comparing current vs previous state
3. **Broadcast delta** to all clients
4. **Include tick number** for client reconciliation

### Client Side

1. **On local input:**
   - Capture state before
   - Apply input locally (prediction)
   - Compute predicted delta
   - Store: `_predictedDeltas[tick] = delta`
   - Send input to server

2. **On server delta received:**
   - Lookup predicted delta for that tick
   - Compute correction: `correction = serverDelta - predictedDelta`
   - Apply correction to current state
   - Remove from pending predictions
   - Smooth visual blend for position/rotation

3. **Remote entities:**
   - No prediction, just apply server delta
   - Interpolate for smooth rendering

### Correction Formula

```
All values treated as deltas (including discrete states):

correction = serverDelta - predictedDelta
currentState += correction

Game logic self-heals:
  - Death locks position → position drift absorbed
  - Stun ignores input → velocity drift absorbed
```

---

## Constraints

1. **Minimal ECS changes** — EntityWorld core should stay stable
2. **Reactive system compatibility** — Observers must work in both modes
3. **Zero-allocation goal** — Pooled buffers, no per-tick GC
4. **Blittable components** — All components are `unmanaged` structs with `Pack = 1`
5. **Existing serialization** — Full snapshot used for initial sync, reconnect, save/load

---

## Questions to Address

1. Where does mode selection live? (GameSimulation? Separate NetworkManager?)
2. How to handle initial state sync in delta mode? (Full snapshot first?)
3. Should delta mode require determinism for replay/debug features?
4. How to handle entity creation/deletion in deltas?
5. Component-level vs entity-level dirty — worth the complexity?