<#
.SYNOPSIS
    Installs the FocusLock guard service. Needs administrator rights.

.DESCRIPTION
    Without the service, a session only comes back at logon, seconds after the desktop is already
    usable, and the state it reads from lives in the user's own folder where it can simply be deleted.
    The service runs as LocalSystem from boot, keeps its state in ProgramData where a standard user
    can read but not write, and starts the app as soon as somebody signs in.

    The service never blocks anything itself — it only starts the app. Remove it any time with
    uninstall-service.ps1, or from WinRE (see docs\RECOVERY.md).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\FocusLock\scripts\install-service.ps1"
#>
param(
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

$name = 'FocusLockGuard'
$dataRoot = Join-Path $env:ProgramData 'FocusLock'
$inbox = Join-Path $dataRoot 'inbox'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from an administrator PowerShell window.'
}

if (-not $ExePath) { $ExePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'FocusLock.App.exe' }
if (-not (Test-Path $ExePath)) { throw "FocusLock.App.exe not found at $ExePath" }
$ExePath = (Resolve-Path $ExePath).Path

if ($ExePath -like '\\*') {
    throw "The service cannot start from a network path ($ExePath). Run scripts\install.ps1 first."
}

# --- state folder -------------------------------------------------------------
# Everyone may read the state; only SYSTEM and administrators may change it. The inbox is the one
# place the app writes, so users need to create files there.
New-Item -ItemType Directory -Force $dataRoot | Out-Null
New-Item -ItemType Directory -Force $inbox | Out-Null

& icacls $dataRoot /inheritance:r /grant 'SYSTEM:(OI)(CI)F' 'Administrators:(OI)(CI)F' 'Users:(OI)(CI)RX' | Out-Null
& icacls $inbox /grant 'Users:(OI)(CI)M' | Out-Null
Write-Output "State folder secured: $dataRoot"

# --- the service --------------------------------------------------------------
$existing = Get-Service -Name $name -ErrorAction SilentlyContinue
if ($existing) {
    Write-Output 'Replacing the existing service'
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $name -Force }
    & sc.exe delete $name | Out-Null
    Start-Sleep -Milliseconds 500
}

& sc.exe create $name binPath= "`"$ExePath`" --service" start= auto DisplayName= 'FocusLock Guard' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc create failed with $LASTEXITCODE" }

& sc.exe description $name 'Brings FocusLock back during a focus session. Starts no lock of its own.' | Out-Null

# Restart it if it ever falls over; the guard is worthless if it is the thing that goes missing.
& sc.exe failure $name reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

Start-Service -Name $name
Write-Output ''
Write-Output "Installed and running: $name"
Write-Output "Binary: $ExePath"
Write-Output 'Remove it with scripts\uninstall-service.ps1 (administrator).'
