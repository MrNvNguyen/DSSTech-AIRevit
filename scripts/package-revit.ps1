param(
  [string]$Configuration = "Release",
  [string]$OutDir,
  [switch]$SkipBuild,
  [switch]$InstallLocal
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "revit-host.ps1")
$root = Split-Path -Parent $PSScriptRoot
$binRoot = Join-Path $root "src\DSSTech.Revit\bin\$Configuration"
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
  throw "Không có build output 2025+ dưới $binRoot. Chạy build-revit.ps1 -Year 2025 trước."
}

if (-not $OutDir) {
  $OutDir = Join-Path $root "dist"
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$stage = Join-Path $OutDir $pkgName
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$addinId = "7B52A0F1-7C61-4C8B-A6A4-0AC7398341A2"
# The host CLR must match the TFM each year was built for (2025.5+ runs .NET 10, 2025.0–2025.4 runs .NET 8).
$hostLines = foreach ($y in $years) {
  $deps = Join-Path $binRoot "$y\DSSTech.deps.json"
  $target = if (Test-Path $deps) { (Get-Content $deps -Raw | ConvertFrom-Json).runtimeTarget.name } else { "" }
  $net = if ($target -match 'Version=v(\d+\.\d+)') { ".NET $($Matches[1])" } else { "?" }
  "- Revit ${y}: build $net"
}
$readme = @"
# DSSTech Revit Add-in v$version

Build: $stamp
Hosts: $($years -join ', ')

$($hostLines -join "`r`n")

Revit 2025 bản .NET 10 chỉ chạy trên Revit 2025.5 trở lên (Help → About). Máy test còn 2025.0–2025.4 thì cập nhật Revit trước.

## Cài đặt (máy có Revit)

1. Đóng hết Revit.
2. Chạy ``Install-All.cmd`` (hoặc ``Install-YYYY.cmd`` cho một host).
3. Mở lại Revit → ribbon **DSSTech**.

## Test dựng Cột / Vách / Dầm (Dựng mô hình · Model Revit)

1. Mở project có Level và Grid (thiếu Grid: CAD → BIM → Trục & tầng).
2. Link DWG mặt bằng kết cấu, Move/Rotate cho khớp trục model.
3. Mở view mặt bằng của tầng cần dựng.
4. DSSTech → CAD → BIM → **Dựng mô hình**:
   - Cột: chọn family cột, level đáy/đỉnh, offset → pick nét trên layer cột. Tiết diện đo từ outline, Mark đọc chữ C1/C5 gần cột.
   - Vách: chọn Tường kết cấu, type gốc → bề dày đo từ cặp nét song song (L/b > 4).
   - Dầm: chọn family dầm, level đỉnh → Mark/tiết diện đọc từ chữ DWG (vd. D1Y19-40x60). Nên dựng cột và vách trước để dầm có gối.
5. Kiểm tra hộp tổng kết sau khi dựng. Một lần Undo hoàn tác cả lô.

Lỗi gửi lại: ảnh hộp thông báo và file log ``%LOCALAPPDATA%\DSSTech\mcp-host.log`` (log chung của add-in).

Thủ công: copy ``Addins\<year>\DSSTech\`` và ``DSSTech.addin`` vào ``%APPDATA%\Autodesk\Revit\Addins\<year>\``.

## Gỡ

Xóa ``%APPDATA%\Autodesk\Revit\Addins\<year>\DSSTech\`` và ``DSSTech.addin`` (và ``QSNormEngine.addin`` cũ nếu còn).

Cài mới tự gỡ manifest ``QSNormEngine.addin`` trùng AddInId (tránh lỗi Duplicated AddInId).
"@
Set-Content -Path (Join-Path $stage "README.txt") -Value $readme -Encoding UTF8

foreach ($y in $years) {
  $src = Join-Path $binRoot "$y"
  $yearDir = Join-Path $stage "Addins\$y"
  $pluginDir = Join-Path $yearDir "DSSTech"
  New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
  Copy-Item (Join-Path $src "*") $pluginDir -Force -Recurse
  Remove-Item (Join-Path $pluginDir "QSNormEngine.Revit2026.dll") -ErrorAction SilentlyContinue
  Remove-Item (Join-Path $pluginDir "QSNormEngine.Revit.dll") -ErrorAction SilentlyContinue
  # Drop PDBs from distributable (smaller; keep DLLs + Config/Data/Assets).
  Get-ChildItem $pluginDir -Filter "*.pdb" -Recurse | Remove-Item -Force

  # Template only — Install-Host.ps1 rewrites Assembly to an absolute path on install.
  # Revit does not expand %APPDATA% in .addin files.
  $assembly = "DSSTech\DSSTech.dll"
  $manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>DSSTech</Name>
    <Assembly>$assembly</Assembly>
    <AddInId>$addinId</AddInId>
    <FullClassName>DSSTech.Revit.App</FullClassName>
    <VendorId>NVNG</VendorId>
    <VendorDescription>DSSTech</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
  Set-Content -Path (Join-Path $yearDir "DSSTech.addin") -Value $manifest -Encoding UTF8

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
$guid = '7B52A0F1-7C61-4C8B-A6A4-0AC7398341A2'
foreach ($legacyName in @('QSNormEngine.addin', 'DSS.QSEngine.addin')) {
  $legacyPath = Join-Path $addinRoot $legacyName
  if (Test-Path $legacyPath) { Remove-Item $legacyPath -Force }
}
foreach ($f in Get-ChildItem $addinRoot -Filter '*.addin' -File -ErrorAction SilentlyContinue) {
  if ($f.Name -ieq 'DSSTech.addin') { continue }
  $raw = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue
  if ($raw -and ($raw -match [regex]::Escape($guid))) { Remove-Item $f.FullName -Force }
}
$legacyDir = Join-Path $addinRoot 'QSNormEngine'
if (Test-Path $legacyDir) { Remove-Item $legacyDir -Recurse -Force }
$pluginDir = Join-Path $addinRoot "DSSTech"
$assembly = Join-Path $pluginDir "DSSTech.dll"
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item (Join-Path $srcYear "DSSTech\*") $pluginDir -Force -Recurse
if (!(Test-Path $assembly)) { throw "Thiếu DLL sau copy: $assembly" }
$manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>DSSTech</Name>
    <Assembly>$assembly</Assembly>
    <AddInId>7B52A0F1-7C61-4C8B-A6A4-0AC7398341A2</AddInId>
    <FullClassName>DSSTech.Revit.App</FullClassName>
    <VendorId>NVNG</VendorId>
    <VendorDescription>DSSTech</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
$manifestPath = Join-Path $addinRoot "DSSTech.addin"
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
# Windows PowerShell 5.1 Compress-Archive can return without writing the zip and without an error.
if (!(Test-Path $zipPath)) { throw "Compress-Archive không tạo được $zipPath. Chạy lại với -SkipBuild." }

$dll = Get-Item (Join-Path $binRoot "$($years[0])\DSSTech.dll")
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
