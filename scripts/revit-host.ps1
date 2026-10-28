# Host discovery for Autodesk Revit. Product code is version-agnostic;
# Autodesk still requires Addins\<year> and a matching .NET TFM at compile time.

$script:QsRevitYearMin = 2025
$script:QsRevitYearMax = 2027
$script:DssTechAddInId = '7B52A0F1-7C61-4C8B-A6A4-0AC7398341A2'

# One .addin / one GUID — remove the old QSNormEngine.addin before install.
function Remove-DssTechLegacyAddInRegistration {
  param([Parameter(Mandatory)][string]$AddinRoot)
  if (!(Test-Path $AddinRoot)) { return }
  $guid = $script:DssTechAddInId
  foreach ($name in @('QSNormEngine.addin', 'DSS.QSEngine.addin')) {
    $p = Join-Path $AddinRoot $name
    if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
  }
  foreach ($f in Get-ChildItem $AddinRoot -Filter '*.addin' -File -ErrorAction SilentlyContinue) {
    if ($f.Name -ieq 'DSSTech.addin') { continue }
    $raw = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue
    if ($raw -and ($raw -match [regex]::Escape($guid))) {
      Remove-Item $f.FullName -Force -ErrorAction SilentlyContinue
    }
  }
  $legacyDir = Join-Path $AddinRoot 'QSNormEngine'
  if (Test-Path $legacyDir) { Remove-Item $legacyDir -Recurse -Force -ErrorAction SilentlyContinue }
}

function Get-QsRevitYearFromPath([string]$ApiPath) {
  if ([string]::IsNullOrWhiteSpace($ApiPath)) { return $null }
  if ($ApiPath -match 'Revit\s+(20\d{2})') { return [int]$Matches[1] }
  return $null
}

function Get-QsRevitTargetFramework([int]$Year, [string]$ApiPath) {
  # Updates move hosts across runtimes (2025.5 / 2026.5 ship .NET 10), so trust the
  # installed RevitAPI.runtimeconfig.json over the year table when it exists.
  if ($ApiPath) {
    $cfg = Join-Path $ApiPath "RevitAPI.runtimeconfig.json"
    if (Test-Path $cfg) {
      try {
        $tfm = (Get-Content $cfg -Raw | ConvertFrom-Json).runtimeOptions.tfm
        if ($tfm -match '^net\d+\.\d+$') { return "$tfm-windows" }
      } catch { }
    }
  }
  switch ($Year) {
    2025 { "net8.0-windows" }
    2026 { "net8.0-windows" }
    2027 { "net10.0-windows" }
    default { throw "Host year $Year is outside supported range $($script:QsRevitYearMin)-$($script:QsRevitYearMax)." }
  }
}

function Get-QsInstalledRevitHosts {
  $roots = @(${env:ProgramFiles}, ${env:ProgramW6432}, ${env:ProgramFiles(x86)}) |
    Where-Object { $_ } |
    Select-Object -Unique
  $found = foreach ($root in $roots) {
    $autodesk = Join-Path $root "Autodesk"
    if (!(Test-Path $autodesk)) { continue }
    Get-ChildItem $autodesk -Directory -ErrorAction SilentlyContinue | ForEach-Object {
      if ($_.Name -notmatch '^Revit (20\d{2})$') { return }
      $year = [int]$Matches[1]
      if ($year -lt $script:QsRevitYearMin -or $year -gt $script:QsRevitYearMax) { return }
      if (Test-Path (Join-Path $_.FullName "RevitAPI.dll")) {
        [pscustomobject]@{ Year = $year; ApiPath = $_.FullName }
      }
    }
  }
  @($found) | Sort-Object Year | Group-Object Year | ForEach-Object { $_.Group[0] }
}
