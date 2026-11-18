$ErrorActionPreference = 'Stop'

$linuxRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir   = Join-Path $linuxRoot "dist\cctv-backup"
$sdkOut    = Join-Path $distDir "sdk"

$dotnet = "dotnet"

Write-Host "=== Packaging CCTV Backup (Linux Edition) into linux/dist/cctv-backup ==="
if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
New-Item -ItemType Directory -Path $distDir, $sdkOut | Out-Null

Write-Host "[1/3] Publishing Self-Contained Single-File Native Linux App (cctv-backup)..."
& $dotnet publish (Join-Path $linuxRoot "src\CctvBackup.Desktop\CctvBackup.Desktop.csproj") `
    -c Release `
    -r linux-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --no-restore `
    -p:UseSharedCompilation=false -m:1 `
    -o $distDir

Write-Host "[2/3] Publishing Web Edition Fallback (cctv-backup-web)..."
$webTempDir = Join-Path $linuxRoot "dist\web-temp"
if (Test-Path $webTempDir) { Remove-Item -Recurse -Force $webTempDir }
& $dotnet publish (Join-Path $linuxRoot "src\CctvBackup.App\CctvBackup.App.csproj") `
    -c Release `
    --no-restore `
    -p:UseSharedCompilation=false -m:1 `
    -o $webTempDir

# Copy only web dll to dist so it never overwrites the desktop binary
Copy-Item (Join-Path $webTempDir "cctv-backup.dll") (Join-Path $distDir "cctv-backup-web.dll") -Force
Remove-Item -Recurse -Force $webTempDir

Write-Host "[3/3] Copying Logo and Creating Desktop Integration..."
$logoSrc = Join-Path $linuxRoot "src\CctvBackup.Desktop\Assets\logo.png"
$icoSrc  = Join-Path $linuxRoot "src\CctvBackup.Desktop\Assets\app.ico"
if (Test-Path $logoSrc) { Copy-Item $logoSrc (Join-Path $distDir "cctv-backup.png") -Force }
if (Test-Path $icoSrc)  { Copy-Item $icoSrc  (Join-Path $distDir "cctv-backup.ico") -Force }

# Shell wrapper launcher (optional backup)
$launcherDesktopSh = @'
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$DIR"
export AVALONIA_GLOBAL_SCALE_FACTOR=1
exec "$DIR/cctv-backup" "$@"
'@ -replace "`r`n", "`n"
[System.IO.File]::WriteAllText((Join-Path $distDir "cctv-backup.sh"), $launcherDesktopSh)

# Web Edition Launcher (Headless / browser fallback)
$launcherWebSh = @'
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$DIR"
exec dotnet "$DIR/cctv-backup-web.dll" "$@"
'@ -replace "`r`n", "`n"
[System.IO.File]::WriteAllText((Join-Path $distDir "cctv-backup-web.sh"), $launcherWebSh)

# Desktop shortcut entry (.desktop)
$desktopEntry = @'
[Desktop Entry]
Version=1.0
Type=Application
Name=CCTV Backup
Comment=High-Speed CCTV DVR Backup Tool (Native Linux Edition)
Exec=bash -c 'DIR="$(dirname %k)"; cd "$DIR" && ./cctv-backup'
Icon=cctv-backup
Terminal=false
Categories=Utility;AudioVideo;
StartupWMClass=cctv-backup
StartupNotify=true
'@ -replace "`r`n", "`n"
[System.IO.File]::WriteAllText((Join-Path $distDir "cctv-backup.desktop"), $desktopEntry)

# One-click desktop installer script
$setupDesktopSh = @'
#!/usr/bin/env bash
set -e

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN="$DIR/cctv-backup"
ICON="$DIR/cctv-backup.png"
DESKTOP_FILE="$DIR/cctv-backup.desktop"

# 1. Ensure executable permissions
chmod +x "$BIN" 2>/dev/null || true
chmod +x "$DIR/cctv-backup.sh" 2>/dev/null || true
chmod +x "$DIR/cctv-backup-web.sh" 2>/dev/null || true
chmod +x "$DIR/setup-desktop.sh" 2>/dev/null || true

# 2. Write desktop file with exact absolute paths and working directory
cat <<EOF > "$DESKTOP_FILE"
[Desktop Entry]
Version=1.0
Type=Application
Name=CCTV Backup
Comment=High-Speed CCTV DVR Backup Tool (Native Linux Edition)
Exec="$BIN"
Path=$DIR
Icon=$ICON
Terminal=false
Categories=Utility;AudioVideo;
StartupWMClass=cctv-backup
StartupNotify=true
EOF

chmod +x "$DESKTOP_FILE"

# 3. Install to user applications menu so it shows in Ubuntu search and dock
mkdir -p "$HOME/.local/share/applications"
cp -f "$DESKTOP_FILE" "$HOME/.local/share/applications/cctv-backup.desktop"
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true

# 4. If Desktop folder exists, install directly to Desktop
if [ -d "$HOME/Desktop" ]; then
    cp -f "$DESKTOP_FILE" "$HOME/Desktop/cctv-backup.desktop"
    chmod +x "$HOME/Desktop/cctv-backup.desktop"
    gio set "$HOME/Desktop/cctv-backup.desktop" metadata::trusted true 2>/dev/null || true
fi

echo ""
echo "=========================================================="
echo "  CCTV Backup Desktop Shortcut Installed Successfully!   "
echo "=========================================================="
echo "• The app icon is now on your Desktop and in the App Menu!"
echo "• NOTE FOR UBUNTU DESKTOP: If the Desktop icon displays"
echo "  a gear or doesn't launch, simply RIGHT-CLICK the icon"
echo "  on your Desktop and click: 'Allow Launching'."
echo "• You can also launch it from the Ubuntu App Search (Super key)"
echo "  or directly run: $BIN"
echo "=========================================================="
'@ -replace "`r`n", "`n"
[System.IO.File]::WriteAllText((Join-Path $distDir "setup-desktop.sh"), $setupDesktopSh)

Write-Host ""
Write-Host "Done! Output folder: $distDir"
Write-Host "  -> Native binary: cctv-backup (standalone executable)"
Write-Host "  -> Desktop installer: ./setup-desktop.sh"
Write-Host "  -> Icon asset: cctv-backup.png"
