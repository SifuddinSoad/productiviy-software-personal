<#
.SYNOPSIS
    Publishes FocusLock into build\FocusLock, ready to copy into the test VM.

.DESCRIPTION
    Self-contained, so the VM needs no .NET install. build\ is the folder shared with the VM
    as \\VBoxSvr\build, so the output lands where the VM can reach it.

    -Debug publishes the DEBUG build, where the session length options count in SECONDS
    (30, 45, 60, 90, 120) instead of minutes. Use that for lock testing — a whole session
    then takes a minute or two.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Debug
#>
param(
    [switch]$Debug
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$config = if ($Debug) { 'Debug' } else { 'Release' }
$out = Join-Path $root 'build\FocusLock'

Write-Output "Publishing $config to $out"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root 'src\FocusLock.App\FocusLock.App.csproj') `
    -c $config -r win-x64 --self-contained true `
    -p:DebugType=embedded `
    -o $out

# The recovery script travels with the app: it is what you run if a lock ever gets stuck.
New-Item -ItemType Directory -Force (Join-Path $out 'scripts') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') (Join-Path $out 'scripts') -Force
Copy-Item (Join-Path $root 'docs\RECOVERY.md') $out -Force

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Output ''
Write-Output ("Done: {0:N0} MB in {1}" -f $size, $out)
Write-Output 'In the VM: \\VBoxSvr\build\FocusLock\FocusLock.App.exe'
if ($Debug) { Write-Output 'DEBUG build - session lengths are in SECONDS.' }
