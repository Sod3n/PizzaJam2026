#!/usr/bin/env bash
# Capture dotnet-trace (CPU + GC) + dotnet-counters from the VPS server while a local
# stress test drives load. Copies the resulting .nettrace + .csv files back to ./profile/
# for local analysis.
#
# Usage:
#   ./capture-vps-profile.sh <VPS_USER> <VPS_HOST> [CONTAINER]
#
# Env:
#   VPS_PASSWORD  if set, used with sshpass (matches CI). Otherwise falls back to ssh key.
#   LOBBIES       default 25
#   DURATION      total seconds of stress (default 60)
#   TRACE_DELAY   seconds to wait after stress starts before beginning trace (default 15)
#   TRACE_LEN     seconds to trace (default 30)
set -u

VPS_USER="${1:?VPS_USER required}"
VPS_HOST="${2:?VPS_HOST required}"
CONTAINER="${3:-pizzajam-server}"
LOBBIES="${LOBBIES:-25}"
DURATION="${DURATION:-60}"
TRACE_DELAY="${TRACE_DELAY:-15}"
TRACE_LEN="${TRACE_LEN:-30}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="${SCRIPT_DIR}/profile/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT_DIR"
echo "[capture] output: $OUT_DIR"

# Pick ssh wrapper
if [[ -n "${VPS_PASSWORD:-}" ]]; then
  SSH() { sshpass -p "$VPS_PASSWORD" ssh -o StrictHostKeyChecking=no "$@"; }
  SCP() { sshpass -p "$VPS_PASSWORD" scp -o StrictHostKeyChecking=no "$@"; }
else
  SSH() { ssh "$@"; }
  SCP() { scp "$@"; }
fi

# 1. Kick off local stress test in background
echo "[capture] starting local stress → $VPS_HOST (${LOBBIES} lobbies × 2 × ${DURATION}s)"
"$SCRIPT_DIR/stress-run.sh" \
  --server-ip "$VPS_HOST" --server-port 9050 \
  --lobbies "$LOBBIES" --players-per-lobby 2 \
  --duration "$DURATION" > "$OUT_DIR/stress.log" 2>&1 &
STRESS_PID=$!

# 2. Wait for warmup, then attach trace on VPS
echo "[capture] warmup ${TRACE_DELAY}s..."
sleep "$TRACE_DELAY"

echo "[capture] capturing ${TRACE_LEN}s trace on VPS..."
SSH "$VPS_USER@$VPS_HOST" bash <<REMOTE
  set -u
  PID=\$(docker exec $CONTAINER bash -c 'dotnet-trace ps | grep Template.Server | head -1 | awk "{print \\\$1}"')
  if [[ -z "\$PID" ]]; then echo "server PID not found in container"; exit 1; fi
  echo "[vps] server PID: \$PID"

  # Run trace + counters in parallel (fire-and-forget via & and wait)
  docker exec -d $CONTAINER bash -c "dotnet-trace collect --process-id \$PID --profile dotnet-sampled-thread-time --providers 'Microsoft-Windows-DotNETRuntime:0x1:4' --duration 00:00:${TRACE_LEN} --output /tmp/vps-cpu.nettrace > /tmp/vps-trace.log 2>&1"
  docker exec -d $CONTAINER bash -c "dotnet-counters collect --process-id \$PID --counters System.Runtime --refresh-interval 1 --format csv --output /tmp/vps-gc.csv > /tmp/vps-counters.log 2>&1 & sleep $((TRACE_LEN+2)); pkill -f dotnet-counters || true"

  # Wait for trace to finish (dotnet-trace returns when duration expires)
  sleep $((TRACE_LEN + 5))
  echo "[vps] trace complete"
  docker exec $CONTAINER ls -lh /tmp/vps-cpu.nettrace /tmp/vps-gc.csv 2>&1 || true

  # Copy files out of container to VPS host tmp
  docker cp $CONTAINER:/tmp/vps-cpu.nettrace /tmp/vps-cpu.nettrace
  docker cp $CONTAINER:/tmp/vps-gc.csv /tmp/vps-gc.csv
REMOTE

# 3. Pull files back to local
echo "[capture] copying traces back to $OUT_DIR"
SCP "$VPS_USER@$VPS_HOST:/tmp/vps-cpu.nettrace" "$OUT_DIR/cpu.nettrace"
SCP "$VPS_USER@$VPS_HOST:/tmp/vps-gc.csv" "$OUT_DIR/gc.csv"

# 4. Wait for stress to finish
wait "$STRESS_PID" || true
echo "[capture] stress done"

ls -lh "$OUT_DIR"
echo ""
echo "[capture] next:"
echo "  dotnet-trace convert --format speedscope --output $OUT_DIR/cpu.speedscope.json $OUT_DIR/cpu.nettrace"
