# CCTV Backup Tool — Native Linux Edition (`linux/`)

This folder contains the **100% Native Linux conversion** of the CCTV Backup Tool.
It lives entirely inside `linux/` so the original Windows version (`src/`, `tests/`, `dist/`, `CctvBackup.sln`) remains untouched.

## Key Features (Zero Wine Required)

- **100% Native Linux Execution**: Runs directly on 64-bit Linux (`linux-x64`) with **no Wine and no 32-bit Windows DLLs required**.
- **Cloud (Serial ID) & Direct IP Support**:
  - **Cloud (Serial ID — Default)**: Uses [`XmCloudNatClient`](src/CctvBackup.Core/Dvr/XmCloudNatClient.cs) + [`DvripDvrClient`](src/CctvBackup.Core/Dvr/DvripDvrClient.cs), a pure native C# implementation of the Xiongmai (`secu100.net` / `libeznat`) NAT traversal, P2P hole-punching, proxy relay fallback, `"XMIP"` reliable UDP slicing/ACK transport, and Sofia DVRIP protocol.
  - **Direct IP / Domain (`34567`)**: Uses [`DvripDvrClient`](src/CctvBackup.Core/Dvr/DvripDvrClient.cs) over native TCP sockets (`MsgLoginReq 1000`, `MsgSysInfoReq 1020`, `MsgFileSearchReq 1440`, `MsgPlayReq 1442` / `MsgPlayData 1424`).
  - **Optional Vendor `libnetsdk.so`**: If you also place a native Linux `libnetsdk.so` inside the `sdk/` directory, [`LinuxDvrClient`](src/CctvBackup.Core/Dvr/LinuxDvrClient.cs) will automatically use [`XmDvrClient`](src/CctvBackup.Core/Dvr/XmDvrClient.cs) (`P/Invoke`) with automatic fallback to the native C# engine.
- **Linux Security & Paths**:
  - Saves branch passwords encrypted with **AES-256-GCM** via [`LinuxFileProtector`](src/CctvBackup.Core/Credentials/LinuxFileProtector.cs) and a `0600` (`UserRead | UserWrite`) per-user key in `~/.config/CctvBackup/.key`.
  - Follows Linux XDG paths (`~/.config/CctvBackup/branches.json`, `credentials.dat`, `settings.json`, `logs/`).
- **Native Desktop App Window**:
  - Launches in a native desktop app window (`--app=...` via Chromium / Chrome / Brave / Edge / `xdg-open`) or runs headless on a Linux server (`--no-browser --port 5080`).
- **Full Feature Parity**:
  - Multi-language UI (English & Bahasa Indonesia), Step-by-Step Workflow Guide, CMS `Data.xml` Branch Importer, Test Connection & Camera Auto-Detection, Find Recordings, Disk Space Checker, Resumable `.part` Downloads, and Activity Logs.

---

## Project Structure

```text
linux/
├── CctvBackup.Linux.sln
├── build-linux.sh                 # Native Linux build script (outputs linux/dist/cctv-backup)
├── install.sh                     # Installs to ~/.local/opt/cctv-backup + desktop shortcut
├── publish-from-windows.ps1       # Cross-packages linux/dist/cctv-backup from Windows
├── cctv-backup.desktop            # FreeDesktop launcher entry
├── src/
│   ├── CctvBackup.Core/           # Core library (XmCloudNatClient, DvripDvrClient, XmSdk, BackupEngine, AES-GCM, Localization)
│   └── CctvBackup.App/            # Native Linux Kestrel + Desktop App UI
└── tests/
    └── CctvBackup.Tests/          # 205 unit & integration tests
```

---

## How to Run on Linux

### 1. Prerequisites
Install the **.NET 8 Runtime** (or SDK) on your Linux machine:
```bash
# Ubuntu / Debian
sudo apt update && sudo apt install -y dotnet-runtime-8.0
```
*(No Wine or 32-bit packages are needed!)*

### 2. Run the Pre-Packaged Build (`linux/dist/cctv-backup`)
If you copied `linux/dist/cctv-backup` to your Linux machine:
```bash
cd linux/dist/cctv-backup
chmod +x cctv-backup.sh
./cctv-backup.sh
```

Or build a native `linux-x64` binary directly on Linux:
```bash
cd linux
chmod +x build-linux.sh install.sh
./build-linux.sh
./dist/cctv-backup/cctv-backup
```

### 3. Headless / Server Mode
To run on a headless Linux server and access the UI from another computer on your LAN:
```bash
./cctv-backup.sh --no-browser --port 5080
```
