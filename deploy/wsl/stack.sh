#!/usr/bin/env bash
# The tsx stack in Docker (Deploy Laptop Plan, D7). Run from anywhere in the repo, inside Ubuntu:
#   bash deploy/wsl/stack.sh <command>
# Commands:
#   build    build the three images one at a time (parallel builds can exceed the deploy laptop's WSL memory)
#   db       start PostgreSQL only (for a restore)
#   up       start everything (PostgreSQL, tsx-aggregator, tsx-webapi, tsx-web); no build
#   down     stop the app containers; PostgreSQL keeps running
#   status   containers, health and memory
#   logs [service]   follow the logs (default tsx-aggregator)
#   update   git pull, build, up (a few minutes of rebuild; the site stays up until the new containers start)
# Settings and secrets: docker-scripts/local.env (see local.env.example).
set -euo pipefail

self=$(readlink -f "$0")
repo=$(git -C "$(dirname "$self")" rev-parse --show-toplevel)
cd "$repo/docker-scripts"
if [ ! -f local.env ]; then
    echo "docker-scripts/local.env is missing. Copy local.env.example to local.env and fill it in." >&2
    exit 2
fi
compose=(docker compose --env-file .env --env-file local.env --profile app)
app_services=(tsx-aggregator tsx-webapi tsx-web)

build() {
    for s in "${app_services[@]}"; do
        echo "== building $s"
        "${compose[@]}" build "$s"
    done
}

case "${1:-}" in
    build) build ;;
    db) "${compose[@]}" up -d postgres ;;
    up) "${compose[@]}" up -d --no-build tsx-web ;;
    down) "${compose[@]}" stop tsx-web tsx-webapi tsx-aggregator ;;
    status)
        "${compose[@]}" ps
        echo
        docker stats --no-stream --format 'table {{.Name}}\t{{.MemUsage}}\t{{.CPUPerc}}'
        ;;
    logs) "${compose[@]}" logs -f --tail 100 "${2:-tsx-aggregator}" ;;
    update)
        git -C "$repo" pull --ff-only
        build
        "${compose[@]}" up -d --no-build tsx-web
        ;;
    *)
        sed -n '2,13p' "$self" | sed 's/^# \{0,1\}//'
        exit 2
        ;;
esac
