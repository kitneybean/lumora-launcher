; Windows installer for Lumora Launcher (Inno Setup 6).
; Installs for the current user without admin rights into %LocalAppData%\Programs\Lumora,
; adds Start menu and desktop shortcuts and an entry in "Apps & features".
; The launcher keeps updating itself in this folder from GitHub Releases; settings and games stay in %AppData%\Lumora.
;
; Build: ISCC.exe /DAppVersion=0.1.3 /DSourceDir=<publish dir> /DOutputDir=<dir> build\installer\lumora.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir (folder with the published win-x64 launcher) is required
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; Never change AppId: Windows uses it to find the installed launcher for updates and uninstall.
AppId={{7C3E2A61-5B8D-4F0E-9C2A-4D1B6E8F3A90}
AppName=Lumora
AppVersion={#AppVersion}
AppVerName=Lumora {#AppVersion}
AppPublisher=Lumora
AppPublisherURL=https://github.com/kitneybean/lumora-launcher
DefaultDirName={localappdata}\Programs\Lumora
DefaultGroupName=Lumora
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Lumora-Setup-{#AppVersion}-win-x64
SetupIconFile=..\..\src\Lumora.Launcher.App\Assets\lumora.ico
UninstallDisplayIcon={app}\Lumora.exe
UninstallDisplayName=Lumora
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Lumora"; Filename: "{app}\Lumora.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Lumora"; Filename: "{app}\Lumora.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Lumora.exe"; Description: "{cm:LaunchProgram,Lumora}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Files left by self-updates (staging folders, logs next to the exe). Games and settings in %AppData%\Lumora stay.
Type: filesandordirs; Name: "{app}"
