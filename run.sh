#!/usr/bin/env bash
# Linux/WSL counterpart of run.cmd. Launches the aggregator and the web API
# in detached terminals. Run from the repo root.

set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TSX_DIR="$REPO_DIR/src/tsx-aggregator"
WEB_DIR="$REPO_DIR/src/stock-market-webapi"

# Default ports. Override by exporting before running:
#   WEBAPI_PORT=5060 GRPC_PORT=7002 ./run.sh
# 5050 was chosen because port 5000 on this machine is taken by an unrelated
# StocksScraper webapi. 7001 is the default in tsx-aggregator/appsettings.json
# and the value the webapi's gRPC client hardcodes in Program.cs.
WEBAPI_PORT="${WEBAPI_PORT:-5050}"
GRPC_PORT="${GRPC_PORT:-7001}"

# Patterns that uniquely identify processes belonging to *this* repo's
# tsx-aggregator and webapi runs (so we don't kill unrelated dotnet apps).
TSX_PATTERN="$TSX_DIR/bin/.*/tsx-aggregator(\\.dll)?\$"
WEB_PATTERN="$WEB_DIR/bin/.*/stock-market\\.webapi(\\.dll)?\$"
TSX_CHROME_PATTERN="$TSX_DIR/bin/.*/Chrome/"

port_listener() {
    # Print "PID command" of whatever owns $1, or empty if nothing.
    local port="$1"
    ss -tlnpH "sport = :$port" 2>/dev/null \
        | grep -oE 'pid=[0-9]+' | head -1 | cut -d= -f2 \
        | xargs -r -I{} ps -o pid,cmd= -p {}
}

is_port_free() {
    ! ss -tln "sport = :$1" 2>/dev/null | grep -q ":$1"
}

run_in_new_terminal() {
    local title="$1"; shift
    local cmd="$*"
    if command -v gnome-terminal >/dev/null 2>&1; then
        gnome-terminal --title="$title" -- bash -c "$cmd; exec bash"
    elif command -v xterm >/dev/null 2>&1; then
        xterm -T "$title" -e bash -c "$cmd; exec bash" &
    else
        echo "[$title] No terminal emulator found. Running in background:"
        ( cd / && bash -c "$cmd" ) &
    fi
}

# Kill processes matching $1 (a regex passed to pgrep -f). Sends SIGTERM,
# waits up to 5s, escalates to SIGKILL if anything is still alive.
kill_matching() {
    local label="$1" pattern="$2"
    local pids
    pids=$(pgrep -f -- "$pattern" || true)
    if [[ -z "$pids" ]]; then
        echo "[$label] no matching processes"
        return 0
    fi
    echo "[$label] terminating PIDs: $(echo $pids | tr '\n' ' ')"
    kill $pids 2>/dev/null || true
    for _ in 1 2 3 4 5; do
        sleep 1
        pids=$(pgrep -f -- "$pattern" || true)
        [[ -z "$pids" ]] && break
    done
    if [[ -n "$pids" ]]; then
        echo "[$label] escalating to SIGKILL: $(echo $pids | tr '\n' ' ')"
        kill -9 $pids 2>/dev/null || true
    fi
}

stop_aggregator() {
    kill_matching "aggregator-chrome" "$TSX_CHROME_PATTERN"
    kill_matching "aggregator"        "$TSX_PATTERN"
    find /tmp -maxdepth 1 -name '*.ifa' -type d -user "$(id -un)" -exec rm -rf {} + 2>/dev/null || true
}

stop_webapi() {
    kill_matching "webapi" "$WEB_PATTERN"
}

stop_all() {
    stop_aggregator
    stop_webapi
}

# Aborts if $1 is taken and the holder isn't one of OUR processes (which the
# user can stop via the menu). Returns 0 if free or if the holder is ours.
preflight_port() {
    local port="$1" service="$2"
    if is_port_free "$port"; then
        return 0
    fi
    local holder
    holder="$(port_listener "$port")"
    case "$holder" in
        *"$TSX_DIR/bin/"*|*"$WEB_DIR/bin/"*)
            echo "[$service] port $port is held by an old run from this repo:"
            echo "  $holder"
            echo "  Stop it from the menu (3/4/5) and try again."
            return 1
            ;;
        *)
            echo "[$service] port $port is taken by an UNRELATED process:"
            echo "  ${holder:-(unknown — run as root for details)}"
            echo "  Pick a different port:  WEBAPI_PORT=5060 ./run.sh"
            return 1
            ;;
    esac
}

start_aggregator() {
    preflight_port "$GRPC_PORT" "aggregator(gRPC)" || return 1
    run_in_new_terminal "tsx-aggregator" \
        "cd '$TSX_DIR' && Ports__Grpc=$GRPC_PORT dotnet run"
}

start_webapi() {
    preflight_port "$WEBAPI_PORT" "webapi" || return 1
    # --urls is passed AFTER `--` so dotnet forwards it to the app, where it
    # beats both launchSettings.json and ASPNETCORE_URLS. --no-launch-profile
    # keeps launchSettings from forcing its own URL/env.
    run_in_new_terminal "stock-market-webapi" \
        "cd '$WEB_DIR' && dotnet run --no-launch-profile -- --urls http://localhost:$WEBAPI_PORT"
}

show_status() {
    echo
    echo "=== Status ==="
    echo "  configured WEBAPI_PORT=$WEBAPI_PORT  GRPC_PORT=$GRPC_PORT"
    if pgrep -af -- "$TSX_PATTERN" >/dev/null; then
        pgrep -af -- "$TSX_PATTERN" | sed 's/^/  aggregator: /'
    else
        echo "  aggregator: not running"
    fi
    if pgrep -af -- "$WEB_PATTERN" >/dev/null; then
        pgrep -af -- "$WEB_PATTERN" | sed 's/^/  webapi:     /'
    else
        echo "  webapi:     not running"
    fi
    local chrome_count
    chrome_count=$(pgrep -f -- "$TSX_CHROME_PATTERN" | wc -l)
    echo "  puppeteer chromes: $chrome_count"
    echo
    echo "  port $WEBAPI_PORT (webapi):     $(port_listener "$WEBAPI_PORT" || echo free)"
    echo "  port $GRPC_PORT (aggregator):  $(port_listener "$GRPC_PORT" || echo free)"
}

while :; do
    cat <<EOF

= MENU ===================================================================
   webapi → http://localhost:$WEBAPI_PORT     gRPC → :$GRPC_PORT

  1  Run TSX Scraper
  2  Run Web API
  3  Stop TSX Scraper
  4  Stop Web API
  5  Stop both
  6  Status
  q  Quit

EOF
    read -rp "Make a choice: " choice
    case "$choice" in
        1) start_aggregator ;;
        2) start_webapi ;;
        3) stop_aggregator ;;
        4) stop_webapi ;;
        5) stop_all ;;
        6) show_status ;;
        q|Q|"") exit 0 ;;
        *) echo "Unknown choice: $choice" ;;
    esac
done
