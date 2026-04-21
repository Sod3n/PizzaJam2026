#!/usr/bin/env bash
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="${SCRIPT_DIR}/StressBot.csproj"
SERVER_IP="127.0.0.1"
SERVER_PORT="9050"
LOBBIES=1
PLAYERS_PER_LOBBY=2
DURATION=60
STAGGER_MS=100
LATENCY_MS=0
LOBBY_WAIT_SECONDS=5

while [[ $# -gt 0 ]]; do
  case "$1" in
    --server-ip) SERVER_IP="$2"; shift 2;;
    --server-port) SERVER_PORT="$2"; shift 2;;
    --lobbies) LOBBIES="$2"; shift 2;;
    --players-per-lobby) PLAYERS_PER_LOBBY="$2"; shift 2;;
    --duration) DURATION="$2"; shift 2;;
    --stagger-ms) STAGGER_MS="$2"; shift 2;;
    --latency-ms) LATENCY_MS="$2"; shift 2;;
    --lobby-wait-seconds) LOBBY_WAIT_SECONDS="$2"; shift 2;;
    -h|--help)
      echo "Usage: $0 --server-ip IP --lobbies N --players-per-lobby M --duration SEC [--stagger-ms MS] [--latency-ms MS] [--lobby-wait-seconds SEC]"
      exit 0;;
    *) echo "Unknown arg: $1" >&2; exit 2;;
  esac
done

TS="$(date +%Y%m%d-%H%M%S)"
OUT_DIR="${SCRIPT_DIR}/runs/${TS}"
mkdir -p "$OUT_DIR"
SUMMARY_CSV="${OUT_DIR}/summary.csv"
echo "timestamp,bot_id,connected,actions_sent,deltas_received,tick_count,tick_ms_avg,current_tick,last_server_tick,drift" > "$SUMMARY_CSV"

echo "[run] output dir: $OUT_DIR"
echo "[run] building StressBot..."
dotnet build "$PROJ" -c Debug -v quiet >"${OUT_DIR}/build.log" 2>&1 || { cat "${OUT_DIR}/build.log"; exit 1; }

BOT_DLL="${SCRIPT_DIR}/bin/Debug/net8.0/StressBot.dll"
if [[ ! -f "$BOT_DLL" ]]; then echo "Bot DLL not built at $BOT_DLL" >&2; exit 1; fi

PIDS=()
BOT_IDS=()
CSVS=()

STAGGER_SLEEP=$(awk "BEGIN { printf \"%f\", ${STAGGER_MS}/1000 }")

for ((L=1; L<=LOBBIES; L++)); do
  CREATOR_ID="L${L}-c"
  STDOUT_FILE="${OUT_DIR}/${CREATOR_ID}.stdout"
  STDERR_FILE="${OUT_DIR}/${CREATOR_ID}.stderr"
  CSV_FILE="${OUT_DIR}/${CREATOR_ID}.csv"

  echo "[run] spawning creator $CREATOR_ID"
  dotnet "$BOT_DLL" \
    --server-ip "$SERVER_IP" --server-port "$SERVER_PORT" \
    --mode create --bot-id "$CREATOR_ID" \
    --duration-seconds "$DURATION" \
    --stats-csv "$CSV_FILE" \
    --latency-ms "$LATENCY_MS" \
    --expected-players "$PLAYERS_PER_LOBBY" \
    --lobby-wait-seconds "$LOBBY_WAIT_SECONDS" \
    >"$STDOUT_FILE" 2>"$STDERR_FILE" &
  PID=$!
  PIDS+=("$PID")
  BOT_IDS+=("$CREATOR_ID")
  CSVS+=("$CSV_FILE")

  LOBBY_ID=""
  for ((w=0; w<100; w++)); do
    if [[ -f "$STDOUT_FILE" ]]; then
      LOBBY_ID="$(grep -m1 '^LOBBY_ID=' "$STDOUT_FILE" | sed 's/^LOBBY_ID=//' | tr -d '\r\n' || true)"
      [[ -n "$LOBBY_ID" ]] && break
    fi
    if ! kill -0 "$PID" 2>/dev/null; then
      echo "[run] creator $CREATOR_ID died before emitting LOBBY_ID. stderr:" >&2
      tail -20 "$STDERR_FILE" >&2 || true
      exit 3
    fi
    sleep 0.1
  done
  if [[ -z "$LOBBY_ID" ]]; then
    echo "[run] timeout waiting for LOBBY_ID from $CREATOR_ID" >&2
    tail -20 "$STDERR_FILE" >&2 || true
    kill "${PIDS[@]}" 2>/dev/null || true
    exit 3
  fi
  echo "[run] lobby $L id=$LOBBY_ID"

  for ((P=2; P<=PLAYERS_PER_LOBBY; P++)); do
    JOINER_ID="L${L}-j${P}"
    J_OUT="${OUT_DIR}/${JOINER_ID}.stdout"
    J_ERR="${OUT_DIR}/${JOINER_ID}.stderr"
    J_CSV="${OUT_DIR}/${JOINER_ID}.csv"
    echo "[run] spawning joiner $JOINER_ID"
    dotnet "$BOT_DLL" \
      --server-ip "$SERVER_IP" --server-port "$SERVER_PORT" \
      --mode join --lobby-id "$LOBBY_ID" \
      --bot-id "$JOINER_ID" \
      --duration-seconds "$DURATION" \
      --stats-csv "$J_CSV" \
      --latency-ms "$LATENCY_MS" \
      >"$J_OUT" 2>"$J_ERR" &
    PIDS+=("$!")
    BOT_IDS+=("$JOINER_ID")
    CSVS+=("$J_CSV")
    sleep "$STAGGER_SLEEP"
  done
  sleep "$STAGGER_SLEEP"
done

echo "[run] ${#PIDS[@]} bots running. waiting up to $((DURATION + 30))s..."
DEADLINE=$(( $(date +%s) + DURATION + 30 ))
ANY_ALIVE=1
while [[ $(date +%s) -lt $DEADLINE && $ANY_ALIVE -eq 1 ]]; do
  ANY_ALIVE=0
  for PID in "${PIDS[@]}"; do
    if kill -0 "$PID" 2>/dev/null; then ANY_ALIVE=1; break; fi
  done
  [[ $ANY_ALIVE -eq 1 ]] && sleep 1
done

EXIT_SUM=0
for i in "${!PIDS[@]}"; do
  PID="${PIDS[$i]}"
  BID="${BOT_IDS[$i]}"
  if kill -0 "$PID" 2>/dev/null; then
    echo "[run] killing stuck bot $BID (pid $PID)"
    kill "$PID" 2>/dev/null || true
    sleep 0.2
    kill -9 "$PID" 2>/dev/null || true
  fi
  wait "$PID" 2>/dev/null; RC=$?
  echo "[run] bot $BID exit=$RC"
  [[ $RC -ne 0 ]] && EXIT_SUM=$((EXIT_SUM + 1))
done

for CSV in "${CSVS[@]}"; do
  if [[ -f "$CSV" ]]; then tail -n +2 "$CSV" >> "$SUMMARY_CSV" 2>/dev/null || true; fi
done

echo "[run] summary: $SUMMARY_CSV"
echo "[run] rows: $(($(wc -l < "$SUMMARY_CSV") - 1))"
echo "[run] failed bots: $EXIT_SUM / ${#PIDS[@]}"

if [[ $EXIT_SUM -gt 0 ]]; then exit 1; fi
exit 0
