param(
  [string]$RevitApiPath,
  [string]$Framework,
  [string]$Configuration = "Release",
  [int[]]$Year
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "revit-host.ps1")
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\DSSTech.Revit\DSSTech.Revit.csproj"

$targets = @()
if ($RevitApiPath) {
  if (!(Test-Path (Join-Path $RevitApiPath "RevitAPI.dll"))) { throw "Không tìm thấy RevitAPI.dll tại $RevitApiPath" }
  $inferred = Get-QsRevitYearFromPath $RevitApiPath
  if ($Year -and $Year.Count -eq 1) { $inferred = $Year[0] }
  if ($null -eq $inferred) { throw "Không suy ra được năm host từ đường dẫn. Hãy truyền -Year." }
  $targets += [pscustomobject]@{ Year = $inferred; ApiPath = $RevitApiPath }
} elseif ($Year) {
  $installed = @(Get-QsInstalledRevitHosts)
  foreach ($y in $Year) {
    $hit = @($installed | Where-Object { $_.Year -eq $y } | Select-Object -First 1)
    if ($hit.Count -eq 0) { throw "Không tìm thấy Revit $y (RevitAPI.dll) trên máy này." }
    $targets += $hit[0]
  }
} else {
  $targets = @(Get-QsInstalledRevitHosts)
  if ($targets.Count -eq 0) {
    throw "Khong tim thay Revit $($script:QsRevitYearMin)-$($script:QsRevitYearMax). Cai Revit hoac truyen -RevitApiPath / -Year."
  }
}

$sidecarProj = Join-Path $root "src\Dss.RevitMcp\Dss.RevitMcp.csproj"
$sidecarDist = Join-Path $root "dist\mcp-sidecar"
Write-Host "Publishing MCP sidecar (net8.0) -> $sidecarDist"
dotnet publish $sidecarProj -c $Configuration -f net8.0 -o $sidecarDist
if ($LASTEXITCODE -ne 0) { throw "Sidecar publish failed." }

$sidecarRequired = @(
  "Dss.RevitMcp.exe",
  "Dss.RevitMcp.dll",
  "Dss.RevitMcp.deps.json",
  "Dss.RevitMcp.runtimeconfig.json",
  "Dss.McpContracts.dll"
)
foreach ($f in $sidecarRequired) {
  if (!(Test-Path (Join-Path $sidecarDist $f))) { throw "Sidecar publish missing: $f" }
}
$sidecarOptional = @("System.Text.Json.dll")
$sidecarFiles = @($sidecarRequired + $sidecarOptional)

foreach ($t in $targets) {
  $tfm = if ($Framework) { $Framework } else { Get-QsRevitTargetFramework $t.Year }
  Write-Host "Building host $($t.Year)  TFM=$tfm  API=$($t.ApiPath)"
  dotnet build $proj -c $Configuration `
    /p:RevitApiPath="$($t.ApiPath)" `
    /p:RevitTargetFramework="$tfm" `
    /p:RevitHostLabel="$($t.Year)" `
    /p:RevitHostYear="$($t.Year)"
  if ($LASTEXITCODE -ne 0) { throw "Build failed for host $($t.Year) ($tfm)." }

  $hostOut = Join-Path $root "src\DSSTech.Revit\bin\$Configuration\$($t.Year)"
  New-Item -ItemType Directory -Path $hostOut -Force | Out-Null
  foreach ($f in $sidecarFiles) {
    $from = Join-Path $sidecarDist $f
    if (Test-Path $from) { Copy-Item $from $hostOut -Force }
  }
}
Write-Host "Build completed for years: $($targets.Year -join ', ')"
