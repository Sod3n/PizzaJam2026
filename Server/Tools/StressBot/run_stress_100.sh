#!/bin/bash
set -e

SERVER_IP="193.168.49.169"
SERVER_PORT=9050
DURATION=60
LOBBY_WAIT=10

PROJECT="/Users/a1/Documents/GitHub/PizzaJam2026-copy/Server/Tools/StressBot/StressBot.csproj"

echo "Building StressBot..."
dotnet build "$PROJECT" -c Release -v q --nologo

BIN=$(find "$(dirname "$PROJECT")/bin/Release" -name "StressBot.dll" -path "*/net8.0/*" | head -1)
if [ -z "$BIN" ]; then
    echo "ERROR: Could not find StressBot.dll"
    exit 1
fi
echo "Using: $BIN"

PIDS=()
LOBBY_COUNT=100

echo "Launching $LOBBY_COUNT lobbies ($(($LOBBY_COUNT * 2)) bots) against $SERVER_IP:$SERVER_PORT..."

for i in $(seq 1 $LOBBY_COUNT); do
    FIFO=$(mktemp -u)
    mkfifo "$FIFO"

    # Bot 1: create lobby, write LOBBY_ID to fifo
    (
        dotnet "$BIN" \
            --server-ip $SERVER_IP --server-port $SERVER_PORT \
            --mode create --bot-id "lobby${i}-bot1" \
            --duration-seconds $DURATION --expected-players 2 \
            --lobby-wait-seconds $LOBBY_WAIT \
            2>/dev/null | tee "$FIFO" | grep -v LOBBY_ID > /dev/null
    ) &
    PIDS+=($!)

    # Bot 2: read lobby ID from fifo, join
    (
        LOBBY_ID=$(grep -m1 "LOBBY_ID=" < "$FIFO" | cut -d= -f2)
        rm -f "$FIFO"
        if [ -z "$LOBBY_ID" ]; then
            echo "[lobby$i-bot2] ERROR: no lobby ID received"
            exit 1
        fi
        dotnet "$BIN" \
            --server-ip $SERVER_IP --server-port $SERVER_PORT \
            --mode join --bot-id "lobby${i}-bot2" \
            --lobby-id "$LOBBY_ID" \
            --duration-seconds $DURATION --expected-players 2 \
            2>/dev/null > /dev/null
    ) &
    PIDS+=($!)

    # Stagger launches slightly
    if (( i % 10 == 0 )); then
        echo "  Launched $i/$LOBBY_COUNT lobbies..."
        sleep 0.5
    else
        sleep 0.1
    fi
done

echo ""
echo "All $LOBBY_COUNT lobbies launched (${#PIDS[@]} processes). Running for ${DURATION}s..."
echo "Press Ctrl+C to stop early."
echo ""

wait "${PIDS[@]}" 2>/dev/null
echo "All bots finished."
