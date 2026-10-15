; Retro Hub installer (Inno Setup 6).
; Build with installer\build-installer.ps1: it publishes the app and passes the version (/DAppVersion=...).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6C0E8E52-3F4B-4E21-9C35-6A1B7D2E9F10}
AppName=Retro Hub
AppVersion={#AppVersion}
AppVerName=Retro Hub {#AppVersion}
AppPublisher=Retro Hub
VersionInfoVersion={#AppVersion}
; Per-user install: no administrator prompt, and the app can update itself in the future.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Retro Hub
DefaultGroupName=Retro Hub
DisableProgramGroupPage=yes
DisableDirPage=auto
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=RetroHub-Setup-{#AppVersion}
SetupIconFile=..\src\RetroHub.App\Assets\RetroHub.ico
UninstallDisplayIcon={app}\RetroHub.exe
UninstallDisplayName=Retro Hub
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Closes a running Retro Hub before replacing files.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
en.FinishedLabel=Retro Hub is installed. Your library, emulators and saves live in %LOCALAPPDATA%\RetroHub and are kept across updates and uninstalls.
ptbr.FinishedLabel=O Retro Hub foi instalado. Sua biblioteca, emuladores e saves ficam em %LOCALAPPDATA%\RetroHub e são mantidos em atualizações e desinstalações.

[CustomMessages]
en.Shortcuts=Shortcuts:
en.DesktopIcon=Create a desktop shortcut
en.Startup=Startup:
en.StartWithWindows=Open Retro Hub when Windows starts
en.StartInTv=Straight into TV mode (use the PC as a console)
en.TvShortcut=Retro Hub (TV mode)
en.OpenNow=Open Retro Hub now
ptbr.Shortcuts=Atalhos:
ptbr.DesktopIcon=Criar atalho na área de trabalho
ptbr.Startup=Inicialização:
ptbr.StartWithWindows=Abrir o Retro Hub junto com o Windows
ptbr.StartInTv=Direto no Modo TV (para usar o PC como console)
ptbr.TvShortcut=Retro Hub (Modo TV)
ptbr.OpenNow=Abrir o Retro Hub agora

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Shortcuts}"
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:Startup}"; Flags: unchecked
Name: "startup\tv"; Description: "{cm:StartInTv}"; Flags: unchecked

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Old manifests must not linger (an emulator removed from the catalog would keep showing up).
; The TV shortcut is named per language: remove the other language's one.
Type: filesandordirs; Name: "{app}\manifests"
Type: files; Name: "{autoprograms}\Retro Hub (Modo TV).lnk"; Languages: en
Type: files; Name: "{autoprograms}\Retro Hub (TV mode).lnk"; Languages: ptbr

[Icons]
Name: "{autoprograms}\Retro Hub"; Filename: "{app}\RetroHub.exe"
Name: "{autoprograms}\{cm:TvShortcut}"; Filename: "{app}\RetroHub.exe"; Parameters: "--tv"
Name: "{autodesktop}\Retro Hub"; Filename: "{app}\RetroHub.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RetroHub"; \
    ValueData: """{app}\RetroHub.exe"""; Tasks: startup and not startup\tv; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RetroHub"; \
    ValueData: """{app}\RetroHub.exe"" --tv"; Tasks: startup\tv; Flags: uninsdeletevalue

; The app starts in the language chosen here (until changed in its settings).
Root: HKCU; Subkey: "Software\RetroHub"; ValueType: string; ValueName: "Language"; ValueData: "en"; Languages: en; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\RetroHub"; ValueType: string; ValueName: "Language"; ValueData: "pt-BR"; Languages: ptbr; Flags: uninsdeletekey

[Run]
Filename: "{app}\RetroHub.exe"; Description: "{cm:OpenNow}"; Flags: nowait postinstall skipifsilent
