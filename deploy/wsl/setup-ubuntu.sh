#!/usr/bin/env bash
# Ubuntu side of the deploy-laptop setup (Deploy Laptop Plan, step 1).
#   1. Turns on systemd in /etc/wsl.conf.
#   2. Installs Docker Engine from Docker's apt repository (not Docker Desktop) and starts it with systemd.
#   3. Starts a throwaway test container on 127.0.0.1:5060 with restart=unless-stopped.
#
# Run inside Ubuntu on the deploy laptop (not on the dev laptop, which uses Docker Desktop):
#   bash deploy/wsl/setup-ubuntu.sh
# Safe to re-run. Uses sudo.
set -euo pipefail

TEST_CONTAINER=startup-test
TEST_PORT=5060

if docker version --format '{{.Server.Platform.Name}}' 2>/dev/null | grep -qi 'desktop'; then
    echo "This machine uses Docker Desktop. This script is for the deploy laptop (Docker Engine inside Ubuntu)." >&2
    exit 2
fi

# 1. systemd
if ! grep -qs '^\s*systemd\s*=\s*true' /etc/wsl.conf; then
    echo "Turning on systemd in /etc/wsl.conf"
    if grep -qs '^\[boot\]' /etc/wsl.conf; then
        sudo sed -i '/^\[boot\]/a systemd=true' /etc/wsl.conf
    else
        printf '[boot]\nsystemd=true\n' | sudo tee -a /etc/wsl.conf >/dev/null
    fi
    echo
    echo "systemd is now on. In Windows PowerShell run:  wsl --shutdown"
    echo "then open Ubuntu again and re-run this script."
    exit 0
fi
if [ "$(ps -p 1 -o comm=)" != "systemd" ]; then
    echo "systemd is set in /etc/wsl.conf but not running yet. In Windows PowerShell run 'wsl --shutdown', then re-run." >&2
    exit 1
fi

# 2. Docker Engine (https://docs.docker.com/engine/install/ubuntu/)
if ! command -v docker >/dev/null 2>&1; then
    echo "Installing Docker Engine"
    sudo apt-get update
    sudo apt-get install -y ca-certificates curl
    sudo install -m 0755 -d /etc/apt/keyrings
    sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    sudo chmod a+r /etc/apt/keyrings/docker.asc
    # shellcheck source=/dev/null
    codename=$(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $codename stable" \
        | sudo tee /etc/apt/sources.list.d/docker.list >/dev/null
    sudo apt-get update
    sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
sudo systemctl enable --now docker.service containerd.service
if ! id -nG "$USER" | grep -qw docker; then
    sudo usermod -aG docker "$USER"
    echo "Added $USER to the docker group (takes effect in new shells)."
fi

# 3. Throwaway test container
if sudo docker ps -a --format '{{.Names}}' | grep -qx "$TEST_CONTAINER"; then
    echo "Test container '$TEST_CONTAINER' already exists"
else
    sudo docker run -d --name "$TEST_CONTAINER" --restart unless-stopped -p "127.0.0.1:$TEST_PORT:80" nginx:alpine >/dev/null
    echo "Started test container '$TEST_CONTAINER' on 127.0.0.1:$TEST_PORT"
fi
curl -fsS -o /dev/null "http://127.0.0.1:$TEST_PORT/" && echo "http://127.0.0.1:$TEST_PORT answers inside Ubuntu."
echo
echo "Done. Next: deploy/README.md section 3 (check from Windows, then the reboot test)."
