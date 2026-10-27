param(
  [string]$Configuration = "Release",
  [int[]]$Year
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "revit-host.ps1")
$root = Split-Path -Parent $PSScriptRoot
$outRoot = Join-Path $root "src\DSSTech.Revit\bin\$Configuration"

$years = @()
if ($Year) {
  $years = @($Year)
} else {
  $years = @(Get-ChildItem $outRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^20\d{2}$' } |
    ForEach-Object { [int]$_.Name } |
    Where-Object { $_ -ge $script:QsRevitYearMin -and $_ -le $script:QsRevitYearMax })
}
if ($years.Count -eq 0) { throw "Chưa có build output dưới $outRoot. Hãy chạy scripts\build-revit.ps1 trước." }

foreach ($y in $years) {
  $source = Join-Path $outRoot "$y"
  if (!(Test-Path $source)) { throw "Chưa có build output: $source. Hãy chạy scripts\build-revit.ps1 -Year $y trước." }
  $addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$y"
  Remove-DssTechLegacyAddInRegistration -AddinRoot $addinRoot
  $pluginDir = Join-Path $addinRoot "DSSTech"
  New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
  Copy-Item (Join-Path $source "*") $pluginDir -Force -Recurse
  $sidecarFiles = @(
    "Dss.RevitMcp.exe",
    "Dss.RevitMcp.deps.json",
    "Dss.McpContracts.dll",
    "System.Text.Json.dll"
  )
  foreach ($f in $sidecarFiles) {
    $from = Join-Path $source $f
    if (Test-Path $from) {
      Copy-Item $from (Join-Path $pluginDir $f) -Force
    } else {
      Write-Warning "Host $y`: thieu sidecar $f trong $source. Chay lai build-revit.ps1."
    }
  }
  Remove-Item (Join-Path $pluginDir "QSNormEngine.Revit2026.dll") -ErrorAction SilentlyContinue
  Remove-Item (Join-Path $pluginDir "QSNormEngine.Revit.dll") -ErrorAction SilentlyContinue
  $assetCount = @(Get-ChildItem (Join-Path $pluginDir "Assets\*.png") -ErrorAction SilentlyContinue).Count
  if ($assetCount -lt 6) {
    Write-Warning "Host $y`: thiếu ribbon icons trong Assets (tim thay $assetCount PNG). Chay lai build-revit.ps1."
  }
  $assembly = Join-Path $pluginDir "DSSTech.dll"
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
  Write-Host "Installed to: $pluginDir"
  Write-Host "Manifest: $manifestPath"
}
