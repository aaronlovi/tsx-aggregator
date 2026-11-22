#!/usr/bin/env bash
# The tsx stack in Docker (Deploy Laptop Plan, D7). Run from anywhere in the repo, inside Ubuntu:
#   bash deploy/wsl/stack.sh <command>
# Commands:
#   build    build the three images one at a time (parallel builds can exceed the deploy laptop's WSL memory)
#   db       start PostgreSQL only (for a restore)
#   up       start everything (PostgreSQL, tsx-aggregator, tsx-webapi, tsx-web); no build; removes a leftover startup-test
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

# The throwaway nginx from setup-ubuntu.sh (and the README's network test) also listens on 5080 and restarts with
# Docker. Left behind, it answers http://localhost:5080 in place of the site, so it goes before the stack starts.
remove_startup_test() {
    if docker container inspect startup-test >/dev/null 2>&1; then
        echo "== removing the leftover startup-test container (it held port 5080)"
        docker rm -f startup-test >/dev/null
        return 0
    fi
    return 1
}

# Starts the stack; if a leftover startup-test was removed, restarts tsx-web so it takes port 5080 on every address.
start_stack() {
    local removed=0
    remove_startup_test && removed=1
    "${compose[@]}" up -d --no-build tsx-web
    if [ "$removed" = 1 ]; then
        "${compose[@]}" restart tsx-web
    fi
}

build() {
    for s in "${app_services[@]}"; do
        echo "== building $s"
        "${compose[@]}" build "$s"
    done
}

case "${1:-}" in
    build) build ;;
    db) "${compose[@]}" up -d postgres ;;
    up) start_stack ;;
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
        start_stack
        ;;
    *)
        sed -n '2,13p' "$self" | sed 's/^# \{0,1\}//'
        exit 2
        ;;
esac
