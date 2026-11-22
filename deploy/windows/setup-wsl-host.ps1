# Windows side of the deploy-laptop setup (Deploy Laptop Plan, step 1).
#   1. Caps WSL's memory in %UserProfile%\.wslconfig (keeps any other lines there).
#   2. Registers the "WSL keep-alive" scheduled task: at startup, whether or not anyone is logged on,
#      it starts Ubuntu and keeps it running, so systemd, Docker Engine and the containers start.
#
# Run from an Administrator PowerShell, as the Windows user who installed Ubuntu:
#   powershell -ExecutionPolicy Bypass -File deploy\windows\setup-wsl-host.ps1
# Options: -Distro Ubuntu  -WslMemoryGB <n> (default: half the RAM)  -SkipWslConfig
# Re-running replaces the task and the memory line; nothing else changes.

param(
    [string]$Distro = "Ubuntu",
    [int]$WslMemoryGB = 0,
    [switch]$SkipWslConfig
)

$ErrorActionPreference = "Stop"
$TaskName = "WSL keep-alive"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Run this from an Administrator PowerShell."
}

# The distro must exist for this Windows user.
$distros = (wsl.exe --list --quiet) -replace "`0", "" | Where-Object { $_.Trim() -ne "" } | ForEach-Object { $_.Trim() }
if ($distros -notcontains $Distro) {
    Write-Error "WSL distro '$Distro' not found (have: $($distros -join ', ')). Install it first: wsl --install -d $Distro"
}

# 1. .wslconfig memory cap
if (-not $SkipWslConfig) {
    if ($WslMemoryGB -le 0) {
        $totalGB = [math]::Floor((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
        $WslMemoryGB = [math]::Max(2, [math]::Floor($totalGB / 2))
    }
    $wslConfig = Join-Path $env:USERPROFILE ".wslconfig"
    $lines = @()
    if (Test-Path $wslConfig) { $lines = @(Get-Content $wslConfig) }
    $lines = @($lines | Where-Object { $_ -notmatch '^\s*memory\s*=' })
    if (-not ($lines -match '^\s*\[wsl2\]\s*$')) { $lines = @("[wsl2]") + $lines }
    $out = @()
    foreach ($line in $lines) {
        $out += $line
        if ($line -match '^\s*\[wsl2\]\s*$') { $out += "memory=${WslMemoryGB}GB" }
    }
    Set-Content -Path $wslConfig -Value $out -Encoding ASCII
    Write-Host "WSL memory cap: ${WslMemoryGB}GB ($wslConfig). Takes effect after 'wsl --shutdown' or a reboot."
}

# 2. Scheduled task
$wsl = Join-Path $env:SystemRoot "System32\wsl.exe"
$action = New-ScheduledTaskAction -Execute $wsl -Argument "-d $Distro --exec /bin/sleep infinity"
$trigger = New-ScheduledTaskTrigger -AtStartup
# ExecutionTimeLimit 0 = no limit. The default (3 days) would stop the task, and Ubuntu with it.
$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -MultipleInstances IgnoreNew

$user = "$env:USERDOMAIN\$env:USERNAME"
Write-Host "The task runs as $user whether or not anyone is logged on, so Windows needs that account's password."
Write-Host "(For a Microsoft account, its Microsoft account password, not the PIN.)"
$cred = Get-Credential -UserName $user -Message "Password for $user (stored by Task Scheduler)"

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
$null = Register-ScheduledTask -TaskName $TaskName `
    -Description "Starts Ubuntu on WSL at boot and keeps it running, so Docker Engine and its containers run without a login." `
    -Action $action -Trigger $trigger -Settings $settings `
    -User $cred.UserName -Password $cred.GetNetworkCredential().Password -RunLevel Limited

Write-Host "Registered scheduled task '$TaskName'. Starting it now..."
Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds 10
$info = Get-ScheduledTask -TaskName $TaskName | Get-ScheduledTaskInfo
Write-Host "State: $((Get-ScheduledTask -TaskName $TaskName).State); last result: $($info.LastTaskResult) (267009 = running)."
Write-Host "Next: open http://localhost:5080 in a Windows browser, then the reboot test (deploy/README.md section 3)."
