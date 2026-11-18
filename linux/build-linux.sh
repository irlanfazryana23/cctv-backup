#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST_DIR="$SCRIPT_DIR/dist/cctv-backup"

echo "=== Building CCTV Backup (Native Linux Edition — No Wine Required) ==="
rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR" "$DIST_DIR/sdk"

echo "[1/3] Publishing Native Linux Desktop App (cctv-backup, self-contained)..."
dotnet publish "$SCRIPT_DIR/src/CctvBackup.Desktop/CctvBackup.Desktop.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$DIST_DIR"

echo "[2/3] Copying App Icons and Desktop Integration..."
if [ -f "$SCRIPT_DIR/src/CctvBackup.Desktop/Assets/logo.png" ]; then
  cp -f "$SCRIPT_DIR/src/CctvBackup.Desktop/Assets/logo.png" "$DIST_DIR/cctv-backup.png"
fi
if [ -f "$SCRIPT_DIR/src/CctvBackup.Desktop/Assets/app.ico" ]; then
  cp -f "$SCRIPT_DIR/src/CctvBackup.Desktop/Assets/app.ico" "$DIST_DIR/cctv-backup.ico"
fi
if [ -f "$SCRIPT_DIR/cctv-backup.desktop" ]; then
  cp -f "$SCRIPT_DIR/cctv-backup.desktop" "$DIST_DIR/cctv-backup.desktop"
fi

echo "[3/3] Copying optional native Linux SDK files (if present)..."
if [ -d "$SCRIPT_DIR/sdk" ]; then
  cp -f "$SCRIPT_DIR/sdk/"* "$DIST_DIR/sdk/" 2>/dev/null || true
fi

chmod +x "$DIST_DIR/cctv-backup" 2>/dev/null || true

echo ""
echo "Build complete: $DIST_DIR"
echo "Run with:       $DIST_DIR/cctv-backup"
