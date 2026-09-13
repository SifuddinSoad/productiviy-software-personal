<#
.SYNOPSIS
    Removes FocusLock's lock traces without needing the app to run.

.DESCRIPTION
    Deletes the HKCU startup entry, the DisableTaskMgr policy value and the active-session pointer
    for the current user. Session data stays unless -RemoveData is given.
    Run it as the user who was locked (not as another admin account), because everything is under HKCU.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1
#>
param(
    [switch]$RemoveData
)

$ErrorActionPreference = 'Continue'
$root = Join-Path $env:LOCALAPPDATA 'FocusLock'

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'FocusLock' -ErrorAction SilentlyContinue
Write-Output 'Startup entry removed'

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'DisableTaskMgr' -ErrorAction SilentlyContinue
Write-Output 'Task Manager policy removed'

$active = Join-Path $root 'active.json'
if (Test-Path $active) {
    Remove-Item $active -Force
    Write-Output 'Active session cleared'
}

Get-Process -Name 'FocusLock.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if ($RemoveData -and (Test-Path $root)) {
    Remove-Item $root -Recurse -Force
    Write-Output "Removed $root"
}

Write-Output 'Done.'
