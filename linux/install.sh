#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST_DIR="$SCRIPT_DIR/dist/cctv-backup"
INSTALL_DIR="$HOME/.local/opt/cctv-backup"
BIN_DIR="$HOME/.local/bin"
APP_DIR="$HOME/.local/share/applications"

if [ ! -d "$DIST_DIR" ]; then
  echo "Building package first..."
  bash "$SCRIPT_DIR/build-linux.sh"
fi

echo "Installing CCTV Backup (Native Linux) to $INSTALL_DIR..."
mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$APP_DIR"
cp -rf "$DIST_DIR/"* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/cctv-backup" 2>/dev/null || true
chmod +x "$INSTALL_DIR/cctv-backup.sh" 2>/dev/null || true

if [ -f "$INSTALL_DIR/cctv-backup" ]; then
  ln -sf "$INSTALL_DIR/cctv-backup" "$BIN_DIR/cctv-backup"
  EXEC_TARGET="$INSTALL_DIR/cctv-backup"
else
  ln -sf "$INSTALL_DIR/cctv-backup.sh" "$BIN_DIR/cctv-backup"
  EXEC_TARGET="$INSTALL_DIR/cctv-backup.sh"
fi

sed "s|Exec=.*|Exec=$EXEC_TARGET|g" "$SCRIPT_DIR/cctv-backup.desktop" > "$APP_DIR/cctv-backup.desktop"
chmod +x "$APP_DIR/cctv-backup.desktop"

echo ""
echo "Installed successfully (100% Native Linux — no Wine needed)!"
echo "- Launch from your desktop application menu ('CCTV Backup')"
echo "- Or run from terminal: $BIN_DIR/cctv-backup"
