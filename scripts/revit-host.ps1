# Host discovery for Autodesk Revit. Product code is version-agnostic;
# Autodesk still requires Addins\<year> and a matching .NET TFM at compile time.

$script:QsRevitYearMin = 2024
$script:QsRevitYearMax = 2027

function Get-QsRevitYearFromPath([string]$ApiPath) {
  if ([string]::IsNullOrWhiteSpace($ApiPath)) { return $null }
  if ($ApiPath -match 'Revit\s+(20\d{2})') { return [int]$Matches[1] }
  return $null
}

function Get-QsRevitTargetFramework([int]$Year) {
  switch ($Year) {
    2024 { "net48" }
    2025 { "net8.0-windows" }
    2026 { "net8.0-windows" }  # 2026.5+ (.NET 10): pass -Framework net10.0-windows
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
