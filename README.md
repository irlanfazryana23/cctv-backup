# CCTV Backup

[![Build and test](https://github.com/irlanfazryana23/cctv-backup/actions/workflows/ci.yml/badge.svg)](https://github.com/irlanfazryana23/cctv-backup/actions/workflows/ci.yml)
[![Version](https://img.shields.io/github/v/tag/irlanfazryana23/cctv-backup?label=version)](https://github.com/irlanfazryana23/cctv-backup/tags)
[![License](https://img.shields.io/github/license/irlanfazryana23/cctv-backup)](LICENSE)

<p align="center">
  <img src="cctv-logo.png" alt="CCTV Backup logo" width="120">
</p>

A Windows desktop tool for backing up recordings from Sanec / Xiongmai branch DVRs
to an external disk. Review recordings before downloading, continue interrupted
backups safely, and play saved `.h264` files without installing another player.

**1.0.0 — Initial release · Windows 10/11 · .NET 8 · x86**

## What it does

- Connects to DVRs through Cloud (Serial ID) or a local IP/domain.
- Imports branch information from a compatible CMS installation.
- Searches recordings by branch, camera and DVR-local date/time.
- Shows file size, progress, speed and estimated time remaining.
- Resumes interrupted backups without downloading completed files again.
- Writes a manifest for each branch so saved recordings can be verified.
- Includes an H.264 player with snapshots, seeking and full-screen playback.
- Provides English and Bahasa Indonesia interfaces.

## Get started

Download the installer from the [Releases page](https://github.com/irlanfazryana23/cctv-backup/releases), or [build it from source](#build-from-source).

1. Run `CCTV-Backup-Setup-1.0.0.exe`.
2. Choose English or Bahasa Indonesia.
3. Add a branch, test the connection, and choose an external NTFS or exFAT disk.
4. Select the period and cameras, then choose **Find recordings**.
5. Review the files and select **Download selected**.

The installer is currently unsigned, so Windows may show an unknown-publisher
warning. The application does not require administrator permission after install.

## Requirements

- Windows 10 or Windows 11, 32-bit process support.
- Internet access for Cloud DVR connections.
- An external disk formatted as NTFS or exFAT. FAT32 cannot store files larger than 4 GB.
- DVR credentials and either a Serial ID or reachable IP/domain and media port.

The CMS is optional. It can provide branch details during import, but the
application can be configured manually.

## Safety and privacy

Passwords are encrypted with Windows DPAPI for the current Windows user and
stored in `%APPDATA%\CctvBackup\credentials.dat`. Branches and preferences are
stored locally. The application has no telemetry, analytics, startup service or
automatic cleanup.

Recordings remain in their original `.h264` format. A backup never deletes
recordings from the DVR. Cancelled or failed partial files are removed, and
completed files are detected on the next search.

## Build from source

This is a .NET 8 Windows Forms application. Open `CctvBackup.sln` in Visual Studio 2022
or build it from a Developer PowerShell:

```powershell
dotnet build CctvBackup.sln --configuration Release
dotnet test tests\CctvBackup.Tests\CctvBackup.Tests.csproj --configuration Release
```

The DVR SDK and video decoder are proprietary vendor files. They are intentionally not
stored in this repository. `publish.ps1` copies the SDK from a local CMS installation
and expects decoder files in `vendor\decoder`; obtain and use those files only under
the applicable vendor license.

The ordinary build and tests do not require the vendor SDK:

```powershell
powershell -File tools\publish.ps1 -CmsFolder "C:\Program Files (x86)\CMS"
powershell -File tools\build-installer.ps1 -SkipPublish
```

Generated files are written to `dist\` and excluded from Git.

## Project layout

| Location | Purpose |
| --- | --- |
| `src\CctvBackup.App\` | Windows Forms application and player |
| `src\CctvBackup.Core\` | Backup engine, DVR clients, settings and localization |
| `tests\CctvBackup.Tests\` | Unit and workflow tests |
| `installer\` | Inno Setup installer definition and translations |
| `tools\` | Publishing, packaging and SDK inspection scripts |
| `vendor\decoder\` | Local-only proprietary decoder input |

## Contributing

Bug reports and pull requests are welcome. Before opening an issue, remove
passwords, serial IDs, IP addresses, usernames and local file paths from logs or
screenshots. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development checklist.

## License

The original project code is available under the [MIT License](LICENSE).
Vendor SDK and decoder binaries are not included and remain subject to their
respective vendor licenses.

## Install
1. Run `CCTV-Backup-Setup-1.0.0.exe`. Choose the setup language (English or Bahasa Indonesia).
2. Windows asks whether to install for **all users** (needs an administrator; goes to `C:\Program Files (x86)\CCTV Backup`) or **only for me**.
   Windows may warn about an "unknown publisher": the installer is not digitally signed. Choose **More info > Run anyway**.
3. Finish. The program is in the **Start menu** as **CCTV Backup** (a desktop icon is optional).

To remove it: Windows **Settings > Apps > CCTV Backup > Uninstall**. The uninstaller asks whether to also delete the saved branches, logins and logs;
answer **No** to keep them for a later re-install.

## Language
Use the **Language / Bahasa** box at the top right to switch between English and Bahasa Indonesia. It changes at once and is remembered.
The first time, the program follows the Windows display language.

## Add your branches
1. Press **Add branch...**
2. Fill in:
   - **Branch name**: any name you like (it becomes the folder name on the disk).
   - **Login type**, as in the CMS:
     - **Cloud (Serial ID)**: the DVR's cloud ID. Works from anywhere with internet, but is slower.
     - **IP address**: the DVR's local IP address (or domain name) and its Media Port (usually 34567). This connects directly and is usually much
       faster, but this PC must be able to reach that address: be on the same network, or have a VPN / leased line to the branch.
       Test it first: `ping` the address from this PC.
   - **User name / Password**: the DVR login.
   The number of cameras is not typed: the program reads it from the DVR.
3. Press **Test connection** to check the login (the cloud can take up to a minute; an unreachable IP address gives an error after about 15 seconds). Then **Save**.
   Save also contacts the DVR to read its cameras; if it cannot be reached it asks whether to save anyway.
4. Repeat for each branch. Use **Edit branch...** (or double-click a branch) to change one, **Remove branch** to delete one.

Passwords are stored encrypted for your Windows account only (`%APPDATA%\CctvBackup\credentials.dat`).

### Import from the CMS (optional)
If the CMS is installed on this PC, **Import from CMS...** copies its branches so you do not have to type them again.
1. Press **Import from CMS...**. The program opens the CMS list (`XML\Data.xml`) or asks you to pick the file.
2. Tick the branches to bring in. Branches already here (same name) are not listed. A branch that is the same DVR as one you already have is shown unticked, with a note.
3. The CMS passwords cannot be read. If the branches share one password, type it in the box; otherwise leave it empty and use **Edit branch...** for each one later.
4. Press **Import**. Cloud branches are imported as Cloud, IP/Domain branches as IP address. ARSP and IPv6 branches are listed as not supported.

The camera list comes from the CMS until you press **Test connection**, which reads the real cameras from the DVR.

## Backing up
**You never have to guess the next step.** The coloured banner at the top always says what to do now, with a button for it: tick a branch, read its cameras, choose the folder, find the recordings, download. Blue = a step for you, yellow = something needs attention, green = ready or finished, grey = the program is working. The two big blue buttons are numbered like the steps (**5. Find recordings**, **6. Download selected**) and stay grey until their step is possible. Rest the mouse on any button for a short explanation.

1. Tick the branches (left). Ticking a branch also shows its cameras (middle, titled with the branch name); all cameras are ticked by default. Cameras are chosen per branch: click a branch name to change its cameras.
2. Set **From / To**. These are the DVR's own clock times (branch local time).
3. Choose the save folder on the external disk.
4. Press **5. Find recordings**. The program lists every recorded file in that period. Nothing is downloaded yet.
5. In the list, tick the ones you want in the **Save** column (all are ticked to start with). Click a tick box, or highlight rows
   (click, Shift+click, Ctrl+click) and press **Space** or **Tick highlighted / Untick highlighted**. **Tick all / Untick all** do the whole list.
   The line above the list shows how many files are ticked and their total size.
   Files already on the disk show "Already saved" and cannot be ticked.
6. Press **6. Download selected**. The program works through the branches one after another and downloads one file at a time,
   so only one branch line is in use at any moment.

Each row shows its **Speed** and **Left** (time left), and the line above the bar shows the total speed and about how long is left.
The speed is a rough guide: it is steady only after a few seconds. When the run ends it shows the average speed.

If you change a branch, camera, date or the save folder after pressing Find, the list is out of date: the program says so and asks you to press
**Find recordings** again.

Files are saved in the DVR's original format (`.h264`), no conversion:

```
<save folder>\<Branch>\Cam01\2025-01-15\083000-091530.h264
```

A file called `_backup-manifest.json` in each branch folder lists what was saved.

### Playing the videos
The program has its own video player, so the `.h264` files can be watched without installing anything else.
- **Double-click** a finished row (Done or Already saved) in the list, or highlight it and press **Play video...**.
- With nothing highlighted, **Play video...** asks for a file (it starts in the save folder), so you can also open an older backup.
- In the player: play / pause, a position bar (click to jump), slower / normal / faster (2x, 4x), previous / next file in the same folder, **Save picture** (a JPG next to the recording), and full screen.
- Keys: **Space** play or pause, **Left / Right** 10 seconds back or forward, **F11** full screen (Esc to leave).
- It needs no administrator permission. If a file is empty or damaged, the player says so.
- **Double-click a `.h264` file in Windows Explorer** and it opens in this player straight away; the backup window does not open. The installer sets this up (a ticked option, "Play .h264 recordings with CCTV Backup"). It is also in the **Open with** list, so you can pick it by hand for one file.
- If another program was already chosen as the default for `.h264` and still exists, Windows keeps it (Windows does not let installers override that). Right-click a file, **Open with > Choose another app > CCTV Backup > Always**. A default that points to a program that was uninstalled is replaced automatically.
- Uninstalling removes the file type again.

## Stopping and continuing
- **Cancel** stops cleanly; unfinished files are removed.
- Press **Download selected** again to continue where it stopped. Files already downloaded are not fetched twice.
  After closing the program or unplugging the disk, press **Find recordings** again: finished files then show "Already saved".
- A failed file is retried up to 3 times, reconnecting each time. Anything still failing is shown in red with the reason;
  press **Download selected** again to retry just those.

## When something goes wrong
| Message | Meaning |
|---|---|
| `ARSP_NO_DEVICE` / `CONNECT_*` | The branch DVR is offline, the Serial ID is wrong, or its internet is down. |
| `PASSWORD_NOT_VALID` / `LOGIN_USER_NOEXIST` | Wrong login. Fix it with **Edit branch...**. Do not retry many times: the DVR locks the account (`USER_LOCKED`). |
| `LOGIN_FULL` / `CONNECT_FULL` | The DVR has no free connections (someone is viewing it). Close other sessions and retry. |
| Not enough free space / FAT32 | Free up the disk or use an NTFS/exFAT disk. |

Logs: `%APPDATA%\CctvBackup\logs\` (one file per day, kept 30 days). Logs never contain passwords.
