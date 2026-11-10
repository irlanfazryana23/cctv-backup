<#
  Builds the program into dist\ and bundles the vendor driver, so it runs on a PC without the CMS.

  usage:  powershell -File tools\publish.ps1 [-CmsFolder "C:\Program Files (x86)\CMS"] [-DecoderFolder "vendor\decoder"]

  The driver files are copied from your own CMS install into dist\CctvBackup\sdk. They belong to the
  vendor: keep the result inside your company, do not publish or share it.
  The video decoder for the built-in player (H264Play.dll and 7 helper DLLs) is copied from vendor\decoder
  into dist\CctvBackup\decoder. They belong to the vendor too.
#>
param(
    [string]$CmsFolder = 'C:\Program Files (x86)\CMS',
    [string]$DecoderFolder = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
$env:DOTNET_NOLOGO = 1

# Everything NetSdk.dll needs that is not a Windows system file (found with tools\sdk-inspect\imports.py).
$driverFiles = 'NetSdk.dll', 'StreamReader.dll', 'msvcr80.dll', 'Microsoft.VC80.CRT.manifest'

foreach ($f in $driverFiles) {
    if (-not (Test-Path (Join-Path $CmsFolder $f))) { throw "Missing $f in $CmsFolder. Install the CMS or pass -CmsFolder." }
}

if (-not $DecoderFolder) { $DecoderFolder = Join-Path $root 'vendor\decoder' }
$decoderFiles = 'H264Play.dll', 'StreamReader.dll', 'avcodec.dll', 'avutil.dll', 'swresample.dll', 'msvcp140.dll', 'vcruntime140.dll', 'vcomp140.dll'
foreach ($f in $decoderFiles) {
    if (-not (Test-Path (Join-Path $DecoderFolder $f))) { throw "Missing $f in $DecoderFolder. Put the decoder files in vendor\decoder (or pass -DecoderFolder)." }
}
$app = Join-Path $root 'dist\CctvBackup'
$spike = Join-Path $root 'dist\CctvBackup-spike'

# start from empty folders so files of an older build (e.g. the old CctvBackup.App.exe) are never shipped
Remove-Item $app, $spike -Recurse -Force -ErrorAction SilentlyContinue

& $dotnet publish (Join-Path $root 'src\CctvBackup.App') -c Release -o $app -v q
if ($LASTEXITCODE -ne 0) { throw 'publish of the app failed' }
& $dotnet publish (Join-Path $root 'src\CctvBackup.Spike') -c Release -o $spike -v q
if ($LASTEXITCODE -ne 0) { throw 'publish of the test tool failed' }

foreach ($target in $app, $spike) {
    $sdk = Join-Path $target 'sdk'
    New-Item -ItemType Directory -Force -Path $sdk | Out-Null
    foreach ($f in $driverFiles) { Copy-Item (Join-Path $CmsFolder $f) $sdk -Force }
}

# The video decoder used by the built-in player: H264Play.dll and the DLLs it needs (found with tools\sdk-inspect\imports.py).
# The vendor's player program itself (Player.exe, its login settings, its ActiveX control) is not shipped.
$decoder = Join-Path $app 'decoder'
New-Item -ItemType Directory -Force -Path $decoder | Out-Null
foreach ($f in $decoderFiles) { Copy-Item (Join-Path $DecoderFolder $f) $decoder -Force }
Write-Host "Done. Program: $app"
