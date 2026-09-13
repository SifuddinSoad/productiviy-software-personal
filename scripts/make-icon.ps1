<#
.SYNOPSIS
    Renders the Focus Mood logo into src\FocusLock.App\Assets\FocusMood.ico.

.DESCRIPTION
    The logo is drawn once, in src\FocusLock.App\Theme\Logo.xaml. This loads that file with WPF,
    renders the icon drawing at every size Windows asks for, and packs the PNGs into one .ico.
    Sizes up to 32 px use the LogoIconSmall drawing, whose heavier ring survives at 16 px and
    does not go grey at the taskbar's 32.

    Run it after changing Logo.xaml and commit the .ico; the build itself never runs it.

    -PreviewDir also writes each size as a PNG, for looking at.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
#>
param(
    [string]$PreviewDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$root = Split-Path $PSScriptRoot -Parent
$xaml = Join-Path $root 'src\FocusLock.App\Theme\Logo.xaml'
$ico = Join-Path $root 'src\FocusLock.App\Assets\FocusMood.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

$stream = [IO.File]::OpenRead($xaml)
try { $logo = [Windows.Markup.XamlReader]::Load($stream) } finally { $stream.Dispose() }

function Render-Png([Windows.Media.ImageSource]$image, [int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.DrawImage($image, (New-Object Windows.Rect 0, 0, $size, $size))
    $dc.Close()

    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $out = New-Object IO.MemoryStream
    $encoder.Save($out)
    return , $out.ToArray()
}

$images = foreach ($size in $sizes) {
    $key = if ($size -le 32) { 'LogoIconSmall' } else { 'LogoIcon' }
    [pscustomobject]@{ Size = $size; Png = (Render-Png $logo[$key] $size) }
}

# ICO: a 6-byte header, a 16-byte entry per image, then the PNG data. A width or height of 0
# means 256.
$file = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $file
$w.Write([uint16]0)
$w.Write([uint16]1)
$w.Write([uint16]$images.Count)

$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim)
    $w.Write([byte]$dim)
    $w.Write([byte]0)        # palette colours
    $w.Write([byte]0)        # reserved
    $w.Write([uint16]1)      # colour planes
    $w.Write([uint16]32)     # bits per pixel
    $w.Write([uint32]$img.Png.Length)
    $w.Write([uint32]$offset)
    $offset += $img.Png.Length
}
foreach ($img in $images) { $w.Write($img.Png) }
$w.Flush()
[IO.File]::WriteAllBytes($ico, $file.ToArray())

if ($PreviewDir) {
    New-Item -ItemType Directory -Force $PreviewDir | Out-Null
    foreach ($img in $images) {
        [IO.File]::WriteAllBytes((Join-Path $PreviewDir "icon-$($img.Size).png"), $img.Png)
    }
    [IO.File]::WriteAllBytes((Join-Path $PreviewDir 'light-256.png'), (Render-Png $logo['LogoLight'] 256))
    [IO.File]::WriteAllBytes((Join-Path $PreviewDir 'light-26.png'), (Render-Png $logo['LogoLight'] 26))
}

Write-Output ("Wrote {0} ({1} sizes, {2:N0} KB)" -f $ico, $images.Count, ((Get-Item $ico).Length / 1KB))
