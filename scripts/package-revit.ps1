param(
  [string]$Configuration = "Release",
  [string]$OutDir,
  [switch]$SkipBuild,
  [switch]$InstallLocal
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "revit-host.ps1")
$root = Split-Path -Parent $PSScriptRoot
$binRoot = Join-Path $root "src\QSNormEngine.Revit\bin\$Configuration"
$version = (Get-Content (Join-Path $root "VERSION") -ErrorAction SilentlyContinue | Select-Object -First 1).Trim()
if ([string]::IsNullOrWhiteSpace($version)) { $version = "0.0.0" }
$stamp = Get-Date -Format "yyyyMMdd-HHmm"
$pkgName = "DSSTech-Revit-v$version-$stamp"

if (-not $SkipBuild) {
  & (Join-Path $PSScriptRoot "build-revit.ps1") -Configuration $Configuration
}

$years = @(Get-ChildItem $binRoot -Directory -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -match '^20\d{2}$' } |
  ForEach-Object { [int]$_.Name } |
  Where-Object { $_ -ge $script:QsRevitYearMin -and $_ -le $script:QsRevitYearMax } |
  Sort-Object)
if ($years.Count -eq 0) {
  throw "Không có build output dưới $binRoot. Chạy build-revit.ps1 trước."
}

if (-not $OutDir) {
  $OutDir = Join-Path $root "dist"
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$stage = Join-Path $OutDir $pkgName
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$addinId = "7B52A0F1-7C61-4C8B-A6A4-0AC7398341A2"
$readme = @"
# DSSTech Revit Add-in v$version

Build: $stamp
Hosts: $($years -join ', ')

## Cài đặt (máy có Revit)

1. Đóng hết Revit.
2. Chạy ``Install-All.cmd`` (hoặc ``Install-YYYY.cmd`` cho một host).
3. Mở lại Revit → ribbon **DSSTech**.

Thủ công: copy thư mục ``Addins\<year>\QSNormEngine\`` và file ``QSNormEngine.addin``
vào ``%APPDATA%\Autodesk\Revit\Addins\<year>\``.

## Gỡ

Xóa ``%APPDATA%\Autodesk\Revit\Addins\<year>\QSNormEngine\`` và ``QSNormEngine.addin``.
"@
Set-Content -Path (Join-Path $stage "README.txt") -Value $readme -Encoding UTF8

foreach ($y in $years) {
  $src = Join-Path $binRoot "$y"
  $yearDir = Join-Path $stage "Addins\$y"
  $pluginDir = Join-Path $yearDir "QSNormEngine"
  New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
  Copy-Item (Join-Path $src "*") $pluginDir -Force -Recurse
  Remove-Item (Join-Path $pluginDir "QSNormEngine.Revit2026.dll") -ErrorAction SilentlyContinue
  # Drop PDBs from distributable (smaller; keep DLLs + Config/Data/Assets).
  Get-ChildItem $pluginDir -Filter "*.pdb" -Recurse | Remove-Item -Force

  # Template only — Install-Host.ps1 rewrites Assembly to an absolute path on install.
  # Revit does not expand %APPDATA% in .addin files.
  $assembly = "QSNormEngine\QSNormEngine.Revit.dll"
  $manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>DSSTech</Name>
    <Assembly>$assembly</Assembly>
    <AddInId>$addinId</AddInId>
    <FullClassName>QSNormEngine.Revit.App</FullClassName>
    <VendorId>NVNG</VendorId>
    <VendorDescription>DSSTech</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
  Set-Content -Path (Join-Path $yearDir "QSNormEngine.addin") -Value $manifest -Encoding UTF8

  $yearCmd = @"
@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Host.ps1" -Year $y
if errorlevel 1 ( echo FAILED & pause & exit /b 1 )
echo OK — restart Revit $y, open DSSTech.
pause
"@
  Set-Content -Path (Join-Path $stage "Install-$y.cmd") -Value $yearCmd -Encoding ASCII
}

$installHost = @'
param([Parameter(Mandatory)][int]$Year)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$srcYear = Join-Path $root "Addins\$Year"
if (!(Test-Path $srcYear)) { throw "Thiếu Addins\$Year trong gói." }
$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$Year"
$pluginDir = Join-Path $addinRoot "QSNormEngine"
$assembly = Join-Path $pluginDir "QSNormEngine.Revit.dll"
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item (Join-Path $srcYear "QSNormEngine\*") $pluginDir -Force -Recurse
if (!(Test-Path $assembly)) { throw "Thiếu DLL sau copy: $assembly" }
$manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>DSSTech</Name>
    <Assembly>$assembly</Assembly>
    <AddInId>7B52A0F1-7C61-4C8B-A6A4-0AC7398341A2</AddInId>
    <FullClassName>QSNormEngine.Revit.App</FullClassName>
    <VendorId>NVNG</VendorId>
    <VendorDescription>DSSTech</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
$manifestPath = Join-Path $addinRoot "QSNormEngine.addin"
Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8
Write-Host "Installed: $pluginDir"
Write-Host "Manifest: $manifestPath"
Write-Host "Assembly: $assembly"
'@
Set-Content -Path (Join-Path $stage "Install-Host.ps1") -Value $installHost -Encoding UTF8

$installAll = @"
@echo off
cd /d "%~dp0"
set FAILED=0
"@
foreach ($y in $years) {
  $installAll += "`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Install-Host.ps1`" -Year $y`nif errorlevel 1 set FAILED=1`n"
}
$installAll += @"
if %FAILED%==1 ( echo FAILED & pause & exit /b 1 )
echo OK — restart Revit, open DSSTech.
pause
"@
Set-Content -Path (Join-Path $stage "Install-All.cmd") -Value $installAll -Encoding ASCII

$zipPath = Join-Path $OutDir "$pkgName.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $stage -DestinationPath $zipPath -CompressionLevel Optimal

$dll = Get-Item (Join-Path $binRoot "$($years[0])\QSNormEngine.Revit.dll")
$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
$info = @"
package=$pkgName
version=$version
stamp=$stamp
hosts=$($years -join ',')
zip=$zipPath
zip_bytes=$((Get-Item $zipPath).Length)
zip_sha256=$hash
revit_dll_mtime=$($dll.LastWriteTime.ToString('o'))
"@
Set-Content -Path (Join-Path $OutDir "$pkgName.txt") -Value $info -Encoding UTF8

Write-Host "Staged: $stage"
Write-Host "Zip:    $zipPath"
Write-Host "SHA256: $hash"
Write-Host "Bytes:  $((Get-Item $zipPath).Length)"

if ($InstallLocal) {
  & (Join-Path $PSScriptRoot "install-revit.ps1") -Configuration $Configuration
}

Write-Output $zipPath
