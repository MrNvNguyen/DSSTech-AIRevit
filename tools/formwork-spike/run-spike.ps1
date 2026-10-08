param(
  [int]$Year = 2025,
  [string]$Configuration = "Release",
  [string]$Framework,
  [switch]$Install,
  [switch]$NoBuild
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\..\scripts\revit-host.ps1")

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$proj = Join-Path $PSScriptRoot "FormworkSpike.AddIn\FormworkSpike.csproj"
$installed = @(Get-QsInstalledRevitHosts)
$hostInfo = @($installed | Where-Object { $_.Year -eq $Year } | Select-Object -First 1)
if ($hostInfo.Count -eq 0) {
  throw "Revit $Year not found. Install Revit or pass -Year for an installed host ($($script:QsRevitYearMin)-$($script:QsRevitYearMax))."
}

$tfm = if ($Framework) { $Framework } else { Get-QsRevitTargetFramework $Year }
if (-not $NoBuild) {
  Write-Host "Building FormworkSpike for Revit $Year (TFM=$tfm)"
  dotnet build $proj -c $Configuration `
    /p:RevitApiPath="$($hostInfo.ApiPath)" `
    /p:RevitTargetFramework="$tfm" `
    /p:RevitHostLabel="$Year"
  if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$outDir = Join-Path $PSScriptRoot "FormworkSpike.AddIn\bin\$Configuration\$Year"
if (!(Test-Path $outDir)) { throw "Missing build output: $outDir" }

if ($Install) {
  $addinRoot = Join-Path $env:ProgramData "Autodesk\Revit\Addins\$Year"
  $pluginDir = Join-Path $addinRoot "FormworkSpike"
  New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
  Copy-Item (Join-Path $outDir "*") $pluginDir -Force -Recurse

  # So the installed DLL can find the evidence folder (ProgramData is not the repo).
  Set-Content -Path (Join-Path $pluginDir "repo-root.txt") -Value $root.Path -Encoding UTF8

  $assembly = Join-Path $pluginDir "FormworkSpike.AddIn.dll"
  $template = Join-Path $PSScriptRoot "addins\FormworkSpike.$Year.addin"
  if (!(Test-Path $template)) { throw "Missing addin template: $template" }
  $manifest = Get-Content $template -Raw
  $manifest = $manifest.Replace("__ASSEMBLY__", $assembly)
  $manifestPath = Join-Path $addinRoot "FormworkSpike.addin"
  Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8
  Write-Host "Installed plugin: $pluginDir"
  Write-Host "Manifest: $manifestPath"
}

Write-Host "Output: $outDir"
Write-Host "Next: open Revit $Year, load pilot model, run Add-Ins > External Tools > Formwork Spike N0"
