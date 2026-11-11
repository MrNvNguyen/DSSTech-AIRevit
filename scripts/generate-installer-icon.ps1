# Builds the installer icon (Programs and Features) from the brand mark: the DSS letters of the DSS Technology logo
# (installer\brand\dss-mark.png, cut by extract-brand-mark.ps1) centred on a white rounded tile, so it reads on light
# and dark Windows themes. Multi-size .ico with PNG entries.
param(
  [string]$Mark = (Join-Path (Split-Path -Parent $PSScriptRoot) "installer\brand\dss-mark.png"),
  [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) "installer\DSSTech.Revit.Installer\DSSTech.ico")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$markImage = [System.Drawing.Image]::FromFile((Resolve-Path $Mark).Path)

function New-Png([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)

  # White tile, rounded ~18 %, with a hairline edge from 48 px up.
  $r = [Math]::Max(2, [int]($size * 0.18)); $d = 2 * $r; $w = $size - 1
  $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
  $tile.AddArc(0, 0, $d, $d, 180, 90); $tile.AddArc($w - $d, 0, $d, $d, 270, 90)
  $tile.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $tile.AddArc(0, $w - $d, $d, $d, 90, 90); $tile.CloseFigure()
  $g.FillPath([System.Drawing.Brushes]::White, $tile)
  if ($size -ge 48) { $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 0, 0, 0)), 1), $tile) }

  # The DSS mark across the tile (small sizes use more of the width to stay legible).
  $fill = if ($size -le 24) { 0.96 } else { 0.86 }
  $mw = $size * $fill; $mh = $mw * $markImage.Height / $markImage.Width
  $g.DrawImage($markImage, [single](($size - $mw) / 2), [single](($size - $mh) / 2), [single]$mw, [single]$mh)
  $g.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  return , $ms.ToArray()
}

$images = $sizes | ForEach-Object { , (New-Png $_) }
$markImage.Dispose()
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
  $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
  $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
  $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Dispose()
Write-Host "Icon: $Out ($($sizes -join ', ') px)"
