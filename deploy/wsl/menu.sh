#!/usr/bin/env bash
# Menu for the tsx stack on the deploy laptop (Ubuntu on WSL). Every everyday task, by number.
#   bash deploy/wsl/menu.sh                  open the menu
#   bash deploy/wsl/menu.sh --install-alias  add "alias tsx=..." to ~/.bashrc, so typing "tsx" opens it
# Wraps deploy/wsl/stack.sh (Docker compose) and tsx-ops (dotnet run). See deploy/README.md.
set -uo pipefail

self=$(readlink -f "$0")
repo=$(git -C "$(dirname "$self")" rev-parse --show-toplevel)
stack="$repo/deploy/wsl/stack.sh"
backups="$HOME/backups"

if [ "${1:-}" = "--install-alias" ]; then
    line="alias tsx='bash $self'"
    if grep -qxF "$line" "$HOME/.bashrc" 2>/dev/null; then
        echo "Already in ~/.bashrc. Type: tsx"
    else
        printf '\n# tsx stack menu (tsx-aggregator deploy/wsl/menu.sh)\n%s\n' "$line" >> "$HOME/.bashrc"
        echo "Added to ~/.bashrc. Open a new Ubuntu window (or run: source ~/.bashrc), then type: tsx"
    fi
    exit 0
fi

# Ctrl+C stops what a menu item is running (e.g. following logs) and comes back here. A handler, not an
# ignore: children get the default action back, so they stop; the menu just carries on.
trap ':' INT

tsx_ops() {
    if ! command -v dotnet >/dev/null 2>&1; then
        echo "dotnet not found. See deploy/README.md section 4.2 (and open a new Ubuntu window afterwards)."
        return 1
    fi
    (cd "$repo/src" && dotnet run --project tsx-ops -- "$@")
}

confirm() {
    local answer
    read -rp "$1 [y/N] " answer
    [ "$answer" = "y" ] || [ "$answer" = "Y" ]
}

pause_for_enter() {
    read -rp "Press Enter for the menu..." _
}

choose_service() {
    local s
    echo "  1 tsx-aggregator   2 tsx-webapi   3 tsx-web   4 postgres"
    read -rp "  Which? [1] " s
    case "${s:-1}" in
        2) echo tsx-webapi ;;
        3) echo tsx-web ;;
        4) echo postgres ;;
        *) echo tsx-aggregator ;;
    esac
}

recent_lines() {
    # $1 = grep pattern, $2 = since (docker logs --since)
    docker logs --since "$2" tsx-aggregator 2>&1 | grep -E "$1" | tail -20 | cut -c1-220
}

address() {
    ip -4 -o addr show scope global 2>/dev/null | awk '{print $4}' | cut -d/ -f1 | grep -v '^172\.' | head -1
}

while :; do
    cat <<EOF

= TSX STACK ==============================================================
   site: http://localhost:5080   phone/wifi: http://$(address || echo '?'):5080

  Look
   1  Status (containers, memory)
   2  Follow a log (Ctrl+C to stop)
   3  Errors in the last 24 hours
   4  Quote fetches in the last 24 hours

  Control
   5  Start everything
   6  Stop the app (database keeps running)
   7  Restart tsx-aggregator
   8  Update to the latest code (git pull, rebuild, restart)

  Scraper and data (tsx-ops)
   9  Service status (running / paused)
  10  Pause scraping
  11  Resume scraping
  12  Event queue
  13  Take a score snapshot
  14  Rebuild all processed reports (dry run first)

  Maintenance
  15  Back up the database
  16  Start-up check (after a reboot)

   q  Quit
EOF
    read -rp "Choice: " choice
    echo
    case "$choice" in
        1) bash "$stack" status; pause_for_enter ;;
        2) svc=$(choose_service); bash "$stack" logs "$svc" ;;
        3) recent_lines '\] (ERR|FTL)' 24h || echo "No errors."; pause_for_enter ;;
        4) recent_lines 'new quotes|old quotes|Not time to fetch' 24h || echo "Nothing in the last 24 hours."; pause_for_enter ;;
        5) bash "$stack" up; pause_for_enter ;;
        6) confirm "Stop tsx-aggregator, tsx-webapi and tsx-web?" && bash "$stack" down; pause_for_enter ;;
        7) docker restart tsx-aggregator && sleep 10 && docker ps --filter name=tsx-aggregator --format '{{.Names}} {{.Status}}'; pause_for_enter ;;
        8) confirm "Pull the latest code, rebuild (several minutes) and restart?" && bash "$stack" update; pause_for_enter ;;
        9) tsx_ops service status; pause_for_enter ;;
        10) tsx_ops service pause RawCollector --execute; pause_for_enter ;;
        11) tsx_ops service resume RawCollector --execute; pause_for_enter ;;
        12) tsx_ops events; pause_for_enter ;;
        13) read -rp "Label (e.g. before-change): " label
            tsx_ops snapshot --source processed --label "${label:-manual}"; pause_for_enter ;;
        14) tsx_ops requeue --all
            echo
            confirm "Queue them all for a rebuild? Back up first (15) if unsure." && tsx_ops requeue --all --execute
            pause_for_enter ;;
        15) mkdir -p "$backups"
            f="$backups/tsx-$(date +%Y%m%d-%H%M).dump"
            docker exec tsx-postgres pg_dump -U postgres -Fc postgres > "$f" && ls -la "$f"
            echo "Copy it off this laptop now and then (e.g. to OneDrive under /mnt/c/Users/...)."
            pause_for_enter ;;
        16) bash "$repo/deploy/wsl/check-startup.sh" tsx-web 5080; pause_for_enter ;;
        q|Q) exit 0 ;;
        *) echo "Unknown choice: $choice" ;;
    esac
done
