# Deploy Laptop Setup

Scripts and steps for running the stack in Docker inside Ubuntu on WSL on the Windows deploy laptop, reachable on the home wifi. The plan and its decisions are in the vault note "Deploy Laptop Plan". On the deploy laptop the Windows account is `EMILY` and the Ubuntu user `aaron`; the repo is at `~/dev/tsx-aggregator` in Ubuntu.

## Table of Contents

- [1. What Gets Installed](#1-what-gets-installed)
- [2. Start-Up Without a Login (Step 1, Done)](#2-start-up-without-a-login-step-1-done)
  - [2.1. Start-Up Without a Login (Step 1, Done) - Install Ubuntu on WSL](#21-start-up-without-a-login-step-1-done---install-ubuntu-on-wsl)
  - [2.2. Start-Up Without a Login (Step 1, Done) - Docker Engine and the Test Container](#22-start-up-without-a-login-step-1-done---docker-engine-and-the-test-container)
  - [2.3. Start-Up Without a Login (Step 1, Done) - The Scheduled Task and Memory Cap](#23-start-up-without-a-login-step-1-done---the-scheduled-task-and-memory-cap)
  - [2.4. Start-Up Without a Login (Step 1, Done) - The Reboot Test](#24-start-up-without-a-login-step-1-done---the-reboot-test)
- [3. Home Wifi Access](#3-home-wifi-access)
- [4. Prepare the Stack](#4-prepare-the-stack)
  - [4.1. Prepare the Stack - Update the Repo](#41-prepare-the-stack---update-the-repo)
  - [4.2. Prepare the Stack - .NET SDK for tsx-ops](#42-prepare-the-stack---net-sdk-for-tsx-ops)
  - [4.3. Prepare the Stack - Settings and Secrets](#43-prepare-the-stack---settings-and-secrets)
  - [4.4. Prepare the Stack - Build the Images](#44-prepare-the-stack---build-the-images)
- [5. Move Over](#5-move-over)
  - [5.1. Move Over - Stop the Dev Laptop and Dump](#51-move-over---stop-the-dev-laptop-and-dump)
  - [5.2. Move Over - Restore and Start](#52-move-over---restore-and-start)
  - [5.3. Move Over - Verify](#53-move-over---verify)
  - [5.4. Move Over - Reboot Test](#54-move-over---reboot-test)
  - [5.5. Move Over - The Dev Laptop Afterwards](#55-move-over---the-dev-laptop-afterwards)
- [6. Day to Day](#6-day-to-day)
- [7. Undo](#7-undo)

## 1. What Gets Installed

| Where | What | Script |
|---|---|---|
| Windows | Ubuntu on WSL | `wsl --install` (by hand) |
| Ubuntu | systemd on, Docker Engine (not Docker Desktop) | `wsl/setup-ubuntu.sh` |
| Windows | `.wslconfig` memory cap (default half the RAM), scheduled task "WSL keep-alive" | `windows/setup-wsl-host.ps1` |
| Windows | `.wslconfig` mirrored networking, firewall rules for TCP 5080 from the local network only | `windows/setup-home-network.ps1` |
| Ubuntu | .NET SDK in `~/.dotnet` (for `tsx-ops`) | `dotnet-install.sh` (4.2) |
| Docker | `tsx-postgres`, `tsx-aggregator`, `tsx-webapi`, `tsx-web` | `wsl/stack.sh` |

The "WSL keep-alive" task runs `wsl.exe -d Ubuntu --exec /bin/sleep infinity` at startup, whether or not anyone is logged on, with no time limit. That starts Ubuntu, systemd starts Docker, and Docker restarts the containers. The task tracker and its ngrok tunnel are not touched.

The site uses port 5080, not 5060: Chrome and Edge refuse 5060 (`ERR_UNSAFE_PORT`, the SIP port).

## 2. Start-Up Without a Login (Step 1, Done)

Passed on 2026-11-21. Kept for a rebuild of the laptop.

### 2.1. Start-Up Without a Login (Step 1, Done) - Install Ubuntu on WSL

In an **Administrator** PowerShell: `wsl --install -d Ubuntu`. Reboot if asked; if `wsl -l -v` still lists no distribution, run the command again. It asks for the Linux user and password. If WSL complains about virtualization, turn on VT-x / AMD-V ("SVM") in the BIOS. Then in Ubuntu: `git clone https://github.com/aaronlovi/tsx-aggregator.git ~/dev/tsx-aggregator`.

### 2.2. Start-Up Without a Login (Step 1, Done) - Docker Engine and the Test Container

In Ubuntu: `bash ~/dev/tsx-aggregator/deploy/wsl/setup-ubuntu.sh`. If it turns on systemd and stops, run `wsl --shutdown` in PowerShell, reopen Ubuntu and run it again. It installs Docker Engine and starts a throwaway `nginx` container on `127.0.0.1:5080`. Close and reopen Ubuntu once so `docker` works without `sudo`.

### 2.3. Start-Up Without a Login (Step 1, Done) - The Scheduled Task and Memory Cap

In an **Administrator** PowerShell (the window title says Administrator, the prompt is `C:\WINDOWS\system32`). Type only the command, not the `PS ...>` prompt:

```powershell
powershell -ExecutionPolicy Bypass -File \\wsl$\Ubuntu\home\aaron\dev\tsx-aggregator\deploy\windows\setup-wsl-host.ps1
```

Answer **R** if asked about a network location. It asks for the Windows password of the account (for a Microsoft account, that password, not the PIN). `267009` at the end means the task is running. If the account password changes, run it again.

### 2.4. Start-Up Without a Login (Step 1, Done) - The Reboot Test

Restart, don't log in, wait 5 minutes, connect with Chrome Remote Desktop and log in, then in Ubuntu `bash ~/dev/tsx-aggregator/deploy/wsl/check-startup.sh` and in PowerShell:

```powershell
(Get-CimInstance Win32_OperatingSystem).LastBootUpTime
(Get-ScheduledTaskInfo -TaskName 'WSL keep-alive').LastRunTime
(Get-Process explorer).StartTime
```

Pass: the task time and the container start are before the `explorer` start (the login).

## 3. Home Wifi Access

The wifi is shared with another household, so it stays **Public** in Windows (Private would also turn on network discovery and every other firewall rule scoped to Private). The site is open to any device on the wifi, read-only; nothing else on the laptop is.

1. In an **Administrator** PowerShell:
   ```powershell
   powershell -ExecutionPolicy Bypass -File \\wsl$\Ubuntu\home\aaron\dev\tsx-aggregator\deploy\windows\setup-home-network.ps1
   ```
   It turns on mirrored networking for WSL and allows TCP 5080 in from the local network's addresses only (e.g. `192.168.6.0/24`), on any network type, then prints the laptop's address. Run it again if the router's address range ever changes.
2. Apply it: `wsl --shutdown` in PowerShell, then open Ubuntu.
3. In the **eero app**: Settings → Network settings → Reservations & port forwarding → Add a reservation → this laptop, so its address stays the same. **No port forwarding**: that would open the site to the internet.
4. Test with the throwaway container, published on all addresses this time:
   ```bash
   docker rm -f startup-test
   docker run -d --name startup-test --restart unless-stopped -p 5080:80 nginx:alpine
   ```
   `http://localhost:5080` on the laptop and `http://<address from 1>:5080` on a phone on the wifi both show "Welcome to nginx!"; with the phone on mobile data it does not load.
5. Repeat the reboot test (2.4) once: mirrored networking changes how WSL starts.

## 4. Prepare the Stack

All in Ubuntu unless stated. Nothing here touches the live system on the dev laptop.

### 4.1. Prepare the Stack - Update the Repo

```bash
cd ~/dev/tsx-aggregator && git pull --ff-only
```

### 4.2. Prepare the Stack - .NET SDK for tsx-ops

`tsx-ops` (snapshots, event queue, pause/resume, requeue) runs with `dotnet run`, so Ubuntu needs the SDK version in `src/global.json`:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --jsonfile ~/dev/tsx-aggregator/src/global.json
echo 'export DOTNET_ROOT=$HOME/.dotnet; export PATH=$PATH:$HOME/.dotnet' >> ~/.bashrc
source ~/.bashrc && dotnet --version
```

### 4.3. Prepare the Stack - Settings and Secrets

1. The Google service-account key (the `linear-hangar-…json` file from the dev laptop's `C:\work\tsx-aggregator`), uploaded with Chrome Remote Desktop's **Upload file**:
   ```bash
   mkdir -p ~/tsx-secrets && chmod 700 ~/tsx-secrets
   mv /mnt/c/Users/EMILY/Downloads/linear-hangar-*.json ~/tsx-secrets/google.json
   chmod 600 ~/tsx-secrets/google.json
   ```
2. A Gmail app password for this laptop (Google account → Security → App passwords, e.g. "tsx deploy laptop").
3. The settings file:
   ```bash
   cd ~/dev/tsx-aggregator/docker-scripts
   cp local.env.example local.env && chmod 600 local.env
   nano local.env
   ```
   Set `GOOGLE_CREDENTIALS_FILE=/home/aaron/tsx-secrets/google.json`, the four Gmail lines, `WEB_BIND=0.0.0.0` and `POSTGRES_BIND=127.0.0.1`. Leave the dev-testing lines commented out.

### 4.4. Prepare the Stack - Build the Images

```bash
bash ~/dev/tsx-aggregator/deploy/wsl/stack.sh build
```

The first build downloads the base images and Google Chrome and takes 10–20 minutes. It builds one image at a time: each build peaks at about 1.5 GB, and WSL here has 2.8 GiB.

## 5. Move Over

The live system moves from the dev laptop to this one. Only one may run the scraper, the quote sheet and the alerts at a time (plan 2.7), so the dev laptop stops first.

### 5.1. Move Over - Stop the Dev Laptop and Dump

On the **dev laptop**:

1. Stop tsx-aggregator and the web API: `./run.sh`, option 3.
2. Dump the database and put it where Windows can see it:
   ```bash
   f=~/backups/tsx-deploy-$(date +%Y%m%d-%H%M).dump
   docker exec tsx-postgres pg_dump -U postgres -Fc postgres > "$f" && ls -la "$f"
   cp "$f" /mnt/c/Users/aaron/Desktop/
   ```
3. Take a snapshot to compare with later: `cd src && dotnet run --project tsx-ops -- snapshot --source processed --label pre-move`.

Upload the `.dump` file to the deploy laptop with Chrome Remote Desktop's **Upload file**. Not a GitHub release: the repo is public.

### 5.2. Move Over - Restore and Start

On the **deploy laptop**, in Ubuntu:

```bash
docker rm -f startup-test                       # frees port 5080
bash ~/dev/tsx-aggregator/deploy/wsl/stack.sh db # PostgreSQL only; wait until 'docker ps' shows it healthy
docker exec -i tsx-postgres pg_restore -U postgres -d postgres --no-owner --clean --if-exists \
    < /mnt/c/Users/EMILY/Downloads/tsx-deploy-*.dump
bash ~/dev/tsx-aggregator/deploy/wsl/stack.sh up
```

### 5.3. Move Over - Verify

1. `bash deploy/wsl/stack.sh status`: four containers up; then `bash deploy/wsl/stack.sh logs` for a minute: no `ERR`/`FTL` lines.
2. From `~/dev/tsx-aggregator/src`:
   - `dotnet run --project tsx-ops -- events`: the queue drains.
   - `dotnet run --project tsx-ops -- service status`: Aggregator and RawCollector running.
   - `dotnet run --project tsx-ops -- snapshot --source processed --label post-move`: the same counts as the dev laptop's `pre-move` snapshot (scored, 14+, 15+, 16/16).
3. `http://localhost:5080` in Chrome on this laptop, then `http://<address from 3>:5080` on a phone on the wifi.
4. Writes are refused: `curl -s -o /dev/null -w '%{http_code}\n' -X POST http://localhost:5080/api/companies/priority` prints `405`.
5. With the phone's wifi off (mobile data), the address does not load.
6. Quotes: within two hours the log shows `Found … new quotes` (`stack.sh logs | grep quotes`).

### 5.4. Move Over - Reboot Test

As in 2.4, then check that `http://localhost:5080` and the phone both load without anyone having logged in.

### 5.5. Move Over - The Dev Laptop Afterwards

From now on, run dev with `PASSIVE=1 ./run.sh`: no scraping, no quote-sheet writes, no alert emails. Known gap: with the quote service off, pages that need prices (top list, company details) wait forever; a read-only quote mode is on the plan's list. Copy the deploy database back now and then for realistic dev data (the same dump and restore, the other way).

## 6. Day to Day

| Task | Command (Ubuntu, in `~/dev/tsx-aggregator`) |
|---|---|
| Status and memory | `bash deploy/wsl/stack.sh status` |
| Logs | `bash deploy/wsl/stack.sh logs [tsx-aggregator\|tsx-webapi\|tsx-web\|postgres]` |
| Update to the latest code | `bash deploy/wsl/stack.sh update` |
| Stop / start the app | `bash deploy/wsl/stack.sh down` / `up` |
| Pause or resume scraping | `cd src && dotnet run --project tsx-ops -- service pause RawCollector --execute` |
| Back up | `docker exec tsx-postgres pg_dump -U postgres -Fc postgres > ~/backups/tsx-$(date +%Y%m%d).dump` |

## 7. Undo

```bash
bash ~/dev/tsx-aggregator/deploy/wsl/stack.sh down
```

```powershell
powershell -ExecutionPolicy Bypass -File \\wsl$\Ubuntu\home\aaron\dev\tsx-aggregator\deploy\windows\setup-home-network.ps1 -Remove
Unregister-ScheduledTask -TaskName "WSL keep-alive" -Confirm:$false
```

Remove the `memory=` and `networkingMode=` lines from `%UserProfile%\.wslconfig` if not wanted. The database stays in the Docker volume `tsx-aggregator_postgres-data` until `docker volume rm` removes it.
