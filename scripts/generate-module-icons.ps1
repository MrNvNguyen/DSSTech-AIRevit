# Generate the module ribbon icons (16/32 px, light + dark) and apply the contrast palette to existing icons.
# See docs/RIBBON_UI.md. Safe to re-run.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\DSSTech.Revit\Assets"

function Get-Color([string]$Hex) { [System.Drawing.ColorTranslator]::FromHtml($Hex) }

$Themes = @(
    @{ Suffix = ""; Line = "#1B3A5C"; Accent = "#9A5B00" },
    @{ Suffix = "-dark"; Line = "#D6E2EE"; Accent = "#E9A23B" }
)

# All drawings use a 32x32 grid; 16 px icons are the same drawing scaled down with a heavier pen.
$Icons = [ordered]@{
    "icon-part-column"  = { param($g, $p, $b)
        $g.FillRectangle($b, 10, 4, 12, 7); $g.DrawRectangle($p, 10, 4, 12, 24) }
    "icon-part-wall"    = { param($g, $p, $b)
        $g.FillRectangle($b, 4, 10, 7, 12); $g.DrawRectangle($p, 4, 10, 24, 12); $g.DrawLine($p, 11, 10, 11, 22) }
    "icon-part-beam"    = { param($g, $p, $b)
        $g.FillRectangle($b, 3, 10, 26, 7); $g.DrawRectangle($p, 3, 10, 26, 7)
        $g.DrawRectangle($p, 4, 17, 5, 11); $g.DrawRectangle($p, 23, 17, 5, 11) }
    "icon-part-floor"   = { param($g, $p, $b)
        $g.FillRectangle($b, 2, 21, 24, 6); $g.DrawRectangle($p, 2, 21, 24, 6)
        $g.DrawPolygon($p, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 2, 21), (New-Object System.Drawing.PointF 9, 8), (New-Object System.Drawing.PointF 30, 8), (New-Object System.Drawing.PointF 26, 21))) }
    "icon-part-pilecap" = { param($g, $p, $b)
        $g.FillRectangle($b, 5, 7, 22, 9); $g.DrawRectangle($p, 5, 7, 22, 9)
        $g.DrawRectangle($p, 8, 16, 5, 12); $g.DrawRectangle($p, 19, 16, 5, 12) }
    "icon-part-footing" = { param($g, $p, $b)
        $g.FillRectangle($b, 5, 18, 22, 9); $g.DrawRectangle($p, 5, 18, 22, 9); $g.DrawRectangle($p, 12, 4, 8, 14) }
    "icon-part-strip"   = { param($g, $p, $b)
        $g.FillRectangle($b, 2, 20, 28, 7); $g.DrawRectangle($p, 2, 20, 28, 7); $g.DrawRectangle($p, 11, 6, 10, 14) }
    "icon-part-raft"    = { param($g, $p, $b)
        $g.FillRectangle($b, 2, 18, 28, 9); $g.DrawRectangle($p, 2, 18, 28, 9)
        $g.DrawRectangle($p, 5, 6, 5, 12); $g.DrawRectangle($p, 14, 6, 5, 12); $g.DrawRectangle($p, 23, 6, 5, 12) }
    "icon-part-pile"    = { param($g, $p, $b)
        $g.FillRectangle($b, 12, 3, 8, 6); $g.DrawRectangle($p, 12, 3, 8, 20)
        $g.DrawLine($p, 12, 23, 16, 29); $g.DrawLine($p, 20, 23, 16, 29) }
    "icon-part-archwall" = { param($g, $p, $b)
        $g.FillRectangle($b, 11, 14, 10, 6); $g.DrawRectangle($p, 3, 8, 26, 18)
        $g.DrawLine($p, 3, 14, 29, 14); $g.DrawLine($p, 3, 20, 29, 20)
        $g.DrawLine($p, 16, 8, 16, 14); $g.DrawLine($p, 11, 14, 11, 20); $g.DrawLine($p, 21, 14, 21, 20); $g.DrawLine($p, 16, 20, 16, 26) }
    "icon-part-door"    = { param($g, $p, $b)
        $g.DrawRectangle($p, 8, 3, 16, 26); $g.FillEllipse($b, 18, 14, 5, 5) }
    "icon-part-window"  = { param($g, $p, $b)
        $g.FillRectangle($b, 5, 6, 11, 10); $g.DrawRectangle($p, 5, 6, 22, 20)
        $g.DrawLine($p, 16, 6, 16, 26); $g.DrawLine($p, 5, 16, 27, 16) }
    "icon-part-room"    = { param($g, $p, $b)
        $g.FillEllipse($b, 10, 10, 11, 11)
        $g.DrawLines($p, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 19, 27), (New-Object System.Drawing.PointF 4, 27), (New-Object System.Drawing.PointF 4, 4), (New-Object System.Drawing.PointF 27, 4), (New-Object System.Drawing.PointF 27, 27), (New-Object System.Drawing.PointF 25, 27))) }
    "icon-part-ceiling" = { param($g, $p, $b)
        $g.DrawLine($p, 3, 6, 29, 6); $g.DrawLine($p, 8, 6, 8, 15); $g.DrawLine($p, 24, 6, 24, 15)
        $g.FillRectangle($b, 3, 15, 13, 8); $g.DrawRectangle($p, 3, 15, 26, 8); $g.DrawLine($p, 16, 15, 16, 23) }
    "icon-part-pipe"    = { param($g, $p, $b)
        $g.DrawLines($p, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 3, 10), (New-Object System.Drawing.PointF 18, 10), (New-Object System.Drawing.PointF 18, 29)))
        $g.DrawLines($p, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 3, 18), (New-Object System.Drawing.PointF 10, 18), (New-Object System.Drawing.PointF 10, 29)))
        $g.FillRectangle($b, 7, 23, 14, 5) }
    "icon-part-duct"    = { param($g, $p, $b)
        $g.DrawRectangle($p, 3, 9, 26, 14); $g.FillPolygon($b, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 9, 12), (New-Object System.Drawing.PointF 22, 16), (New-Object System.Drawing.PointF 9, 20))) }
    "icon-part-tray"    = { param($g, $p, $b)
        $g.FillRectangle($b, 12, 13, 8, 12)
        $g.DrawLines($p, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 3, 8), (New-Object System.Drawing.PointF 3, 25), (New-Object System.Drawing.PointF 29, 25), (New-Object System.Drawing.PointF 29, 8))) }
    "icon-part-equipment" = { param($g, $p, $b)
        $g.DrawRectangle($p, 4, 7, 24, 19); $g.FillEllipse($b, 10, 10, 12, 12); $g.DrawEllipse($p, 10, 10, 12, 12) }
    "icon-part-grid"    = { param($g, $p, $b)
        $g.FillEllipse($b, 3, 3, 9, 9); $g.DrawEllipse($p, 3, 3, 9, 9); $g.DrawEllipse($p, 20, 3, 9, 9)
        $g.DrawLine($p, 7.5, 12, 7.5, 29); $g.DrawLine($p, 24.5, 12, 24.5, 29); $g.DrawLine($p, 3, 19, 29, 19) }
    "icon-level"        = { param($g, $p, $b)
        $g.DrawLine($p, 3, 9, 21, 9); $g.DrawLine($p, 3, 23, 21, 23)
        $g.FillPolygon($b, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 22, 17), (New-Object System.Drawing.PointF 30, 17), (New-Object System.Drawing.PointF 26, 23)))
        $g.DrawLine($p, 26, 9, 26, 17) }
}

foreach ($name in $Icons.Keys) {
    foreach ($theme in $Themes) {
        foreach ($size in 32, 16) {
            $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $g.ScaleTransform($size / 32.0, $size / 32.0)
            $pen = New-Object System.Drawing.Pen (Get-Color $theme.Line), $(if ($size -eq 32) { 2.0 } else { 2.6 })
            $brush = New-Object System.Drawing.SolidBrush (Get-Color $theme.Accent)
            & $Icons[$name] $g $pen $brush
            $pen.Dispose(); $brush.Dispose(); $g.Dispose()
            $bmp.Save((Join-Path $out "$name-$size$($theme.Suffix).png"), [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
        }
    }
}

# Existing icons: accent and AI state colours that fall short on one background (docs/RIBBON_UI.md).
function Set-IconColor([string]$Pattern, [string]$From, [string]$To) {
    $src = Get-Color $From
    $dst = Get-Color $To
    foreach ($file in Get-ChildItem $out -Filter $Pattern) {
        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        $stream = New-Object System.IO.MemoryStream (, $bytes)
        $image = [System.Drawing.Image]::FromStream($stream)
        $bmp = New-Object System.Drawing.Bitmap $image
        $image.Dispose(); $stream.Dispose()
        $changed = $false
        for ($y = 0; $y -lt $bmp.Height; $y++) {
            for ($x = 0; $x -lt $bmp.Width; $x++) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.A -eq 0) { continue }
                if ([Math]::Abs($c.R - $src.R) + [Math]::Abs($c.G - $src.G) + [Math]::Abs($c.B - $src.B) -gt 60) { continue }
                $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($c.A, $dst.R, $dst.G, $dst.B))
                $changed = $true
            }
        }
        if ($changed) { $bmp.Save($file.FullName, [System.Drawing.Imaging.ImageFormat]::Png) }
        $bmp.Dispose()
    }
}

Set-IconColor "icon-*-16.png" "#E9A23B" "#9A5B00"
Set-IconColor "icon-*-32.png" "#E9A23B" "#9A5B00"
Set-IconColor "icon-mcp-16.png" "#1F9D55" "#14803F"
Set-IconColor "icon-mcp-32.png" "#1F9D55" "#14803F"
Set-IconColor "icon-mcp-*-dark.png" "#1F9D55" "#4CC784"
Set-IconColor "icon-mcp-off-16.png" "#8A949E" "#5F6B76"
Set-IconColor "icon-mcp-off-32.png" "#8A949E" "#5F6B76"
Set-IconColor "icon-mcp-off-*-dark.png" "#8A949E" "#B4BEC8"

Write-Host "Module icons ready in $out"
