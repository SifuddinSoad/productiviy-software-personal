<#
.SYNOPSIS
    Copies FocusLock onto this PC and runs it from there.

.DESCRIPTION
    Windows starts the logon entry before shared and network folders exist, so a session can only
    survive a restart when the app sits on a local disk. Running straight from \\VBoxSvr\build
    works for everything except that — which is the part worth testing.

    Installs to %LOCALAPPDATA%\Programs\FocusLock, so no admin rights are needed.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File \\VBoxSvr\build\FocusLock\scripts\install.ps1
#>
param(
    [string]$Source,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

if (-not $Source) { $Source = Split-Path $PSScriptRoot -Parent }
$target = Join-Path $env:LOCALAPPDATA 'Programs\FocusLock'
$exe = Join-Path $target 'FocusLock.App.exe'

if (-not (Test-Path (Join-Path $Source 'FocusLock.App.exe'))) {
    throw "FocusLock.App.exe not found in $Source"
}

Get-Process -Name 'FocusLock.App' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output 'Closing the running copy'
    $_.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 800
    if (-not $_.HasExited) { $_ | Stop-Process -Force }
}

Write-Output "Installing to $target"
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $Source '*') $target -Recurse -Force

# Start menu shortcut, so it can be launched like any other app
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\FocusLock.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($startMenu)
$link.TargetPath = $exe
$link.WorkingDirectory = $target
$link.Description = 'Saidrix Studio FocusLock'
$link.Save()

Write-Output ''
Write-Output "Installed: $exe"
Write-Output 'Start menu shortcut created.'
Write-Output 'Restart-resume will work from here, unlike the shared folder.'

if (-not $NoLaunch) {
    Write-Output ''
    Write-Output 'Starting it...'
    Start-Process $exe
}
