#!/usr/bin/env bash
# Reboot test for the deploy laptop (Deploy Laptop Plan, step 1).
# After rebooting WITHOUT logging in, wait 5 minutes, log in (e.g. through Chrome Remote Desktop), open Ubuntu
# and run:  bash deploy/wsl/check-startup.sh
# It passes if Ubuntu and the test container started before you opened Ubuntu, i.e. without a login.
set -uo pipefail

TEST_CONTAINER=${1:-startup-test}
TEST_PORT=${2:-5060}

now=$(date +%s)
wsl_up=$(date -d "$(uptime -s)" +%s)
echo "Now:                 $(date '+%F %T')"
echo "Ubuntu (WSL) up since: $(uptime -s)  ($(( (now - wsl_up) / 60 )) min ago)"

started=$(docker inspect -f '{{.State.StartedAt}}' "$TEST_CONTAINER" 2>/dev/null) || {
    echo "FAIL: container '$TEST_CONTAINER' not found or Docker not running"; exit 1; }
started_s=$(date -d "$started" +%s)
echo "Container started:   $(date -d "$started" '+%F %T')  ($(( (now - started_s) / 60 )) min ago)"

if curl -fsS -o /dev/null "http://127.0.0.1:$TEST_PORT/"; then
    echo "http://127.0.0.1:$TEST_PORT answers inside Ubuntu."
else
    echo "FAIL: http://127.0.0.1:$TEST_PORT does not answer"; exit 1
fi

echo
echo "Compare with when you logged in. PASS if both times above are before your login"
echo "(with a 5-minute wait, both should be at least 4-5 min ago). Also open http://localhost:$TEST_PORT"
echo "in a Windows browser: it should show the nginx welcome page."
