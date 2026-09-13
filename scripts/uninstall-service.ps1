<#
.SYNOPSIS
    Removes the FocusLock guard service and everything it kept. Needs administrator rights.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\FocusLock\scripts\uninstall-service.ps1"
#>
param(
    [switch]$KeepState
)

$ErrorActionPreference = 'Stop'

$name = 'FocusLockGuard'
$dataRoot = Join-Path $env:ProgramData 'FocusLock'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this from an administrator PowerShell window.'
}

$service = Get-Service -Name $name -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $name -Force }
    & sc.exe delete $name | Out-Null
    Write-Output "Service removed: $name"
} else {
    Write-Output 'Service was not installed.'
}

if (-not $KeepState -and (Test-Path $dataRoot)) {
    # Take ownership back first: the folder was locked down to SYSTEM on install.
    & icacls $dataRoot /grant 'Administrators:(OI)(CI)F' /T | Out-Null
    Remove-Item $dataRoot -Recurse -Force
    Write-Output "State removed: $dataRoot"
}

Write-Output ''
Write-Output 'Sessions and whiteboards are untouched; they live in your own AppData folder.'
