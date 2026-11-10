; Inno Setup script for the CCTV Backup installer.
; Do not run this by hand: tools\build-installer.ps1 builds the program first and then compiles this script.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "CCTV Backup"
#define AppExe "CctvBackup.exe"

[Setup]
; Never change AppId: Windows uses it to recognise an older install and upgrade it in place.
AppId={{B3E1D5C2-7F44-4A8B-9D6E-2A5C8E1F0B73}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\src\CctvBackup.App\Assets\app.ico
OutputDir=..\dist\installer
OutputBaseFilename=CCTV-Backup-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Installs for all users (Program Files). A person without admin rights can choose "only for me" in the first dialog.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
; Tells Windows that file associations changed, so Explorer shows the new icon and 'open with' at once.
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "indonesian"; MessagesFile: "Indonesian.isl"

[CustomMessages]
english.DeleteUserData=Also delete the saved branches, logins and logs of this program?%n%nChoose No to keep them (for example if you plan to install it again).
english.AssocGroup=File types
indonesian.AssocGroup=Jenis file
english.AssocH264=Play .h264 recordings with {#AppName} (double-click a file to watch it, no need to open the program first)
indonesian.AssocH264=Putar rekaman .h264 dengan {#AppName} (klik dua kali file untuk menontonnya, tanpa membuka program lebih dulu)
indonesian.DeleteUserData=Hapus juga cabang, login, dan log yang tersimpan milik program ini?%n%nPilih Tidak untuk menyimpannya (misalnya jika Anda akan memasangnya lagi).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "assoc"; Description: "{cm:AssocH264}"; GroupDescription: "{cm:AssocGroup}"

[Files]
; The program, its .NET runtime, the DVR driver ("sdk" folder) and the video decoder ("decoder" folder). Debug symbols are not needed on a user's PC.
Source: "..\dist\CctvBackup\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; Double-clicking a .h264 file starts CCTV Backup with the file as its argument; only the player opens.
; HKA = all users for an administrator install, the current user for an "only for me" install.
Root: HKA; Subkey: "Software\Classes\.h264"; ValueType: string; ValueName: ""; ValueData: "CctvBackup.Recording"; Flags: uninsdeletevalue; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\.h264\OpenWithProgids"; ValueType: string; ValueName: "CctvBackup.Recording"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\CctvBackup.Recording"; ValueType: string; ValueName: ""; ValueData: "H264 recording ({#AppName})"; Flags: uninsdeletekey; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\CctvBackup.Recording\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\CctvBackup.Recording\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assoc
; Always listed under "Open with", even if the task above is not ticked.
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".h264"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; temporary download folder the program may have created
Type: filesandordirs; Name: "{commonappdata}\CctvBackup"

[Code]
const
  H264UserChoice = 'Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.h264\UserChoice';

// The program a command line starts, e.g. "C:\x\Player.exe" "%1"  ->  C:\x\Player.exe
function ProgramOfCommand(Cmd: String): String;
var
  P: Integer;
begin
  Cmd := Trim(Cmd);
  Result := '';
  if Cmd = '' then Exit;
  if Cmd[1] = '"' then
  begin
    Delete(Cmd, 1, 1);
    P := Pos('"', Cmd);
    if P > 0 then Result := Copy(Cmd, 1, P - 1) else Result := Cmd;
  end
  else
  begin
    P := Pos(' ', Cmd);
    if P > 0 then Result := Copy(Cmd, 1, P - 1) else Result := Cmd;
  end;
  Result := ExpandConstant(Result);
end;

// Windows keeps the program the user once chose for .h264 ("UserChoice") and ignores new registrations while it is set.
// If that program is gone (for example a DVR player that was uninstalled) the choice is dead: remove it, so the
// registration above takes effect. A choice that still works is never touched.
procedure ClearDeadH264Choice;
var
  ProgId, Cmd, Exe: String;
begin
  if not RegQueryStringValue(HKCU, H264UserChoice, 'ProgId', ProgId) then Exit;
  if (ProgId = '') or (ProgId = 'CctvBackup.Recording') then Exit;
  // Only a choice whose command is known and points at a missing file counts as dead.
  if not RegQueryStringValue(HKCR, ProgId + '\shell\open\command', '', Cmd) then Exit;
  Exe := ProgramOfCommand(Cmd);
  if (Exe = '') or FileExists(Exe) then Exit;
  if RegDeleteKeyIncludingSubkeys(HKCU, H264UserChoice) then
    Log('Removed the dead .h264 choice (' + ProgId + ' -> ' + Exe + ').')
  else
    Log('Could not remove the dead .h264 choice (' + ProgId + '); the user can pick the program with Open with.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('assoc') then
    ClearDeadH264Choice;
end;
// Uninstalling removes the program but, unless the user agrees, keeps the saved branches and logins.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if not UninstallSilent then
    begin
      if MsgBox(CustomMessage('DeleteUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(ExpandConstant('{userappdata}\CctvBackup'), True, True, True);
    end;
  end;
end;
