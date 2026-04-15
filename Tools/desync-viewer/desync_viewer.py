#!/usr/bin/env python3
"""
Desync Viewer — offline analyzer for deterministic netcode desync logs.

Reads JSONL traces recorded by DesyncRecorder (C#) on both server and client,
pairs them by session ID, and shows per-tick diffs with action comparison.

Usage:
    desync-viewer help
    desync-viewer pull
    desync-viewer list
    desync-viewer summary <session|latest|latest-desync>
    desync-viewer desyncs <session|latest|latest-desync>
    desync-viewer tick <session> <tick> [--context N]
    desync-viewer actions <session> --tick-range N-M
"""

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

# ── Config ────────────────────────────────────────────────────────────────────

CONFIG_PATH = Path.home() / ".desync-viewer.json"
DEFAULT_LOCAL_DIR = Path.home() / "PizzaJam_DesyncLogs"

def load_config():
    defaults = {
        "vps_host": "193.168.49.169",
        "vps_user": "root",
        "server_log_path": "/app/desync-logs",
        "local_log_dir": str(DEFAULT_LOCAL_DIR),
    }
    if CONFIG_PATH.exists():
        try:
            with open(CONFIG_PATH) as f:
                cfg = json.load(f)
            defaults.update(cfg)
        except Exception:
            pass
    return defaults

# ── JSONL Parsing ─────────────────────────────────────────────────────────────

def parse_jsonl(filepath):
    """Parse a JSONL file. Returns (header, ticks_dict).
    ticks_dict: {tick_number: record}. Last-write-wins for dedup (handles rollback)."""
    header = None
    ticks = {}
    with open(filepath) as f:
        for line_num, line in enumerate(f):
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue  # skip partial lines (crash-safe)
            if "session" in obj and "side" in obj and "tick" not in obj:
                header = obj
            elif "tick" in obj:
                ticks[obj["tick"]] = obj
    return header, ticks

# ── Session Discovery ─────────────────────────────────────────────────────────

def discover_sessions(local_dir):
    """Scan local_dir for server_*.jsonl and client_*.jsonl, pair by session ID."""
    local_dir = Path(local_dir)
    if not local_dir.exists():
        return []

    files = {}  # session_id -> {"server": path, "client": path}
    for f in sorted(local_dir.glob("*.jsonl")):
        name = f.stem  # e.g. "server_a3f2b1c4-..." or "client_a3f2b1c4-..."
        if name.startswith("server_"):
            sid = name[len("server_"):]
            files.setdefault(sid, {})["server"] = f
        elif name.startswith("client_"):
            sid = name[len("client_"):]
            files.setdefault(sid, {})["client"] = f

    sessions = []
    for sid, paths in files.items():
        server_path = paths.get("server")
        client_path = paths.get("client")
        mtime = 0
        if server_path:
            mtime = max(mtime, server_path.stat().st_mtime)
        if client_path:
            mtime = max(mtime, client_path.stat().st_mtime)
        sessions.append({
            "id": sid,
            "server": server_path,
            "client": client_path,
            "mtime": mtime,
        })

    sessions.sort(key=lambda s: s["mtime"], reverse=True)
    return sessions


def analyze_session(session):
    """Load and compare a session's server + client logs."""
    server_header, server_ticks = None, {}
    client_header, client_ticks = None, {}

    if session["server"]:
        server_header, server_ticks = parse_jsonl(session["server"])
    if session["client"]:
        client_header, client_ticks = parse_jsonl(session["client"])

    # Find all ticks (final only: last entry per tick from each side)
    all_ticks = sorted(set(server_ticks.keys()) | set(client_ticks.keys()))

    desyncs = []
    for tick in all_ticks:
        s = server_ticks.get(tick)
        c = client_ticks.get(tick)
        if s is None or c is None:
            continue  # only one side has this tick

        hash_match = s.get("hash") == c.get("hash")
        eid_match = s.get("eid") == c.get("eid")

        s_actions = sorted(json.dumps(a, sort_keys=True) for a in s.get("actions", []))
        c_actions = sorted(json.dumps(a, sort_keys=True) for a in c.get("actions", []))
        actions_match = s_actions == c_actions

        if not hash_match:
            desyncs.append({
                "tick": tick,
                "server_hash": s.get("hash", "?"),
                "client_hash": c.get("hash", "?"),
                "server_eid": s.get("eid"),
                "client_eid": c.get("eid"),
                "eid_match": eid_match,
                "actions_match": actions_match,
                "server": s,
                "client": c,
            })

    return {
        "server_header": server_header,
        "client_header": client_header,
        "server_ticks": server_ticks,
        "client_ticks": client_ticks,
        "all_ticks": all_ticks,
        "desyncs": desyncs,
        "tick_count": len(all_ticks),
    }


def resolve_session(sessions, name):
    """Resolve 'latest', 'latest-desync', or a session ID prefix."""
    if not sessions:
        print("No sessions found.", file=sys.stderr)
        sys.exit(1)

    if name == "latest":
        return sessions[0]

    if name == "latest-desync":
        for s in sessions:
            analysis = analyze_session(s)
            if analysis["desyncs"]:
                return s
        print("No sessions with desyncs found.", file=sys.stderr)
        sys.exit(1)

    # Match by prefix
    for s in sessions:
        if s["id"].startswith(name):
            return s

    print(f"Session '{name}' not found.", file=sys.stderr)
    sys.exit(1)

# ── Commands ──────────────────────────────────────────────────────────────────

def cmd_help(_args):
    print("""desync-viewer -- offline analyzer for deterministic netcode desync logs

Commands:
  help                                  Show this help message
  pull                                  Fetch server logs from VPS via rsync
  list                                  List all paired sessions with desync status
  summary <session|latest|latest-desync>
                                        One-screen overview of a session
  desyncs <session|latest|latest-desync>
                                        List all desync ticks with details
  tick <session> <tick> [--context N]   Full detail for one tick (or range)
  diff <session> <tick>                 Per-entity component diff at a tick (needs snapshots)
  actions <session> --tick-range N-M    Action log for tick range, both sides aligned

Session can be:
  latest          Most recent session
  latest-desync   Most recent session that has at least one desync
  <id-prefix>     Session ID prefix (from 'list' output)

Examples:
  desync-viewer pull
  desync-viewer list
  desync-viewer summary latest-desync
  desync-viewer desyncs latest-desync
  desync-viewer tick latest-desync 6300
  desync-viewer tick latest-desync 6300 --context 5
  desync-viewer actions latest-desync --tick-range 6295-6305
""")


def cmd_pull(_args):
    cfg = load_config()
    local_dir = Path(cfg["local_log_dir"]).expanduser()
    local_dir.mkdir(parents=True, exist_ok=True)

    remote = f"{cfg['vps_user']}@{cfg['vps_host']}"
    # Get server logs from Docker volume
    # The volume is mounted at /app/desync-logs inside the container
    # On the host, docker volumes are at /var/lib/docker/volumes/<name>/_data/
    src = f"{remote}:/var/lib/docker/volumes/pizzajam-desync-logs/_data/"

    print(f"Pulling server logs from {src} -> {local_dir}/")
    try:
        subprocess.run(
            ["rsync", "-avz", "--include=server_*.jsonl", "--exclude=*", src, str(local_dir) + "/"],
            check=True,
        )
        print("Done.")
    except FileNotFoundError:
        # rsync not available, try scp
        print("rsync not found, trying scp...")
        subprocess.run(
            ["scp", f"{remote}:/var/lib/docker/volumes/pizzajam-desync-logs/_data/server_*.jsonl", str(local_dir) + "/"],
            check=True,
        )
        print("Done.")
    except subprocess.CalledProcessError as e:
        print(f"Pull failed: {e}", file=sys.stderr)
        sys.exit(1)


def cmd_list(_args):
    cfg = load_config()
    sessions = discover_sessions(cfg["local_log_dir"])

    if not sessions:
        print("No sessions found.")
        return

    print(f"{'SESSION':<40} {'FILES':<12} {'STATUS'}")
    print("-" * 70)

    for s in sessions:
        paired = []
        if s["server"]:
            paired.append("S")
        if s["client"]:
            paired.append("C")
        files_str = "+".join(paired)

        # Quick desync check
        analysis = analyze_session(s)
        n = len(analysis["desyncs"])
        if n == 0:
            status = f"clean ({analysis['tick_count']} ticks)"
        else:
            first = analysis["desyncs"][0]["tick"]
            status = f"{n} desync(s), first at tick {first}"

        sid = s["id"][:36]  # truncate long GUIDs
        print(f"{sid:<40} {files_str:<12} {status}")


def cmd_summary(args):
    cfg = load_config()
    sessions = discover_sessions(cfg["local_log_dir"])
    session = resolve_session(sessions, args.session)
    analysis = analyze_session(session)

    print(f"Session: {session['id']}")
    print(f"Server log: {session['server'] or 'MISSING'}")
    print(f"Client log: {session['client'] or 'MISSING'}")
    print(f"Ticks: {analysis['tick_count']}")
    print(f"Desyncs: {len(analysis['desyncs'])}")

    if analysis["desyncs"]:
        d = analysis["desyncs"][0]
        print(f"\nFirst desync at tick {d['tick']}:")
        print(f"  Server hash: {d['server_hash']}")
        print(f"  Client hash: {d['client_hash']}")
        print(f"  NextEntityId: server={d['server_eid']} client={d['client_eid']}  {'MATCH' if d['eid_match'] else 'DRIFT'}")
        print(f"  Actions: {'MATCH' if d['actions_match'] else 'MISMATCH'}")


def cmd_desyncs(args):
    cfg = load_config()
    sessions = discover_sessions(cfg["local_log_dir"])
    session = resolve_session(sessions, args.session)
    analysis = analyze_session(session)

    if not analysis["desyncs"]:
        print("No desyncs found in this session.")
        return

    print(f"{'TICK':<10} {'EID S/C':<16} {'EID':<8} {'ACTIONS':<10} {'HASH S':<20} {'HASH C':<20}")
    print("-" * 84)

    for d in analysis["desyncs"]:
        eid_str = f"{d['server_eid']}/{d['client_eid']}"
        eid_status = "OK" if d["eid_match"] else "DRIFT"
        act_status = "match" if d["actions_match"] else "MISS"
        sh = str(d["server_hash"])[:16]
        ch = str(d["client_hash"])[:16]
        print(f"{d['tick']:<10} {eid_str:<16} {eid_status:<8} {act_status:<10} {sh:<20} {ch:<20}")


def cmd_tick(args):
    cfg = load_config()
    sessions = discover_sessions(cfg["local_log_dir"])
    session = resolve_session(sessions, args.session)
    analysis = analyze_session(session)

    tick = int(args.tick)
    context = args.context or 0
    tick_min = tick - context
    tick_max = tick + context

    # Table header
    print(f"{'TICK':<8} {'S HASH':<18} {'C HASH':<18} {'MATCH':<6} {'S EID':<7} {'C EID':<7} {'S ACT':<6} {'C ACT':<6}")
    print("-" * 76)

    for t in range(tick_min, tick_max + 1):
        s = analysis["server_ticks"].get(t)
        c = analysis["client_ticks"].get(t)

        if s is None and c is None:
            continue

        s_hash = str(s.get("hash", ""))[:14] if s else "-"
        c_hash = str(c.get("hash", ""))[:14] if c else "-"
        match = "OK" if (s and c and s.get("hash") == c.get("hash")) else "DIFF" if (s and c) else "?"
        s_eid = str(s.get("eid", "-")) if s else "-"
        c_eid = str(c.get("eid", "-")) if c else "-"
        s_act = str(len(s.get("actions", []))) if s else "-"
        c_act = str(len(c.get("actions", []))) if c else "-"

        marker = " <--" if t == tick else ""
        print(f"{t:<8} {s_hash:<18} {c_hash:<18} {match:<6} {s_eid:<7} {c_eid:<7} {s_act:<6} {c_act:<6}{marker}")

    # Detail for the requested tick
    s = analysis["server_ticks"].get(tick)
    c = analysis["client_ticks"].get(tick)

    if s and c:
        print(f"\n--- Tick {tick} detail ---")
        print(f"Server: hash={s.get('hash')} eid={s.get('eid')} resim={s.get('resim')}")
        print(f"Client: hash={c.get('hash')} eid={c.get('eid')} resim={c.get('resim')}")

        if s.get("hash") != c.get("hash"):
            if s.get("eid") != c.get("eid"):
                print(f"\n  NextEntityId DRIFT: server={s.get('eid')} client={c.get('eid')}")

        s_actions = s.get("actions", [])
        c_actions = c.get("actions", [])
        if s_actions or c_actions:
            print(f"\n  Server actions ({len(s_actions)}):")
            for a in s_actions:
                print(f"    {a.get('t', '?')} target={a.get('e', '?')} data={a.get('d', '?')}")
            print(f"  Client actions ({len(c_actions)}):")
            for a in c_actions:
                print(f"    {a.get('t', '?')} target={a.get('e', '?')} data={a.get('d', '?')}")


def cmd_actions(args):
    cfg = load_config()
    sessions = discover_sessions(cfg["local_log_dir"])
    session = resolve_session(sessions, args.session)
    analysis = analyze_session(session)

    if not args.tick_range:
        print("--tick-range N-M is required", file=sys.stderr)
        sys.exit(1)

    parts = args.tick_range.split("-")
    if len(parts) != 2:
        print("tick-range must be N-M format", file=sys.stderr)
        sys.exit(1)

    tick_min, tick_max = int(parts[0]), int(parts[1])

    print(f"{'TICK':<8} {'SIDE':<8} {'ACTION':<24} {'TARGET':<8} {'DATA':<10} {'RESIM'}")
    print("-" * 66)

    for t in range(tick_min, tick_max + 1):
        s = analysis["server_ticks"].get(t)
        c = analysis["client_ticks"].get(t)

        s_actions = s.get("actions", []) if s else []
        c_actions = c.get("actions", []) if c else []

        if not s_actions and not c_actions:
            continue

        for a in s_actions:
            resim = "resim" if (s and s.get("resim")) else ""
            print(f"{t:<8} {'server':<8} {a.get('t','?'):<24} {a.get('e','?'):<8} {a.get('d','?'):<10} {resim}")
        for a in c_actions:
            resim = "resim" if (c and c.get("resim")) else ""
            print(f"{t:<8} {'client':<8} {a.get('t','?'):<24} {a.get('e','?'):<8} {a.get('d','?'):<10} {resim}")


# ── Snapshot Diff ─────────────────────────────────────────────────────────────

def parse_snapshot(raw_bytes):
    """Minimal parse of the ECS serialized state.
    Returns (nextEntityId, entityCapacity, entities) where entities is a dict
    of {entityIndex: {typeLocalId: component_bytes}}."""
    import struct
    import io
    buf = io.BytesIO(raw_bytes)

    def r_int(): return struct.unpack('<i', buf.read(4))[0]
    def r_bytes(n): return buf.read(n)

    next_eid = r_int()
    entity_cap = r_int()

    # External state
    ext_count = r_int()
    for _ in range(ext_count):
        key_len = 0
        # BinaryReader.ReadString: 7-bit encoded length prefix
        shift = 0
        while True:
            b = buf.read(1)[0]
            key_len |= (b & 0x7F) << shift
            shift += 7
            if (b & 0x80) == 0:
                break
        buf.read(key_len)  # key string
        val_len = r_int()
        buf.read(val_len)  # value bytes

    # Component ID mappings
    map_count = r_int()
    id_to_name = {}
    for _ in range(map_count):
        guid_bytes = r_bytes(16)
        local_id = r_int()
        # We don't have the type registry, store guid hex as name
        guid_hex = guid_bytes.hex()
        id_to_name[local_id] = guid_hex

    # Entity masks
    mask_data_len = r_int()
    mask_data = r_bytes(mask_data_len)
    mask_elem_size = 16  # BitMask128 = 2 ulongs
    masks = []
    for i in range(entity_cap):
        offset = i * mask_elem_size
        if offset + mask_elem_size <= len(mask_data):
            masks.append(mask_data[offset:offset + mask_elem_size])
        else:
            masks.append(b'\x00' * mask_elem_size)

    # Components
    comp_count = r_int()
    components = {}  # local_id -> (elem_size, packed_data, elem_count)
    for _ in range(comp_count):
        local_id = r_int()
        data_len = r_int()
        data = r_bytes(data_len)
        elem_count = r_int()
        if elem_count > 0 and len(data) > 0:
            elem_size = len(data) // elem_count
            components[local_id] = (elem_size, data, elem_count)

    # Build per-entity component map
    entities = {}
    for eidx in range(entity_cap):
        mask = masks[eidx]
        if mask == b'\x00' * 16:
            continue
        ent_comps = {}
        for local_id, (elem_size, data, elem_count) in components.items():
            # Check if bit is set in mask
            byte_idx = local_id // 8
            bit_idx = local_id % 8
            if byte_idx < len(mask) and (mask[byte_idx] & (1 << bit_idx)):
                offset = eidx * elem_size
                if offset + elem_size <= len(data):
                    ent_comps[local_id] = data[offset:offset + elem_size]
        if ent_comps:
            entities[eidx] = ent_comps

    return next_eid, entity_cap, entities, id_to_name


def diff_snapshots(server_snap_b64, client_snap_b64):
    """Diff two base64-encoded snapshots. Returns a list of diff entries."""
    import base64
    s_bytes = base64.b64decode(server_snap_b64)
    c_bytes = base64.b64decode(client_snap_b64)

    s_eid, _, s_ents, s_names = parse_snapshot(s_bytes)
    c_eid, _, c_ents, c_names = parse_snapshot(c_bytes)

    # Merge name maps (prefer server names)
    names = {**c_names, **s_names}

    diffs = []
    all_entity_ids = sorted(set(s_ents.keys()) | set(c_ents.keys()))

    for eidx in all_entity_ids:
        s_has = eidx in s_ents
        c_has = eidx in c_ents

        if s_has and not c_has:
            comp_names = [names.get(lid, f"TypeId({lid})") for lid in s_ents[eidx]]
            diffs.append(f"  Entity {eidx}: SERVER ONLY  components: [{', '.join(comp_names)}]")
            continue
        if c_has and not s_has:
            comp_names = [names.get(lid, f"TypeId({lid})") for lid in c_ents[eidx]]
            diffs.append(f"  Entity {eidx}: CLIENT ONLY  components: [{', '.join(comp_names)}]")
            continue

        # Both have entity -- compare components
        s_comps = s_ents[eidx]
        c_comps = c_ents[eidx]
        all_lids = sorted(set(s_comps.keys()) | set(c_comps.keys()))

        for lid in all_lids:
            type_name = names.get(lid, f"TypeId({lid})")
            # Use short name: last 8 chars of guid for readability
            short_name = type_name[-8:] if len(type_name) > 8 else type_name

            s_data = s_comps.get(lid)
            c_data = c_comps.get(lid)

            if s_data is None:
                diffs.append(f"  Entity {eidx}.{short_name}: SERVER MISSING")
                continue
            if c_data is None:
                diffs.append(f"  Entity {eidx}.{short_name}: CLIENT MISSING")
                continue

            if s_data != c_data:
                byte_diffs = []
                for b in range(min(len(s_data), len(c_data))):
                    if s_data[b] != c_data[b]:
                        byte_diffs.append(f"byte[{b}]:S=0x{s_data[b]:02X}/C=0x{c_data[b]:02X}")
                diff_count = len(byte_diffs)
                shown = byte_diffs[:8]
                trailer = f" ...+{diff_count - 8} more" if diff_count > 8 else ""
                diffs.append(f"  Entity {eidx}.{short_name}: {diff_count} byte(s) differ  {' '.join(shown)}{trailer}")

    return s_eid, c_eid, diffs


def cmd_diff(args):
    cfg = load_config()
    sessions = discover_sessions(cfg["local_log_dir"])
    session = resolve_session(sessions, args.session)
    analysis = analyze_session(session)

    tick = int(args.tick)

    s = analysis["server_ticks"].get(tick)
    c = analysis["client_ticks"].get(tick)

    if not s or not c:
        print(f"Tick {tick} not found on both sides.", file=sys.stderr)
        sys.exit(1)

    s_snap = s.get("snap")
    c_snap = c.get("snap")

    if not s_snap and not c_snap:
        # Try nearby ticks that have snapshots
        for dt in range(-5, 6):
            t2 = tick + dt
            s2 = analysis["server_ticks"].get(t2, {})
            c2 = analysis["client_ticks"].get(t2, {})
            if s2.get("snap") or c2.get("snap"):
                if not s_snap and s2.get("snap"):
                    s_snap = s2["snap"]
                    print(f"(using server snapshot from tick {t2})")
                if not c_snap and c2.get("snap"):
                    c_snap = c2["snap"]
                    print(f"(using client snapshot from tick {t2})")

    if not s_snap or not c_snap:
        print(f"No snapshot data available for tick {tick} (snapshots are only recorded when hash changes).", file=sys.stderr)
        print(f"Server has snap: {'yes' if s_snap else 'no'}, Client has snap: {'yes' if c_snap else 'no'}")
        sys.exit(1)

    print(f"--- State diff at tick {tick} ---")
    print(f"Server hash: {s.get('hash')}  eid: {s.get('eid')}")
    print(f"Client hash: {c.get('hash')}  eid: {c.get('eid')}")
    print()

    try:
        s_eid, c_eid, diffs = diff_snapshots(s_snap, c_snap)
        print(f"NextEntityId: server={s_eid} client={c_eid}")
        print(f"Differences: {len(diffs)}")
        print()
        for d in diffs:
            print(d)
    except Exception as e:
        print(f"Failed to parse snapshots: {e}", file=sys.stderr)
        sys.exit(1)


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(prog="desync-viewer", add_help=False)
    sub = parser.add_subparsers(dest="command")

    sub.add_parser("help")
    sub.add_parser("pull")
    sub.add_parser("list")

    p_summary = sub.add_parser("summary")
    p_summary.add_argument("session")

    p_desyncs = sub.add_parser("desyncs")
    p_desyncs.add_argument("session")

    p_tick = sub.add_parser("tick")
    p_tick.add_argument("session")
    p_tick.add_argument("tick")
    p_tick.add_argument("--context", type=int, default=0)

    p_actions = sub.add_parser("actions")
    p_actions.add_argument("session")
    p_actions.add_argument("--tick-range", required=True)

    p_diff = sub.add_parser("diff")
    p_diff.add_argument("session")
    p_diff.add_argument("tick")

    args = parser.parse_args()

    if args.command is None or args.command == "help":
        cmd_help(args)
    elif args.command == "pull":
        cmd_pull(args)
    elif args.command == "list":
        cmd_list(args)
    elif args.command == "summary":
        cmd_summary(args)
    elif args.command == "desyncs":
        cmd_desyncs(args)
    elif args.command == "tick":
        cmd_tick(args)
    elif args.command == "actions":
        cmd_actions(args)
    elif args.command == "diff":
        cmd_diff(args)
    else:
        cmd_help(args)


if __name__ == "__main__":
    main()
