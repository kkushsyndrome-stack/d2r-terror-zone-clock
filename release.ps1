# Packages the Terror Zone Clock for a GitHub release.
# Output: dist\D2RTerrorZoneClock-v<version>.zip and a matching .sha256 file.
# The version comes from AssemblyInformationalVersion in CalendarTimeTool.cs.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
& (Join-Path $root 'build.ps1') -OutDir $stage
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') $stage

$version = (Get-Item (Join-Path $stage 'D2RTerrorZoneClock.exe')).VersionInfo.ProductVersion
if (-not $version) { throw "The built exe has no version; check the assembly attributes in CalendarTimeTool.cs" }
$name = "D2RTerrorZoneClock-v$version.zip"
$zip = Join-Path $dist $name

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  $name" | Set-Content -Encoding ascii "$zip.sha256"
Write-Host "Packaged $zip"
Write-Host "SHA256 $hash"
