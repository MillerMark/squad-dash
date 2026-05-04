; SquadDash Inno Setup 6 installer script
; Build with: ISCC.exe /DAppVersion=1.0.0 SquadDash.iss
; Or use:     .\installer\build-installer.ps1 -Version 1.0.0

#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif

[Setup]
AppId={{52769C8B-FFBC-4427-9DA0-BBF6288CA206}
AppName=SquadDash
AppVersion={#AppVersion}
AppPublisher=SquadDash Team
AppPublisherURL=https://github.com/MillerMark/squad-dash
AppSupportURL=https://github.com/MillerMark/squad-dash/issues
AppUpdatesURL=https://github.com/MillerMark/squad-dash/releases
DefaultDirName={localappdata}\SquadDash
DefaultGroupName=SquadDash
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=SquadDash-{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
; No UAC prompt — installs entirely within %LocalAppData%
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "shellmenu";   Description: "Add ""Open SquadDash Here"" to Windows Explorer folder context menu"; GroupDescription: "Shell integration"

[Files]
; Launcher — lives at the app root so the Start Menu shortcut points to it
Source: "..\artifacts\publish\launcher\SquadDash.exe"; DestDir: "{app}"; Flags: ignoreversion

; App payload — SquadDash.App.exe + all DLLs, runtimes, and assets from dotnet publish
Source: "..\artifacts\publish\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs

; Squad.SDK — Node.js runtime scripts + production node_modules
; NOTE: Node.js itself is NOT bundled. node.exe must be on PATH (see README / WinGet prerequisites).
Source: "..\artifacts\publish\sdk\*"; DestDir: "{app}\Squad.SDK"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SquadDash";                        Filename: "{app}\SquadDash.exe"
Name: "{group}\{cm:UninstallProgram,SquadDash}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\SquadDash";                  Filename: "{app}\SquadDash.exe"; Tasks: desktopicon

[Registry]
; Explorer context menu: right-click on a folder in the tree / file list
Root: HKCU; Subkey: "Software\Classes\Directory\shell\SquadDash";          ValueType: string; ValueName: "";      ValueData: "Open SquadDash Here";                                        Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\SquadDash";          ValueType: string; ValueName: "Icon";  ValueData: "{app}\SquadDash.exe,0";                                      Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\SquadDash\command";  ValueType: string; ValueName: "";      ValueData: """{app}\SquadDash.exe"" ""--folder"" ""%1""";                Tasks: shellmenu

; Explorer context menu: right-click on the background of an open folder (%V = current folder)
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\SquadDash";          ValueType: string; ValueName: "";      ValueData: "Open SquadDash Here";                            Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\SquadDash";          ValueType: string; ValueName: "Icon";  ValueData: "{app}\SquadDash.exe,0";                          Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\SquadDash\command";  ValueType: string; ValueName: "";      ValueData: """{app}\SquadDash.exe"" ""--folder"" ""%V""";   Tasks: shellmenu

[Run]
Filename: "{app}\SquadDash.exe"; Description: "{cm:LaunchProgram,SquadDash}"; Flags: nowait postinstall skipifsilent
