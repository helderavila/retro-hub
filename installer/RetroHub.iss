; Instalador do Retro Hub (Inno Setup 6).
; Gere com installer\build-installer.ps1 — ele publica o app e passa a versão (/DAppVersion=…).

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
; Instala só para o usuário atual: não pede administrador e o app pode se atualizar sozinho no futuro.
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
; Fecha o Retro Hub aberto antes de atualizar os arquivos.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.FinishedLabel=O Retro Hub foi instalado. Sua biblioteca, emuladores e saves ficam em %LOCALAPPDATA%\RetroHub e são mantidos em atualizações e desinstalações.

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"
Name: "startup"; Description: "Abrir o Retro Hub junto com o Windows"; GroupDescription: "Inicialização:"; Flags: unchecked
Name: "startup\tv"; Description: "Direto no Modo TV (para usar o PC como console)"; Flags: unchecked

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Versões antigas dos manifestos não podem sobrar (um emulador removido do catálogo continuaria aparecendo).
Type: filesandordirs; Name: "{app}\manifests"

[Icons]
Name: "{autoprograms}\Retro Hub"; Filename: "{app}\RetroHub.exe"
Name: "{autoprograms}\Retro Hub (Modo TV)"; Filename: "{app}\RetroHub.exe"; Parameters: "--tv"
Name: "{autodesktop}\Retro Hub"; Filename: "{app}\RetroHub.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RetroHub"; \
    ValueData: """{app}\RetroHub.exe"""; Tasks: startup and not startup\tv; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RetroHub"; \
    ValueData: """{app}\RetroHub.exe"" --tv"; Tasks: startup\tv; Flags: uninsdeletevalue

[Run]
Filename: "{app}\RetroHub.exe"; Description: "Abrir o Retro Hub agora"; Flags: nowait postinstall skipifsilent
