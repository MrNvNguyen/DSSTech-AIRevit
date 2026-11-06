# Generate the module ribbon icons (16/32 px, light + dark) and apply the contrast palette to existing icons.
# See docs/RIBBON_UI.md. Safe to re-run. -Only icon-a,icon-b draws just those (no palette pass on the others).
param([string[]]$Only)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function Pts([float[]]$xy) {
    $list = @()
    for ($i = 0; $i -lt $xy.Count; $i += 2) { $list += New-Object System.Drawing.PointF $xy[$i], $xy[$i + 1] }
    [System.Drawing.PointF[]]$list
}

# A pen in the accent colour, as wide as the line pen.
function AccentPen($p, $b) { New-Object System.Drawing.Pen $b.Color, $p.Width }

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

    # ── MEPF: one picture per discipline and per kind of run ──
    "icon-mep-systems"  = { param($g, $p, $b)
        $g.FillRectangle($b, 3, 5, 6, 6); $g.DrawLine($p, 12, 8, 29, 8)
        $g.FillRectangle($b, 3, 13, 6, 6); $g.DrawLine($p, 12, 16, 29, 16)
        $g.FillRectangle($b, 3, 21, 6, 6); $g.DrawLine($p, 12, 24, 29, 24) }
    "icon-mep-mech"     = { param($g, $p, $b)
        $g.DrawEllipse($p, 3, 3, 26, 26)
        foreach ($a in 10, 100, 190, 280) { $g.FillPie($b, 6, 6, 20, 20, $a, 45) }
        $g.DrawEllipse($p, 13, 13, 6, 6) }
    "icon-mep-elec"     = { param($g, $p, $b)
        $bolt = Pts @(19, 2, 7, 18, 15, 18, 12, 30, 25, 13, 17, 13, 22, 2)
        $g.FillPolygon($b, $bolt); $g.DrawPolygon($p, $bolt) }
    "icon-mep-water"    = { param($g, $p, $b)
        $g.FillEllipse($b, 7, 13, 18, 17); $g.FillPolygon($b, (Pts @(16, 3, 8, 19, 24, 19)))
        $g.DrawLine($p, 16, 3, 8, 18); $g.DrawLine($p, 16, 3, 24, 18); $g.DrawArc($p, 7, 13, 18, 17, -20, 220) }
    "icon-mep-fire"     = { param($g, $p, $b)
        $flame = Pts @(16, 2, 22, 10, 25, 17, 24, 24, 19, 29, 13, 29, 8, 24, 7, 17, 11, 11, 13, 16, 15, 9)
        $g.FillPolygon($b, $flame); $g.DrawPolygon($p, $flame) }
    "icon-mep-chw"      = { param($g, $p, $b)
        $g.DrawLine($p, 3, 10, 29, 10); $g.DrawLine($p, 3, 22, 29, 22)
        $g.FillPolygon($b, (Pts @(19, 4, 28, 10, 19, 16))); $g.FillPolygon($b, (Pts @(13, 16, 4, 22, 13, 28))) }
    "icon-mep-condensate" = { param($g, $p, $b)
        $g.DrawRectangle($p, 3, 3, 26, 9); $g.DrawLine($p, 6, 9, 26, 9)
        $g.DrawLine($p, 8, 12, 8, 17); $g.DrawLine($p, 8, 17, 26, 22)
        $g.FillPolygon($b, (Pts @(26, 23, 23, 28, 29, 28))); $g.FillEllipse($b, 23, 25, 6, 6) }
    "icon-mep-drain"    = { param($g, $p, $b)
        $g.DrawLine($p, 3, 7, 24, 13); $g.DrawLine($p, 24, 13, 24, 19)
        $g.FillPolygon($b, (Pts @(18, 19, 30, 19, 24, 26))); $g.DrawLine($p, 3, 29, 29, 29) }
    "icon-mep-supply"   = { param($g, $p, $b)
        $g.DrawRectangle($p, 3, 9, 17, 6); $g.DrawLines($p, (Pts @(20, 12, 25, 12, 25, 17)))
        $g.DrawLine($p, 11, 9, 11, 4); $g.DrawLine($p, 7, 4, 15, 4)
        $g.FillPolygon($b, (Pts @(25, 19, 22, 25, 28, 25))); $g.FillEllipse($b, 22, 22, 6, 6) }
    "icon-mep-sprinkler" = { param($g, $p, $b)
        $g.DrawLine($p, 3, 5, 29, 5); $g.DrawLine($p, 16, 5, 16, 12)
        $g.FillPolygon($b, (Pts @(10, 12, 22, 12, 16, 18)))
        $g.DrawLine($p, 13, 21, 7, 29); $g.DrawLine($p, 16, 21, 16, 30); $g.DrawLine($p, 19, 21, 25, 29) }
    "icon-mep-smoke"    = { param($g, $p, $b)
        $g.DrawRectangle($p, 9, 14, 14, 16); $g.FillPolygon($b, (Pts @(16, 17, 21, 24, 11, 24))); $g.FillRectangle($b, 14, 24, 4, 4)
        $g.DrawBezier($p, 12, 12, 8, 9, 16, 6, 12, 2); $g.DrawBezier($p, 20, 12, 16, 9, 24, 6, 20, 2) }

    "icon-mep-terminal" = { param($g, $p, $b)
        $g.DrawLine($p, 3, 6, 29, 6); $g.DrawRectangle($p, 6, 10, 20, 8); $g.FillRectangle($b, 10, 13, 12, 3)
        $g.DrawLine($p, 9, 21, 5, 28); $g.DrawLine($p, 16, 21, 16, 29); $g.DrawLine($p, 23, 21, 27, 28) }
    "icon-mep-flex"     = { param($g, $p, $b)
        $g.DrawRectangle($p, 3, 3, 26, 7)
        $ap = AccentPen $p $b; $g.DrawBezier($ap, 16, 10, 6, 15, 26, 20, 16, 25); $ap.Dispose()
        $g.DrawRectangle($p, 10, 25, 12, 4) }

    # ── Devices: the drop-downs and each kind of device ──
    "icon-dev-wall"     = { param($g, $p, $b)
        $g.FillRectangle($b, 3, 3, 6, 26); $g.DrawRectangle($p, 3, 3, 6, 26)
        $g.DrawRectangle($p, 14, 9, 14, 14); $g.FillEllipse($b, 17, 13, 3, 6); $g.FillEllipse($b, 22, 13, 3, 6) }
    "icon-dev-ceiling"  = { param($g, $p, $b)
        $g.FillRectangle($b, 3, 3, 26, 5); $g.DrawRectangle($p, 3, 3, 26, 5); $g.DrawLine($p, 16, 8, 16, 13)
        $g.DrawPolygon($p, (Pts @(9, 22, 23, 22, 19, 13, 13, 13)))
        $g.DrawLine($p, 16, 25, 16, 29); $g.DrawLine($p, 10, 25, 7, 28); $g.DrawLine($p, 22, 25, 25, 28) }
    "icon-dev-socket"   = { param($g, $p, $b)
        $g.DrawRectangle($p, 5, 5, 22, 22); $g.FillEllipse($b, 10, 10, 4, 8); $g.FillEllipse($b, 18, 10, 4, 8); $g.FillRectangle($b, 14, 21, 4, 3) }
    "icon-dev-switch"   = { param($g, $p, $b)
        $g.DrawRectangle($p, 7, 3, 18, 26); $g.FillRectangle($b, 12, 8, 8, 8); $g.DrawRectangle($p, 12, 8, 8, 16) }
    "icon-dev-walllight" = { param($g, $p, $b)
        $g.DrawLine($p, 4, 3, 4, 29); $g.DrawLine($p, 4, 16, 10, 16); $g.FillEllipse($b, 10, 9, 13, 14)
        $g.DrawLine($p, 25, 10, 29, 7); $g.DrawLine($p, 26, 16, 30, 16); $g.DrawLine($p, 25, 22, 29, 25) }
    "icon-dev-splitac"  = { param($g, $p, $b)
        $g.DrawRectangle($p, 3, 6, 26, 12); $g.FillRectangle($b, 6, 14, 20, 2)
        $g.DrawLine($p, 9, 21, 7, 28); $g.DrawLine($p, 16, 21, 16, 28); $g.DrawLine($p, 23, 21, 25, 28) }
    "icon-dev-ceilinglight" = { param($g, $p, $b)
        $g.DrawLine($p, 3, 5, 29, 5); $g.FillPie($b, 6, -5, 20, 20, 0, 180)
        $g.DrawLine($p, 16, 19, 16, 25); $g.DrawLine($p, 9, 17, 5, 23); $g.DrawLine($p, 23, 17, 27, 23) }
    "icon-dev-sprinkler" = { param($g, $p, $b)
        $g.DrawLine($p, 3, 5, 29, 5); $g.FillRectangle($b, 14, 5, 4, 6); $g.DrawLine($p, 9, 12, 23, 12)
        foreach ($d in @(@(6, 17), @(14.5, 20), @(23, 17), @(10, 25), @(19, 25))) { $g.FillEllipse($b, $d[0], $d[1], 3.5, 3.5) } }
    "icon-dev-firealarm" = { param($g, $p, $b)
        $g.DrawLine($p, 3, 5, 29, 5); $g.FillPie($b, 7, -2, 18, 14, 0, 180)
        $g.DrawArc($p, 8, 10, 16, 10, 20, 140); $g.DrawArc($p, 4, 12, 24, 14, 25, 130) }

    # ── Combine panel ──
    "icon-combine"      = { param($g, $p, $b)
        $g.DrawLine($p, 3, 11, 29, 11)
        $ap = AccentPen $p $b; $g.DrawLines($ap, (Pts @(3, 19, 9, 19, 12, 27, 20, 27, 23, 19, 29, 19))); $ap.Dispose() }
    "icon-clash"        = { param($g, $p, $b)
        $g.FillRectangle($b, 13, 13, 6, 6); $g.DrawRectangle($p, 3, 3, 16, 16); $g.DrawRectangle($p, 13, 13, 16, 16) }
    "icon-clash-results" = { param($g, $p, $b)
        foreach ($y in 8, 16, 24) {
            $g.FillPolygon($b, (Pts @(3, ($y + 3), 7, ($y - 4), 11, ($y + 3)))); $g.DrawLine($p, 14, $y, 29, $y) } }
    "icon-compare"      = { param($g, $p, $b)
        $g.DrawRectangle($p, 3, 3, 15, 20); $g.FillRectangle($b, 17, 15, 9, 5); $g.DrawRectangle($p, 14, 9, 15, 20) }
    "icon-raise"        = { param($g, $p, $b)
        $g.FillRectangle($b, 3, 3, 26, 6); $g.DrawRectangle($p, 3, 3, 26, 6); $g.DrawLine($p, 3, 28, 29, 28)
        $g.DrawLine($p, 16, 25, 16, 15); $g.FillPolygon($b, (Pts @(10, 17, 22, 17, 16, 10))) }
    "icon-asset"        = { param($g, $p, $b)
        $g.DrawPolygon($p, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 3, 16), (New-Object System.Drawing.PointF 10, 7), (New-Object System.Drawing.PointF 29, 7), (New-Object System.Drawing.PointF 29, 25), (New-Object System.Drawing.PointF 10, 25)))
        $g.FillEllipse($b, 7.5, 12.5, 7, 7)
        $g.DrawLine($p, 17, 12, 25, 12); $g.DrawLine($p, 17, 16, 25, 16); $g.DrawLine($p, 17, 20, 22, 20) }
}

foreach ($name in $Icons.Keys) {
    if ($Only -and $Only -notcontains $name) { continue }
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

if ($Only) {
    Write-Host "Icons ready in $out"
    return
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
