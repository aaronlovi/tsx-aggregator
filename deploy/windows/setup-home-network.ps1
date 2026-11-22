# Home-wifi access to the stocks site on the deploy laptop (Deploy Laptop Plan 2.8, D7).
#   1. Turns on WSL mirrored networking in %UserProfile%\.wslconfig, so devices on the wifi can reach ports
#      published in WSL (with the default NAT mode only the laptop itself can).
#   2. Allows TCP <Port> in from Private networks only: a Windows Defender Firewall rule and a Hyper-V firewall
#      rule for WSL (mirrored mode filters WSL traffic with both).
#   3. Checks that the current network is Private; a Public network gets no access.
#
# Run from an Administrator PowerShell, as the Windows user who runs WSL:
#   powershell -ExecutionPolicy Bypass -File deploy\windows\setup-home-network.ps1
# Options: -Port 5080. Re-running replaces the rules. Then run 'wsl --shutdown' (or reboot) to apply.
# Undo: -Remove (deletes both rules; take networkingMode=mirrored out of .wslconfig by hand).

param(
    [int]$Port = 5080,
    [switch]$Remove
)

$ErrorActionPreference = "Stop"
$RuleName = "TSX stocks site (WSL) TCP $Port"
$WslVmCreatorId = "{40E0AC32-46A5-438A-A0B2-2B479E8F2E90}"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Run this from an Administrator PowerShell."
}

function Remove-Rules {
    Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Get-NetFirewallHyperVRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq $RuleName } |
        ForEach-Object { Remove-NetFirewallHyperVRule -Name $_.Name }
}

if ($Remove) {
    Remove-Rules
    Write-Host "Removed the firewall rules '$RuleName'. networkingMode=mirrored in .wslconfig is left as is."
    return
}

# 1. .wslconfig: networkingMode=mirrored under [wsl2], other lines kept.
$wslConfig = Join-Path $env:USERPROFILE ".wslconfig"
$lines = @()
if (Test-Path $wslConfig) { $lines = @(Get-Content $wslConfig) }
$lines = @($lines | Where-Object { $_ -notmatch '^\s*networkingMode\s*=' })
if (-not ($lines -match '^\s*\[wsl2\]\s*$')) { $lines = @("[wsl2]") + $lines }
$out = @()
foreach ($line in $lines) {
    $out += $line
    if ($line -match '^\s*\[wsl2\]\s*$') { $out += "networkingMode=mirrored" }
}
Set-Content -Path $wslConfig -Value $out -Encoding ASCII
Write-Host "WSL networking: mirrored ($wslConfig). Takes effect after 'wsl --shutdown' or a reboot."

# 2. Firewall rules, Private networks only.
Remove-Rules
$null = New-NetFirewallRule -DisplayName $RuleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port `
    -Profile Private -Description "Stocks site in WSL/Docker, home wifi only (tsx-aggregator deploy/README.md)"
$null = New-NetFirewallHyperVRule -Name ("tsx-stocks-" + $Port) -DisplayName $RuleName -Direction Inbound `
    -VMCreatorId $WslVmCreatorId -Protocol TCP -LocalPorts $Port -Action Allow -Profiles Private
Write-Host "Firewall: TCP $Port allowed in from Private networks (Windows Defender Firewall and Hyper-V firewall for WSL)."

# 3. Network profile and address.
$profiles = @(Get-NetConnectionProfile)
foreach ($p in $profiles) {
    Write-Host ("Network '{0}' ({1}): {2}" -f $p.Name, $p.InterfaceAlias, $p.NetworkCategory)
}
if ($profiles | Where-Object { $_.NetworkCategory -eq "Public" }) {
    Write-Warning ("A network is Public, so the rule does not apply there. For the home wifi: Settings > Network & internet > " +
        "Wi-Fi > (your network) > Network profile type > Private network. Only do this for your own home network.")
}
$ips = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.PrefixOrigin -in @("Dhcp", "Manual") -and $_.IPAddress -notlike "169.254.*" } |
    Select-Object -ExpandProperty IPAddress
foreach ($ip in $ips) { Write-Host "From a phone on the wifi: http://${ip}:$Port  (reserve this address in the router)" }
Write-Host "Next: 'wsl --shutdown' in PowerShell, open Ubuntu again, then deploy/README.md."
