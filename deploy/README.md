# Deploy Laptop Setup

Scripts and steps for running the stack in Docker inside Ubuntu on WSL on the Windows deploy laptop. The plan and its decisions are in the vault note "Deploy Laptop Plan". This file covers **step 1 only**: proving that Ubuntu, Docker Engine and a container start after a reboot with nobody logged in, before any tsx code goes there.

## Table of Contents

- [1. What Gets Installed](#1-what-gets-installed)
- [2. Setup](#2-setup)
  - [2.1. Setup - Get the Scripts onto the Laptop](#21-setup---get-the-scripts-onto-the-laptop)
  - [2.2. Setup - Install Ubuntu on WSL](#22-setup---install-ubuntu-on-wsl)
  - [2.3. Setup - Docker Engine and the Test Container](#23-setup---docker-engine-and-the-test-container)
  - [2.4. Setup - The Scheduled Task and Memory Cap](#24-setup---the-scheduled-task-and-memory-cap)
- [3. The Reboot Test](#3-the-reboot-test)
- [4. Undo](#4-undo)

## 1. What Gets Installed

| Where | What | Script |
|---|---|---|
| Windows | Ubuntu on WSL | `wsl --install` (by hand) |
| Ubuntu | systemd on, Docker Engine (not Docker Desktop), a throwaway `nginx` container on `127.0.0.1:5080` | `wsl/setup-ubuntu.sh` |

Port 5080, not 5060: Chrome and Edge refuse 5060 (`ERR_UNSAFE_PORT`, the SIP port).
| Windows | `.wslconfig` memory cap (default half the RAM), scheduled task "WSL keep-alive" | `windows/setup-wsl-host.ps1` |

The "WSL keep-alive" task runs `wsl.exe -d Ubuntu --exec /bin/sleep infinity` at startup, whether or not anyone is logged on, with no time limit. That starts Ubuntu, systemd starts Docker, and Docker restarts the container. The task tracker and ngrok are not touched.

## 2. Setup

### 2.1. Setup - Get the Scripts onto the Laptop

Either clone the repo inside Ubuntu once it is installed (2.2), or, before the repo is pushed, copy the `deploy` folder over with Chrome Remote Desktop's **Upload file** (session menu on the right). Uploaded files land in the deploy laptop's `Downloads` folder, which Ubuntu sees as `/mnt/c/Users/<windows user>/Downloads`.

### 2.2. Setup - Install Ubuntu on WSL

In an **Administrator** PowerShell:

```powershell
wsl --install -d Ubuntu
```

Reboot when asked, then open **Ubuntu** from the Start menu and create the Linux user and password. If WSL complains about virtualization, turn on virtualization (VT-x / AMD-V, "SVM") in the laptop's BIOS.

### 2.3. Setup - Docker Engine and the Test Container

In Ubuntu:

```bash
bash deploy/wsl/setup-ubuntu.sh
```

The first run only turns on systemd and stops. Then, in Windows PowerShell, run `wsl --shutdown`, open Ubuntu again and run the script a second time. It installs Docker Engine, starts it, and starts the test container. It ends with "http://127.0.0.1:5080 answers inside Ubuntu."

### 2.4. Setup - The Scheduled Task and Memory Cap

In an **Administrator** PowerShell (path to wherever the `deploy` folder is):

```powershell
powershell -ExecutionPolicy Bypass -File deploy\windows\setup-wsl-host.ps1
```

It asks for your Windows account password (for a Microsoft account, the Microsoft account password, not the PIN); Task Scheduler needs it to run the task with nobody logged on. It ends with the task state; `267009` means running. Options: `-WslMemoryGB 8` to choose the cap, `-SkipWslConfig` to leave `.wslconfig` alone.

Then open `http://localhost:5080` in a Windows browser: it should show "Welcome to nginx!". That checks that Windows reaches Docker's ports in Ubuntu.

## 3. The Reboot Test

1. Restart the laptop (Start → Power → Restart). **Don't log in.**
2. Wait 5 minutes. Note the time.
3. Connect with Chrome Remote Desktop and log in. Note the time.
4. Open Ubuntu and run `bash deploy/wsl/check-startup.sh`.
5. **Pass** if "Ubuntu (WSL) up since" and "Container started" are both before the login time, and `http://localhost:5080` shows the nginx page in a Windows browser.
6. Check the task tracker still works (its ngrok address from a phone).

If it fails, Task Scheduler → "WSL keep-alive" → History and "Last Run Result" show why.

## 4. Undo

```bash
docker rm -f startup-test
```

```powershell
Unregister-ScheduledTask -TaskName "WSL keep-alive" -Confirm:$false
```

Remove the `memory=` line from `%UserProfile%\.wslconfig` if not wanted.
