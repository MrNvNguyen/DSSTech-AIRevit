# Cuts the brand logo (DSS over TECHNOLOGY on a white ground) into transparent PNGs for the installer and UI:
#   installer\brand\dss-logo.png  the whole logo, white ground made transparent, trimmed
#   installer\brand\dss-mark.png  the DSS letters only (the band above TECHNOLOGY), trimmed
param(
  [Parameter(Mandatory = $true)][string]$Logo,
  [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "installer\brand")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, WindowsBase
New-Item -ItemType Directory -Force $OutDir | Out-Null

$fs = [IO.File]::OpenRead((Resolve-Path $Logo).Path)
$frame = [System.Windows.Media.Imaging.BitmapDecoder]::Create($fs, 'None', 'OnLoad').Frames[0]
$fs.Close()
$src = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $frame, ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0
$w = $src.PixelWidth; $h = $src.PixelHeight; $stride = $w * 4
$px = New-Object byte[] ($stride * $h)
$src.CopyPixels($px, $stride, 0)

# White ground -> transparent: alpha from how far the darkest channel sits below the ground (~248), colour
# un-blended from white so anti-aliased edges keep their hue.
$ground = 248.0; $solid = 150.0
for ($i = 0; $i -lt $px.Length; $i += 4) {
  $b = $px[$i]; $g = $px[$i + 1]; $r = $px[$i + 2]
  $lo = [Math]::Min($r, [Math]::Min($g, $b))
  $a = [Math]::Max(0.0, [Math]::Min(1.0, ($ground - $lo) / ($ground - $solid)))
  if ($a -le 0) { $px[$i] = 0; $px[$i + 1] = 0; $px[$i + 2] = 0; $px[$i + 3] = 0; continue }
  foreach ($k in 0, 1, 2) {
    $c = ($px[$i + $k] - (1 - $a) * 255) / $a
    $px[$i + $k] = [byte][Math]::Max(0, [Math]::Min(255, [Math]::Round($c)))
  }
  $px[$i + 3] = [byte][Math]::Round($a * 255)
}

function RowHas([int]$y) { for ($x = 0; $x -lt $w; $x++) { if ($px[$y * $stride + $x * 4 + 3] -gt 40) { return $true } }; return $false }
function Bounds([int]$y0, [int]$y1) {
  $minX = $w; $maxX = -1; $minY = $h; $maxY = -1
  for ($y = $y0; $y -le $y1; $y++) { for ($x = 0; $x -lt $w; $x++) {
    if ($px[$y * $stride + $x * 4 + 3] -gt 40) { if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }; if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y } } } }
  return @($minX, $minY, $maxX, $maxY)
}
function Save([int[]]$bx, [string]$name) {
  $pad = 2
  $x0 = [Math]::Max(0, $bx[0] - $pad); $y0 = [Math]::Max(0, $bx[1] - $pad)
  $cw = [Math]::Min($w - 1, $bx[2] + $pad) - $x0 + 1; $ch = [Math]::Min($h - 1, $bx[3] + $pad) - $y0 + 1
  $bmp = [System.Windows.Media.Imaging.BitmapSource]::Create($w, $h, 96, 96, [System.Windows.Media.PixelFormats]::Bgra32, $null, $px, $stride)
  $crop = New-Object System.Windows.Media.Imaging.CroppedBitmap $bmp, (New-Object System.Windows.Int32Rect $x0, $y0, $cw, $ch)
  $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
  $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($crop))
  $out = [IO.File]::Create((Join-Path $OutDir $name)); $enc.Save($out); $out.Close()
  Write-Host "$name ${cw}x$ch"
}

# Bands of rows with ink: the first is DSS, the next TECHNOLOGY.
$bands = @(); $start = -1
for ($y = 0; $y -lt $h; $y++) {
  $has = RowHas $y
  if ($has -and $start -lt 0) { $start = $y }
  if (-not $has -and $start -ge 0) { $bands += , @($start, ($y - 1)); $start = -1 }
}
if ($start -ge 0) { $bands += , @($start, ($h - 1)) }
if ($bands.Count -eq 0) { throw "No ink found in $Logo" }
Save (Bounds $bands[0][0] $bands[$bands.Count - 1][1]) "dss-logo.png"
Save (Bounds $bands[0][0] $bands[0][1]) "dss-mark.png"
