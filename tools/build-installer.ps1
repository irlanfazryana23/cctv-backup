<#
  Builds the installer:  dist\installer\CCTV-Backup-Setup-<version>.exe

  usage:  powershell -File tools\build-installer.ps1 [-SkipPublish] [-CmsFolder "C:\Program Files (x86)\CMS"]

  Steps: makes the icon if it is missing, publishes the program (tools\publish.ps1, which also copies the DVR driver
  from a CMS install into the "sdk" folder), downloads the Inno Setup compiler the first time (into tools\.cache,
  nothing is installed on this PC), and compiles installer\CctvBackup.iss.

  The installer contains vendor driver files from your CMS: keep it inside your company.
#>
param(
    [switch]$SkipPublish,
    [string]$CmsFolder = 'C:\Program Files (x86)\CMS'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---- version, from the program's project file ----
$csproj = [xml](Get-Content (Join-Path $root 'src\CctvBackup.App\CctvBackup.App.csproj'))
$version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'No <Version> found in CctvBackup.App.csproj' }
Write-Host "Version: $version"

# ---- icon ----
$icon = Join-Path $root 'src\CctvBackup.App\Assets\app.ico'
if (-not (Test-Path $icon)) {
    Write-Host 'Making the icon from cctv-logo.png...'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-icon.ps1')
    if (-not (Test-Path $icon)) { throw 'The icon could not be made.' }
}

# ---- program ----
if (-not $SkipPublish) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'publish.ps1') -CmsFolder $CmsFolder
    if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
}
if (-not (Test-Path (Join-Path $root 'dist\CctvBackup\CctvBackup.exe'))) { throw 'dist\CctvBackup\CctvBackup.exe is missing: run without -SkipPublish.' }
if (-not (Test-Path (Join-Path $root 'dist\CctvBackup\sdk\NetSdk.dll'))) { throw 'dist\CctvBackup\sdk\NetSdk.dll is missing: the DVR driver was not bundled.' }

# ---- Indonesian text for the installer wizard (small translation file from the Inno Setup project) ----
$isl = Join-Path $root 'installer\Indonesian.isl'
if (-not (Test-Path $isl)) {
    Write-Host 'Downloading Indonesian.isl...'
    Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/jrsoftware/issrc/main/Files/Languages/Unofficial/Indonesian.isl' -OutFile $isl
}

# ---- the Inno Setup compiler (portable copy from NuGet) ----
$cache = Join-Path $PSScriptRoot '.cache'
$iscc = Join-Path $cache 'innosetup\tools\ISCC.exe'
if (-not (Test-Path $iscc)) {
    Write-Host 'Downloading the Inno Setup compiler (one time, about 15 MB)...'
    New-Item -ItemType Directory -Force $cache | Out-Null
    $zip = Join-Path $cache 'innosetup.zip'
    Invoke-WebRequest -UseBasicParsing 'https://api.nuget.org/v3-flatcontainer/tools.innosetup/6.7.1/tools.innosetup.6.7.1.nupkg' -OutFile $zip
    Expand-Archive $zip (Join-Path $cache 'innosetup') -Force
    if (-not (Test-Path $iscc)) { throw 'ISCC.exe not found after download.' }
}

# ---- compile ----
Remove-Item (Join-Path $root 'dist\installer') -Recurse -Force -ErrorAction SilentlyContinue
& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\CctvBackup.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$out = Join-Path $root "dist\installer\CCTV-Backup-Setup-$version.exe"
Write-Host ("Installer ready: {0}  ({1:N0} MB)" -f $out, ((Get-Item $out).Length / 1MB))
