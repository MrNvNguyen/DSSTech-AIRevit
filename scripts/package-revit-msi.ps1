param(
  [string]$Configuration = "Release",
  [string]$OutDir,
  [int[]]$Year,
  [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "revit-host.ps1")

$root = Split-Path -Parent $PSScriptRoot
$binRoot = Join-Path $root "src\DSSTech.Revit\bin\$Configuration"
$installerDir = Join-Path $root "installer\DSSTech.Revit.Installer"
$wixproj = Join-Path $installerDir "DSSTech.Revit.Installer.wixproj"
$addinId = $script:DssTechAddInId

$version = (Get-Content (Join-Path $root "VERSION") -ErrorAction SilentlyContinue | Select-Object -First 1).Trim()
if ([string]::IsNullOrWhiteSpace($version)) { $version = "0.0.0" }
# WiX ProductVersion is major.minor.build[.revision] with each part <= 65535
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
  throw "VERSION must be numeric x.y.z for MSI (got '$version')."
}

if (-not $SkipBuild) {
  $buildArgs = @{ Configuration = $Configuration }
  if ($Year) { $buildArgs.Year = $Year }
  & (Join-Path $PSScriptRoot "build-revit.ps1") @buildArgs
}

$years = if ($Year) {
  @($Year)
} else {
  @(Get-ChildItem $binRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^20\d{2}$' -and (Test-Path (Join-Path $_.FullName 'DSSTech.dll')) } |
    ForEach-Object { [int]$_.Name } |
    Where-Object { $_ -ge $script:QsRevitYearMin -and $_ -le $script:QsRevitYearMax } |
    Sort-Object)
}
$years = @($years | Where-Object { $_ -ge 2025 } | Select-Object -Unique)
if ($years.Count -eq 0) {
  throw "Không có build output 2025+ dưới $binRoot. Chạy build-revit.ps1 trước."
}
foreach ($y in $years) {
  $dll = Join-Path $binRoot "$y\DSSTech.dll"
  if (!(Test-Path $dll)) { throw "Thiếu $dll" }
}

if (-not $OutDir) { $OutDir = Join-Path $root "dist" }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmm"
$pkgName = "DSSTech-Revit-v$version-$stamp"
$stageRoot = Join-Path $OutDir "$pkgName-msi-stage"
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

function New-DeterministicGuid([string]$key) {
  $md5 = [System.Security.Cryptography.MD5]::Create()
  try {
    $bytes = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("DSSTech.MSI.v1|$key"))
  } finally { $md5.Dispose() }
  # UUID version 4 variant bits
  $bytes[7] = ($bytes[7] -band 0x0F) -bor 0x40
  $bytes[8] = ($bytes[8] -band 0x3F) -bor 0x80
  return ([guid]::new($bytes)).ToString().ToUpperInvariant()
}

function Get-WiXId([string]$prefix, [string]$key) {
  $hash = New-DeterministicGuid $key
  $compact = $hash.Replace("-", "")
  # WiX Id max ~72; keep readable prefix + hash
  return ($prefix + $compact.Substring(0, 16))
}

$manifestTemplate = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>DSSTech</Name>
    <Assembly>DSSTech\DSSTech.dll</Assembly>
    <AddInId>$addinId</AddInId>
    <FullClassName>DSSTech.Revit.App</FullClassName>
    <VendorId>NVNG</VendorId>
    <VendorDescription>DSSTech</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

$componentXml = New-Object System.Collections.Generic.List[string]
$dirXml = New-Object System.Collections.Generic.List[string]
$dirXml.Add('  <Fragment>')
$dirXml.Add('    <DirectoryRef Id="AddinsFolder">')

foreach ($y in $years) {
  $src = Join-Path $binRoot "$y"
  $yearStage = Join-Path $stageRoot "$y"
  $pluginStage = Join-Path $yearStage "DSSTech"
  New-Item -ItemType Directory -Path $pluginStage -Force | Out-Null
  Copy-Item (Join-Path $src "*") $pluginStage -Force -Recurse
  Remove-Item (Join-Path $pluginStage "QSNormEngine.Revit2026.dll") -ErrorAction SilentlyContinue
  Remove-Item (Join-Path $pluginStage "QSNormEngine.Revit.dll") -ErrorAction SilentlyContinue
  Get-ChildItem $pluginStage -Filter "*.pdb" -Recurse | Remove-Item -Force
  Set-Content -Path (Join-Path $yearStage "DSSTech.addin") -Value $manifestTemplate -Encoding UTF8

  $yearDirId = "Addins$y"
  $pluginDirId = "Plugin$y"
  $dirXml.Add("      <Directory Id=`"$yearDirId`" Name=`"$y`">")
  $dirXml.Add("        <Directory Id=`"$pluginDirId`" Name=`"DSSTech`" />")
  $dirXml.Add("      </Directory>")

  # Manifest component (next to plugin folder)
  $addinSrc = Join-Path $yearStage "DSSTech.addin"
  $addinGuid = New-DeterministicGuid "addin|$y"
  $addinFileId = Get-WiXId "fAddin" "addin|$y"
  $addinCompId = Get-WiXId "cAddin" "addin|$y"
  $componentXml.Add("    <Component Id=`"$addinCompId`" Directory=`"$yearDirId`" Guid=`"$addinGuid`">")
  $componentXml.Add("      <File Id=`"$addinFileId`" Source=`"$addinSrc`" KeyPath=`"yes`" />")
  $componentXml.Add("    </Component>")

  $files = Get-ChildItem $pluginStage -Recurse -File | Sort-Object FullName
  foreach ($file in $files) {
    $rel = $file.FullName.Substring($pluginStage.Length).TrimStart('\', '/')
    $relKey = "$y|$rel".Replace('\', '/')
    $compGuid = New-DeterministicGuid "file|$relKey"
    $fileId = Get-WiXId "f" $relKey
    $compId = Get-WiXId "c" $relKey

    $parentDir = Split-Path $rel -Parent
    if ([string]::IsNullOrWhiteSpace($parentDir)) {
      $dirId = $pluginDirId
    } else {
      # Nested dirs under DSSTech\...
      $parts = $parentDir -split '[\\/]'
      $dirId = $pluginDirId
      $accum = ""
      foreach ($part in $parts) {
        $accum = if ($accum) { "$accum/$part" } else { $part }
        $nextId = Get-WiXId "d" "$y|dir|$accum"
        # Emit Directory only once via side table
        if (-not $script:EmittedDirs) { $script:EmittedDirs = @{} }
        if (-not $script:EmittedDirs.ContainsKey($nextId)) {
          $script:EmittedDirs[$nextId] = [pscustomobject]@{
            Id = $nextId; Name = $part; Parent = $dirId; Year = $y; Path = $accum
          }
        }
        $dirId = $nextId
      }
    }

    $srcAttr = $file.FullName
    $componentXml.Add("    <Component Id=`"$compId`" Directory=`"$dirId`" Guid=`"$compGuid`">")
    $componentXml.Add("      <File Id=`"$fileId`" Source=`"$srcAttr`" KeyPath=`"yes`" />")
    $componentXml.Add("    </Component>")
  }
}

$dirXml.Add('    </DirectoryRef>')

# Nested plugin subdirectories (must appear before components that reference them)
if ($script:EmittedDirs -and $script:EmittedDirs.Count -gt 0) {
  # Parents before children
  $ordered = @($script:EmittedDirs.Values | Sort-Object { $_.Path.Length }, Path)
  foreach ($d in $ordered) {
    $dirXml.Add("    <DirectoryRef Id=`"$($d.Parent)`">")
    $dirXml.Add("      <Directory Id=`"$($d.Id)`" Name=`"$($d.Name)`" />")
    $dirXml.Add("    </DirectoryRef>")
  }
}
$dirXml.Add('  </Fragment>')

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
foreach ($line in $dirXml) { [void]$sb.AppendLine($line) }
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="ProductComponents">')
foreach ($line in $componentXml) { [void]$sb.AppendLine($line) }
[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$generated = Join-Path $installerDir "GeneratedFiles.wxs"
Set-Content -Path $generated -Value $sb.ToString() -Encoding UTF8

Write-Host "Staged hosts: $($years -join ', ')"
Write-Host "Generated: $generated"

$msiOutDir = Join-Path $installerDir "bin\$Configuration"
dotnet build $wixproj -c $Configuration `
  -p:ProductVersion=$version `
  -p:DefineConstants="ProductVersion=$version"
if ($LASTEXITCODE -ne 0) { throw "WiX MSI build failed." }

$builtMsi = Get-ChildItem $msiOutDir -Filter "*.msi" -Recurse -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1
if (-not $builtMsi) { throw "Không tìm thấy .msi sau build dưới $msiOutDir" }

$msiName = "DSSTech-Revit-v$version-$stamp.msi"
$msiPath = Join-Path $OutDir $msiName
Copy-Item $builtMsi.FullName $msiPath -Force

$hash = (Get-FileHash $msiPath -Algorithm SHA256).Hash
$info = @"
package=$pkgName
version=$version
stamp=$stamp
hosts=$($years -join ',')
msi=$msiPath
msi_bytes=$((Get-Item $msiPath).Length)
msi_sha256=$hash
install_root=%ProgramData%\Autodesk\Revit\Addins\<year>\
"@
Set-Content -Path (Join-Path $OutDir ($msiName -replace '\.msi$', '.txt')) -Value $info -Encoding UTF8

Write-Host "MSI:    $msiPath"
Write-Host "SHA256: $hash"
Write-Host "Bytes:  $((Get-Item $msiPath).Length)"
Write-Output $msiPath
