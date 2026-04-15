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

import pickle
import hashlib

CACHE_DIR = Path.home() / ".desync-viewer-cache"

def _cache_key(filepath):
    """Cache key based on path, size and mtime."""
    filepath = Path(filepath)
    st = filepath.stat()
    raw = f"{filepath}:{st.st_size}:{st.st_mtime_ns}"
    return hashlib.md5(raw.encode()).hexdigest()

def _load_cache(filepath):
    """Try to load cached parse result. Returns (header, ticks, snap_index) or None."""
    key = _cache_key(filepath)
    cache_file = CACHE_DIR / f"{key}.pkl"
    if cache_file.exists():
        try:
            with open(cache_file, "rb") as f:
                data = pickle.load(f)
            # Support old 2-tuple caches and new 3-tuple
            if isinstance(data, tuple) and len(data) == 3:
                return data
            elif isinstance(data, tuple) and len(data) == 2:
                return data[0], data[1], {}
        except Exception:
            cache_file.unlink(missing_ok=True)
    return None

def _save_cache(filepath, header, ticks, snap_index):
    """Save parsed result (without snap data) + snap byte-offset index to cache."""
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    key = _cache_key(filepath)
    cache_file = CACHE_DIR / f"{key}.pkl"
    try:
        with open(cache_file, "wb") as f:
            pickle.dump((header, ticks, snap_index), f, protocol=pickle.HIGHEST_PROTOCOL)
    except Exception:
        pass

def parse_jsonl(filepath, include_snaps=False):
    """Parse a JSONL file. Returns (header, ticks_dict).
    ticks_dict: {tick_number: record}. Last-write-wins for dedup (handles rollback).
    Preserves 'snap' from any entry for a tick (resim re-records may drop it).

    When include_snaps=False (default), snap data is stripped before caching
    for much faster subsequent loads (~13GB -> ~few MB).
    Also builds a snap_index: {tick: byte_offset} for fast diff lookups."""
    if not include_snaps:
        cached = _load_cache(filepath)
        if cached:
            return cached[0], cached[1]  # header, ticks (snap_index accessed separately)

    header = None
    ticks = {}
    rejected = []  # list of rejected action entries
    snap_index = {}  # tick -> byte_offset of first line with snap for that tick
    with open(filepath, "rb") as f:
        while True:
            offset = f.tell()
            raw = f.readline()
            if not raw:
                break
            line = raw.decode("utf-8", errors="replace").strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            if obj.get("rejected"):
                rejected.append(obj)
            elif "session" in obj and "side" in obj and "tick" not in obj:
                header = obj
            elif "tick" in obj:
                tick = obj["tick"]
                # Track byte offset of last snap entry per tick
                if obj.get("snap"):
                    snap_index[tick] = offset
                # Preserve snap from earlier entry if new entry lacks it
                prev = ticks.get(tick)
                if prev and prev.get("snap") and not obj.get("snap"):
                    obj["snap"] = prev["snap"]
                ticks[tick] = obj

    if not include_snaps:
        for t in ticks.values():
            t.pop("snap", None)
        _save_cache(filepath, header, ticks, snap_index)

    # Store rejected list on header for easy access
    if header is not None:
        header["_rejected"] = rejected

    return header, ticks


def _load_snap_index(filepath):
    """Load just the snap byte-offset index from cache."""
    cached = _load_cache(filepath)
    if cached:
        return cached[2]
    return None


def load_snap_at_offset(filepath, offset):
    """Seek to a byte offset, read one JSONL line, return its snap field."""
    with open(filepath, "rb") as f:
        f.seek(offset)
        raw = f.readline()
        if not raw:
            return None
        line = raw.decode("utf-8", errors="replace").strip()
        try:
            obj = json.loads(line)
            return obj.get("snap")
        except json.JSONDecodeError:
            return None


def load_snaps_for_tick(filepath, target_tick, search_range=5):
    """Load snap data for a tick (and nearby) using the cached offset index.
    Falls back to full scan if no index exists."""
    idx = _load_snap_index(filepath)
    if idx:
        snaps = {}
        for tick, offset in idx.items():
            if abs(tick - target_tick) <= search_range:
                snap = load_snap_at_offset(filepath, offset)
                if snap:
                    snaps[tick] = snap
        return snaps
    # Fallback: full scan (only on first run before cache exists)
    snaps = {}
    with open(filepath) as f:
        for line in f:
            if '"snap"' not in line:
                continue
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            tick = obj.get("tick")
            if tick is not None and abs(tick - target_tick) <= search_range:
                snap = obj.get("snap")
                if snap:
                    snaps[tick] = snap
    return snaps

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


def analyze_session(session, include_snaps=False):
    """Load and compare a session's server + client logs."""
    server_header, server_ticks = None, {}
    client_header, client_ticks = None, {}

    if session["server"]:
        server_header, server_ticks = parse_jsonl(session["server"], include_snaps=include_snaps)
    if session["client"]:
        client_header, client_ticks = parse_jsonl(session["client"], include_snaps=include_snaps)

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

    # Collect rejected actions from headers
    server_rejected = server_header.get("_rejected", []) if server_header else []
    client_rejected = client_header.get("_rejected", []) if client_header else []

    return {
        "server_header": server_header,
        "client_header": client_header,
        "server_ticks": server_ticks,
        "client_ticks": client_ticks,
        "all_ticks": all_ticks,
        "desyncs": desyncs,
        "tick_count": len(all_ticks),
        "server_rejected": server_rejected,
        "client_rejected": client_rejected,
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
  action-diff <session> [--after N] [--brief]
                                        Compare actions between server and client
  rejected <session>                    Show actions rejected as TooOld by the server

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

    sr = analysis["server_rejected"]
    cr = analysis["client_rejected"]
    if sr or cr:
        print(f"Rejected actions: server={len(sr)}  client={len(cr)}")
    else:
        print(f"Rejected actions: none")

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


def cmd_action_diff(args):
    cfg = load_config()
    sessions = discover_sessions(cfg["local_log_dir"])
    session = resolve_session(sessions, args.session)
    analysis = analyze_session(session)

    min_tick = int(args.after) if args.after else 0

    from collections import defaultdict
    s_by_sig = defaultdict(list)
    c_by_sig = defaultdict(list)

    for tick, rec in analysis["server_ticks"].items():
        if tick < min_tick:
            continue
        resim = rec.get("resim", False)
        for a in rec.get("actions", []):
            sig = (a.get("t", "?"), a.get("e", "?"), a.get("d", "?"))
            s_by_sig[sig].append((tick, resim))

    for tick, rec in analysis["client_ticks"].items():
        if tick < min_tick:
            continue
        for a in rec.get("actions", []):
            sig = (a.get("t", "?"), a.get("e", "?"), a.get("d", "?"))
            c_by_sig[sig].append((tick, False))

    all_sigs = sorted(set(s_by_sig.keys()) | set(c_by_sig.keys()))

    # Filter: ignore resim-only server entries (not real deliveries)
    for sig in list(s_by_sig.keys()):
        entries = s_by_sig[sig]
        non_resim = [(t, r) for t, r in entries if not r]
        s_by_sig[sig] = non_resim if non_resim else entries  # keep resim if it's all we have

    s_total = sum(len(v) for v in s_by_sig.values())
    c_total = sum(len(v) for v in c_by_sig.values())
    print(f"Actions after tick {min_tick}: server={s_total} (excl. resim echoes)  client={c_total}")
    print()

    # Summary table
    print(f"{'ACTION':<28} {'TARGET':<8} {'DATA':<12} {'SERVER':<8} {'CLIENT':<8} {'STATUS'}")
    print("-" * 80)

    missing_on_server = []
    for sig in all_sigs:
        s_ticks = s_by_sig.get(sig, [])
        c_ticks = c_by_sig.get(sig, [])
        sc = len(s_ticks)
        cc = len(c_ticks)

        if sc == 0 and cc > 0:
            status = "NEVER DELIVERED"
            missing_on_server.append((sig, s_ticks, c_ticks))
        elif sc == cc:
            status = "OK"
        elif cc == 0:
            status = "SERVER ONLY"
        else:
            status = f"delivered"

        print(f"{sig[0]:<28} {sig[1]:<8} {sig[2]:<12} {sc:<8} {cc:<8} {status}")

    if args.brief:
        return

    # Detail for never-delivered actions
    if missing_on_server:
        print(f"\n{'='*80}")
        print(f"NEVER DELIVERED — client predicted these but server never received them:")
        print(f"{'='*80}")
        for sig, s_ticks, c_ticks in missing_on_server:
            c_tick_list = sorted(t for t, _ in c_ticks)
            print(f"\n  {sig[0]} target={sig[1]} data={sig[2]}")
            print(f"    Client ticks ({len(c_tick_list)}): {_fmt_ticks(c_tick_list)}")
    else:
        print(f"\nNo undelivered actions — server received every action type the client predicted.")


def _fmt_ticks(ticks, max_show=12):
    if len(ticks) <= max_show:
        return ", ".join(str(t) for t in ticks)
    shown = ", ".join(str(t) for t in ticks[:max_show])
    return f"{shown} ...+{len(ticks) - max_show} more"


def cmd_rejected(args):
    cfg = load_config()
    sessions = discover_sessions(cfg["local_log_dir"])
    session = resolve_session(sessions, args.session)
    analysis = analyze_session(session)

    sr = analysis["server_rejected"]
    cr = analysis["client_rejected"]

    if not sr and not cr:
        print("No rejected actions in this session.")
        print("(Rejected action recording requires updated DesyncRecorder with OnActionRejected support)")
        return

    all_rejected = []
    for r in sr:
        all_rejected.append({**r, "side": "server"})
    for r in cr:
        all_rejected.append({**r, "side": "client"})

    all_rejected.sort(key=lambda r: r.get("executeTick", 0))

    print(f"Rejected actions: server={len(sr)}  client={len(cr)}")
    print()
    print(f"{'EXEC TICK':<12} {'MIN ALLOWED':<14} {'LATE BY':<10} {'SIDE':<8} {'ACTION':<28} {'TARGET':<8} {'DATA'}")
    print("-" * 96)

    for r in all_rejected:
        exec_tick = r.get("executeTick", "?")
        min_allowed = r.get("minAllowed", "?")
        late_by = ""
        if isinstance(exec_tick, (int, float)) and isinstance(min_allowed, (int, float)):
            late_by = str(int(min_allowed - exec_tick))
        side = r.get("side", "?")
        action = r.get("action", "?")
        target = r.get("target", "?")
        data = r.get("data", "?")
        print(f"{exec_tick:<12} {min_allowed:<14} {late_by:<10} {side:<8} {action:<28} {target:<8} {data}")


# ── Snapshot Diff ─────────────────────────────────────────────────────────────

def parse_snapshot(raw_bytes):
    """Minimal parse of the ECS serialized state.
    Returns (nextEntityId, entityCapacity, entities, local_to_guid) where entities is a dict
    of {entityIndex: {guid_hex: component_bytes}} keyed by stable guid (not local id)."""
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

    # Component ID mappings: stable_guid -> local_id
    map_count = r_int()
    local_to_guid = {}  # local_id -> guid_hex
    for _ in range(map_count):
        guid_bytes = r_bytes(16)
        local_id = r_int()
        guid_hex = guid_bytes.hex()
        local_to_guid[local_id] = guid_hex

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

    # Components (stored by local_id in the binary)
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

    # Build per-entity component map keyed by GUID (not local id)
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
                    guid = local_to_guid.get(local_id, f"local_{local_id}")
                    ent_comps[guid] = data[offset:offset + elem_size]
        if ent_comps:
            entities[eidx] = ent_comps

    return next_eid, entity_cap, entities, local_to_guid


# Known StableId -> friendly name mapping (from [StableId] attributes in C# code)
# Generated from: grep -rn 'StableId("' --include="*.cs"
KNOWN_TYPES = {
    # Framework
    "00000000000000000000000072b3622d": "World",
    "aafea77a230040ef9ffc071170bb9a26": "Transform2D",
    "1f3b778829474d669d331275d2757278": "CharacterBody2D",
    "56221147380744998c88e92544a47833": "StaticBody2D",
    "c90757757634e5a0a006037000784226": "CollisionShape2D",
    "42ac8ac353d640ca9f7e5daee2548ba6": "Area2D",
    "8014592a33754c07ae29598114227003": "RigidBody2D",
    "ea5b4d6c7f894012d3456a7b8c9d0e1f": "NavigationWorld2D",
    "c8f3b2d45e674a90b1234f5a6b7c8d9e": "NavigationAgent2D",
    "b7e2a1c34d564f89a0123e4f5a6b7c8d": "NavigationRegion2D",
    "d9a4c3e56f784b01c2345a6b7c8d9e0f": "NavigationObstacle2D",
    "f7a23b1c9d454e67b8c23a1f5d7e9b04": "NavMeshConstraint",
    "fb4fe7e4224448ea9ec50a972a3861d4": "PhysicsWorldState",
    "6b42507d9d0041389275b15a8b046aa07": "SceneTag",
    "8888888888888888888888888888888888": "TransientEntity",
    # Game components
    "fcf83639f988e35a8fcc1f0ebc71fb9e": "CowComponent",
    "f3a7b2c19d4e4f5a8b6c1e2d3f4a5b6c": "SkinSpawnCounts",
    "a1b2c3d4e5f64a5b8c7d9e0f1a2b3c4d": "SkinComponent",
    "b7a3e9d2f5c14a8b9d6e3c2f1a0b4e5d": "NameComponent",
    "7677ab1404a9965c960f2d4db849f226": "PlayerEntity",
    "870d8ebdfec06a51ae7fa00f2712a1ce": "PlayerState",
    "a1b2c3d4e5f64a7b8c9d0e1f2a3b4c5d": "GlobalResources",
    "a3b4c5d6e7f84a9b0c1d2e3f4a5b6c7d": "StateComponent",
    "b4c5d6e7f8a94b0c1d2e3f4a5b6c7d8e": "EnterState",
    "c5d6e7f8a9b04c1d2e3f4a5b6c7d8e9f": "ExitState",
    "b7e3a1d94f2c48e69a1b5d3c7f8e2a0b": "MetricsComponent",
    "cb92c8de61faf358acef82ab01ca21fe": "HouseComponent",
    "a3b4c5d6e7f812345678a9abcdef01234": "LoveHouseComponent",
    "432c54c2ff739454b2ad483a305898ca": "GrassComponent",
    "d1e2f3a4b5c64d7e8f9a0b1c2d3e4f5a": "HelperComponent",
    "a7b3c9d2e4f54a6b8c1d9e0f2a3b4c5d": "HelperPetComponent",
    "c7d8e9f0a1b24c3d8e5f6a7b8c9d0e1f": "PropComponent",
    "4910d42fad667d59bd9ac6389ab8c2dc": "WallComponent",
    "861f67429fbc055eb43db0f04d1b057f": "LandComponent",
    "c3d4e5f6a7b84c9d0e1f2a3b4c5d6e7f": "FoodSignComponent",
    "8cc5f1c69a7c09558319b05b750929366": "SellPointComponent",
    "ba8aface147fca54be3eb1547f32b73e": "FinalStructure",
    "f3e2d1c0b9a84f7e9d6c5b4a3c2d1e0f": "BreedBornComponent",
    "fc48881c3e294bbb8585751a65979f96": "HiddenComponent",
    "131a66f508d7408f9d5b90a7e8b9b880": "RejectedComponent",
    "7babed6d282d429889cd9f45bcf8a756": "InteractedComponent",
    "a1e7c3b25f4d4a8e9b2c6d3f1e8a7b5c": "InteractHighlight",
    "5d2c8e1a4b9f4f8a9c7d2e3f1a0b5c6d": "CoinComponent",
    "8f1d3c2b5a6e4d9f8b7c1e2a3f4b5c6d": "ScoreComponent",
    "a2f3b4c5d6e74f8a9b0c1d2e3f4a5b6c": "FoodFarmComponent",
    "cf00123456789abcdef0aabbccddeef1": "CarrotFarm",
    "cf00123456789abcdef0aabbccddeef2": "AppleOrchard",
    "cf00123456789abcdef0aabbccddeef3": "MushroomCave",
    "cf00123456789abcdef0aabbccddf001": "Warehouse",
    "cf00123456789abcdef0aabbccddf002": "WarehouseSign",
    "cf00123456789abcdef0aabbccddeef9": "Decoration",
}


def guid_bytes_to_string(guid_hex):
    """Convert raw guid bytes (from C# Guid.ToByteArray mixed-endian) to standard guid string.
    C# Guid.ToByteArray uses: first 4 bytes LE, next 2 bytes LE, next 2 bytes LE, rest BE."""
    b = bytes.fromhex(guid_hex)
    if len(b) != 16:
        return guid_hex
    # Reorder to standard form
    parts = [
        b[3::-1].hex(),    # first 4 bytes reversed
        b[5:3:-1].hex(),   # next 2 bytes reversed
        b[7:5:-1].hex(),   # next 2 bytes reversed
        b[8:10].hex(),     # 2 bytes as-is
        b[10:16].hex(),    # 6 bytes as-is
    ]
    return "-".join(parts)


def guid_to_name(guid_hex):
    """Convert a guid hex string to a friendly name if known, otherwise short guid."""
    # Try direct match (already normalized)
    clean = guid_hex.replace("-", "").lower()
    if clean in KNOWN_TYPES:
        return KNOWN_TYPES[clean]

    # Try converting from C# mixed-endian byte order to standard string form
    std = guid_bytes_to_string(clean).replace("-", "")
    if std in KNOWN_TYPES:
        return KNOWN_TYPES[std]

    # Return last 8 chars as short identifier
    return clean[-8:]


def diff_snapshots(server_snap_b64, client_snap_b64):
    """Diff two base64-encoded snapshots. Returns a list of diff entries."""
    import base64
    s_bytes = base64.b64decode(server_snap_b64)
    c_bytes = base64.b64decode(client_snap_b64)

    s_eid, _, s_ents, s_names = parse_snapshot(s_bytes)
    c_eid, _, c_ents, c_names = parse_snapshot(c_bytes)

    diffs = []
    all_entity_ids = sorted(set(s_ents.keys()) | set(c_ents.keys()))

    for eidx in all_entity_ids:
        s_has = eidx in s_ents
        c_has = eidx in c_ents

        if s_has and not c_has:
            comp_names = [guid_to_name(g) for g in s_ents[eidx]]
            diffs.append(f"  Entity {eidx}: SERVER ONLY  components: [{', '.join(comp_names)}]")
            continue
        if c_has and not s_has:
            comp_names = [guid_to_name(g) for g in c_ents[eidx]]
            diffs.append(f"  Entity {eidx}: CLIENT ONLY  components: [{', '.join(comp_names)}]")
            continue

        # Both have entity -- compare components by stable guid
        s_comps = s_ents[eidx]
        c_comps = c_ents[eidx]
        all_guids = sorted(set(s_comps.keys()) | set(c_comps.keys()))

        for guid in all_guids:
            name = guid_to_name(guid)
            s_data = s_comps.get(guid)
            c_data = c_comps.get(guid)

            if s_data is None:
                diffs.append(f"  Entity {eidx}.{name}: SERVER MISSING")
                continue
            if c_data is None:
                diffs.append(f"  Entity {eidx}.{name}: CLIENT MISSING")
                continue

            if s_data != c_data:
                byte_diffs = []
                for b in range(min(len(s_data), len(c_data))):
                    if s_data[b] != c_data[b]:
                        byte_diffs.append(f"byte[{b}]:S=0x{s_data[b]:02X}/C=0x{c_data[b]:02X}")
                # Size difference
                if len(s_data) != len(c_data):
                    byte_diffs.append(f"SIZE:S={len(s_data)}/C={len(c_data)}")
                diff_count = len(byte_diffs)
                shown = byte_diffs[:8]
                trailer = f" ...+{diff_count - 8} more" if diff_count > 8 else ""
                diffs.append(f"  Entity {eidx}.{name}: {diff_count} byte(s) differ  {' '.join(shown)}{trailer}")

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

    # Targeted snap loading — seek directly via cached byte-offset index
    s_snaps = load_snaps_for_tick(session["server"], tick) if session["server"] else {}
    c_snaps = load_snaps_for_tick(session["client"], tick) if session["client"] else {}

    s_snap = s_snaps.get(tick)
    c_snap = c_snaps.get(tick)

    if not s_snap and not c_snap:
        # Try nearby ticks that have snapshots
        for dt in range(-5, 6):
            t2 = tick + dt
            if not s_snap and t2 in s_snaps:
                s_snap = s_snaps[t2]
                print(f"(using server snapshot from tick {t2})")
            if not c_snap and t2 in c_snaps:
                c_snap = c_snaps[t2]
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

    p_rejected = sub.add_parser("rejected")
    p_rejected.add_argument("session")

    p_action_diff = sub.add_parser("action-diff")
    p_action_diff.add_argument("session")
    p_action_diff.add_argument("--after", default="0", help="Only show actions after this tick")
    p_action_diff.add_argument("--brief", action="store_true", help="Summary table only, no detail")

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
    elif args.command == "action-diff":
        cmd_action_diff(args)
    elif args.command == "rejected":
        cmd_rejected(args)
    else:
        cmd_help(args)


if __name__ == "__main__":
    main()
