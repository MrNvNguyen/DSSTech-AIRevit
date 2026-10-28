param(
  [int]$Year = 2025,
  [string]$Configuration = "Release",
  [string]$Framework,
  [switch]$Install,
  [switch]$DevLink,
  [switch]$NoBuild
)
# Usage (from repo root):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\formwork-n1\run-n1-measure.ps1 -Year 2025 -Install
# Or double-click: tools\formwork-n1\install-n1.cmd
$ErrorActionPreference = "Stop"
$PSScriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }

$hostScript = Join-Path $PSScriptRoot "..\..\scripts\revit-host.ps1"
if (!(Test-Path $hostScript)) { throw "Missing scripts/revit-host.ps1 (run from repo tools/formwork-n1)." }
. $hostScript

$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$proj = Join-Path $PSScriptRoot "FormworkN1.AddIn\FormworkN1.csproj"
$installed = @(Get-QsInstalledRevitHosts)
$hostInfo = @($installed | Where-Object { $_.Year -eq $Year } | Select-Object -First 1)
if ($hostInfo.Count -eq 0) {
  $years = ($installed | ForEach-Object { $_.Year }) -join ", "
  throw "Revit $Year not found. Installed hosts: [$years]. Pass -Year <installed>."
}

$tfm = if ($Framework) { $Framework } else { Get-QsRevitTargetFramework $Year $hostInfo[0].ApiPath }
if (-not $NoBuild) {
  Write-Host "Building DSSTech.Core + FormworkN1 harness for Revit $Year (TFM=$tfm)"
  # Core is restored via ProjectReference. Do not build DSSTech.Revit into the harness.
  & dotnet build $proj -c $Configuration `
    /p:RevitApiPath="$($hostInfo.ApiPath)" `
    /p:RevitTargetFramework="$tfm" `
    /p:RevitHostLabel="$Year"
  if ($LASTEXITCODE -ne 0) { throw "Harness build failed." }
}

$outDir = Join-Path $PSScriptRoot "deploy\$Year"
if (!(Test-Path $outDir)) { throw "Missing build output: $outDir" }

# Always stamp repo root next to build output (DevLink + ProgramData fallback).
Set-Content -Path (Join-Path $outDir "repo-root.txt") -Value $root -Encoding UTF8

function Write-FormworkN1Manifest([string]$AssemblyPath, [string]$AddinRoot, [int]$YearNum) {
  $template = Join-Path $PSScriptRoot "addins\FormworkN1.$YearNum.addin"
  if (!(Test-Path $template)) { throw "Missing addin template: $template" }
  $manifest = (Get-Content $template -Raw).Replace("__ASSEMBLY__", $AssemblyPath)
  New-Item -ItemType Directory -Path $AddinRoot -Force | Out-Null
  $manifestPath = Join-Path $AddinRoot "FormworkN1.addin"
  Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8
  return $manifestPath
}

if ($Install -or $DevLink) {
  $addinRoot = Join-Path $env:ProgramData "Autodesk\Revit\Addins\$Year"
  $pluginDir = Join-Path $addinRoot "FormworkN1"
  $useDevLink = [bool]$DevLink

  if (-not $useDevLink) {
    try {
      New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
      Copy-Item (Join-Path $outDir "*") $pluginDir -Force -Recurse
      # Harness is self-contained (Formwork sources linked). Drop a leftover product DLL from the old name.
      Get-ChildItem $pluginDir -Filter "QSNormEngine.Revit*" -ErrorAction SilentlyContinue | Remove-Item -Force

      Set-Content -Path (Join-Path $pluginDir "repo-root.txt") -Value $root -Encoding UTF8
      $assembly = Join-Path $pluginDir "FormworkN1.AddIn.dll"
      $manifestPath = Write-FormworkN1Manifest $assembly $addinRoot $Year
      Write-Host "Installed plugin: $pluginDir"
      Write-Host "Manifest: $manifestPath"
    }
    catch {
      Write-Warning "ProgramData copy failed (Revit often locks DLLs). Falling back to -DevLink."
      Write-Warning $_.Exception.Message
      $useDevLink = $true
    }
  }

  if ($useDevLink) {
    # Point .addin at repo build output — close/restart Revit after rebuild.
    Get-ChildItem $outDir -Filter "QSNormEngine.Revit*" -ErrorAction SilentlyContinue | Remove-Item -Force
    $assembly = Join-Path $outDir "FormworkN1.AddIn.dll"
    if (!(Test-Path $assembly)) { throw "Missing harness DLL: $assembly" }
    $manifestPath = Write-FormworkN1Manifest $assembly $addinRoot $Year
    Write-Host "DevLink manifest: $manifestPath"
    Write-Host "Assembly: $assembly"
    Write-Host "NOTE: Close Revit completely before rebuild; restart Revit to load new DLL."
  }
}
elseif (-not $Install) {
  Write-Host "Build only. To register the External Tool, re-run with -Install (or -DevLink)."
}

Write-Host "Output: $outDir"
Write-Host "Next: Restart Revit $Year → open pilot model → Add-Ins > External Tools > Formwork N1 Measure"
