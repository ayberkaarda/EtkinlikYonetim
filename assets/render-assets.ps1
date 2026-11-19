# Renders the SVG sources in this folder to the PNG and ICO files the application embeds.
# Uses headless Microsoft Edge (no window is shown). Run from any directory:
#   powershell -ExecutionPolicy Bypass -File assets/render-assets.ps1
$ErrorActionPreference = 'Stop'

$assets = $PSScriptRoot
$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge was not found.' }

$work = Join-Path ([IO.Path]::GetTempPath()) ("etkinlik-assets-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

function Render([string]$svg, [int]$width, [int]$height, [string]$output) {
    $source = (Join-Path $assets $svg) -replace '\\', '/'
    $page = Join-Path $work 'page.html'
    Set-Content -Path $page -Encoding UTF8 -Value (
        "<!doctype html><html><body style=`"margin:0;background:transparent`">" +
        "<img src=`"file:///$source`" style=`"display:block;width:${width}px;height:${height}px`"></body></html>")
    $profile = Join-Path $work 'profile'
    $arguments = @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars', "--user-data-dir=$profile",
        '--default-background-color=00000000', '--allow-file-access-from-files',
        "--window-size=$width,$height", "--screenshot=$output", "file:///$($page -replace '\\', '/')")
    Start-Process -FilePath $edge -ArgumentList $arguments -WindowStyle Hidden -Wait
    if (-not (Test-Path $output)) { throw "Rendering $svg failed." }
}

# Writes an .ico whose frames are the given PNG files (PNG frames are supported since Windows Vista).
function Write-Icon([string[]]$pngs, [string]$output) {
    $frames = $pngs | ForEach-Object { , [IO.File]::ReadAllBytes($_) }
    $stream = [IO.File]::Create($output)
    $writer = New-Object IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $size = [BitConverter]::ToInt32(@($frame[19], $frame[18], $frame[17], $frame[16]), 0)
            $dimension = if ($size -ge 256) { 0 } else { $size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Length
        }
        foreach ($frame in $frames) { $writer.Write($frame) }
    }
    finally {
        $writer.Dispose()
    }
}

try {
    Render 'logo.svg' 256 256 (Join-Path $assets 'logo.png')
    Render 'background.svg' 1320 920 (Join-Path $assets 'background.png')

    $iconFrames = foreach ($size in 16, 32, 48, 256) {
        $frame = Join-Path $work "icon-$size.png"
        Render 'logo.svg' $size $size $frame
        $frame
    }
    Write-Icon $iconFrames (Join-Path $assets 'app.ico')
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
