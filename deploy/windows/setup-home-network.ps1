# Home-wifi access to the stocks site on the deploy laptop (Deploy Laptop Plan 2.8, D7).
#   1. Turns on WSL mirrored networking in %UserProfile%\.wslconfig, so devices on the wifi can reach ports
#      published in WSL (with the default NAT mode only the laptop itself can).
#   2. Allows TCP <Port> in from devices on the local network (subnet) only, whatever the network type, so the
#      wifi can stay Public (shared with another household; Private would also turn on network discovery and
#      every other rule scoped to Private). A Windows Defender Firewall rule and a Hyper-V firewall rule for WSL
#      (mirrored mode filters WSL traffic with both).
#   3. Prints the network(s) and the laptop's address for phones.
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

# The real network connections (Wi-Fi, Ethernet), not WSL's or Hyper-V's virtual adapters, and their subnets.
$profiles = @(Get-NetConnectionProfile)
$addresses = @(foreach ($p in $profiles) {
    Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $p.InterfaceIndex -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike "169.254.*" }
})
if ($addresses.Count -eq 0) {
    Write-Error "No network connection with an IPv4 address found. Connect to the home wifi first."
}

function Get-SubnetCidr([string]$ip, [int]$prefix) {
    # Byte by byte: Windows PowerShell 5.1 reads 0xFFFFFFFF as -1, so 32-bit mask arithmetic is unreliable.
    $bytes = ([System.Net.IPAddress]::Parse($ip)).GetAddressBytes()
    for ($i = 0; $i -lt 4; $i++) {
        $bits = [Math]::Min(8, [Math]::Max(0, $prefix - 8 * $i))
        $maskByte = (0xFF -shl (8 - $bits)) -band 0xFF
        $bytes[$i] = [byte]($bytes[$i] -band $maskByte)
    }
    return "{0}/{1}" -f ([System.Net.IPAddress]::new($bytes)), $prefix
}
$subnets = @($addresses | ForEach-Object { Get-SubnetCidr $_.IPAddress $_.PrefixLength } | Sort-Object -Unique)

# 2. Firewall rules: local subnet only, any network type.
Remove-Rules
$null = New-NetFirewallRule -DisplayName $RuleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port `
    -Profile Any -RemoteAddress LocalSubnet `
    -Description "Stocks site in WSL/Docker, local network only (tsx-aggregator deploy/README.md)"
$null = New-NetFirewallHyperVRule -Name ("tsx-stocks-" + $Port) -DisplayName $RuleName -Direction Inbound `
    -VMCreatorId $WslVmCreatorId -Protocol TCP -LocalPorts $Port -RemoteAddresses $subnets -Action Allow -Profiles Any
Write-Host ("Firewall: TCP $Port allowed in from the local network only ({0}), on any network type." -f ($subnets -join ", "))
Write-Host "If the wifi's address range changes (new router), run this script again."

# 3. Networks and addresses.
foreach ($p in $profiles) {
    Write-Host ("Network '{0}' ({1}): {2} (stays as it is)" -f $p.Name, $p.InterfaceAlias, $p.NetworkCategory)
}
foreach ($a in $addresses) { Write-Host "From a phone on the wifi: http://$($a.IPAddress):$Port  (reserve this address in the router)" }
Write-Host "Next: 'wsl --shutdown' in PowerShell, open Ubuntu again, then deploy/README.md."
