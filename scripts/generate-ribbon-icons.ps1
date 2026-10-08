# Generate DSSTech ribbon icons (16px + 32px PNG).
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\QSNormEngine.Revit\Assets"
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Save-Icon {
    param([string]$Name, [int]$Size, [scriptblock]$Draw)
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::FromArgb(255, 248, 250, 252))
    & $Draw $g $Size
    $path = Join-Path $out "$Name-$Size.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "Wrote $path"
}

function Get-Font($size) {
    New-Object System.Drawing.Font("Segoe UI", $size, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
}

Save-Icon "icon-norm" 32 {
    param($g, $s)
    $blue = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)
    $pen = New-Object System.Drawing.Pen $blue, 2
    $g.FillRectangle([System.Drawing.Brushes]::White, 4, 5, 18, 22)
    $g.DrawRectangle($pen, 4, 5, 18, 22)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), 16, 14, 14, 14)
    $g.DrawString("DM", (Get-Font 7), [System.Drawing.Brushes]::White, 18.5, 17.5)
    $pen.Dispose()
}
Save-Icon "icon-norm" 16 {
    param($g, $s)
    $blue = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)
    $g.FillRectangle([System.Drawing.Brushes]::White, 2, 2, 9, 11)
    $g.DrawRectangle((New-Object System.Drawing.Pen $blue, 1), 2, 2, 9, 11)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $blue), 8, 7, 6, 6)
    $g.DrawString("D", (Get-Font 5), [System.Drawing.Brushes]::White, 9.2, 7.5)
}

Save-Icon "icon-boq" 32 {
    param($g, $s)
    $green = [System.Drawing.Color]::FromArgb(255, 5, 150, 105)
    $pen = New-Object System.Drawing.Pen $green, 2
    $gridPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 5, 150, 105)), 1
    $g.FillRectangle([System.Drawing.Brushes]::White, 3, 6, 20, 20)
    $g.DrawRectangle($pen, 3, 6, 20, 20)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $green), 3, 6, 20, 5)
    $g.DrawLine($gridPen, 5, 14, 21, 14)
    $g.DrawLine($gridPen, 5, 18, 21, 18)
    $g.DrawLine($gridPen, 5, 22, 21, 22)
    $g.DrawLine($gridPen, 11, 11, 11, 25)
    $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 16, 185, 129))
    $g.FillRectangle($b, 22, 12, 8, 14)
    $g.FillRectangle($b, 24, 8, 4, 4)
    $g.DrawString("Q", (Get-Font 8), [System.Drawing.Brushes]::White, 6, 8)
    $pen.Dispose(); $gridPen.Dispose(); $b.Dispose()
}
Save-Icon "icon-boq" 16 {
    param($g, $s)
    $green = [System.Drawing.Color]::FromArgb(255, 5, 150, 105)
    $g.FillRectangle([System.Drawing.Brushes]::White, 1, 3, 10, 10)
    $g.DrawRectangle((New-Object System.Drawing.Pen $green, 1), 1, 3, 10, 10)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $green), 1, 3, 10, 3)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $green), 11, 5, 4, 8)
}

Save-Icon "icon-params" 32 {
    param($g, $s)
    $amber = [System.Drawing.Color]::FromArgb(255, 217, 119, 6)
    $brush = New-Object System.Drawing.SolidBrush $amber
    $g.FillEllipse($brush, 3, 4, 18, 18)
    $g.FillEllipse([System.Drawing.Brushes]::White, 10, 11, 6, 6)
    $g.FillRectangle($brush, 18, 18, 12, 10)
    $g.DrawString("QS", (Get-Font 6), [System.Drawing.Brushes]::White, 19.5, 19)
    $brush.Dispose()
}
Save-Icon "icon-params" 16 {
    param($g, $s)
    $amber = [System.Drawing.Color]::FromArgb(255, 217, 119, 6)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $amber), 1, 1, 9, 9)
    $g.FillEllipse([System.Drawing.Brushes]::White, 4, 4, 3, 3)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $amber), 9, 9, 6, 5)
    $g.DrawString("Q", (Get-Font 5), [System.Drawing.Brushes]::White, 10.5, 9)
}

Write-Host "Ribbon icons ready in $out"
