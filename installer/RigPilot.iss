#ifndef PayloadDir
  #error PayloadDir is required. Build with package.ps1 -Installer.
#endif
#ifndef AppVersion
  #error AppVersion is required.
#endif
#ifndef OutputDir
  #error OutputDir is required.
#endif
#ifndef OutputName
  #error OutputName is required.
#endif

[Setup]
AppId={{7485FF70-F630-4D5C-AB81-EAE753CB7A82}
AppName=RigPilot
AppVersion={#AppVersion}
AppPublisher=RigPilot
AppPublisherURL=https://github.com/bk9tv/RigPilot
AppSupportURL=https://github.com/bk9tv/RigPilot/issues
AppUpdatesURL=https://github.com/bk9tv/RigPilot/releases
DefaultDirName={localappdata}\Programs\RigPilot
DefaultGroupName=RigPilot
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
AppMutex=Local\RigPilot-{username}
CloseApplications=no
RestartApplications=no
SetupIconFile=..\assets\rigpilot.ico
UninstallDisplayIcon={app}\RigPilot.exe
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
VersionInfoVersion={#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknuepfung erstellen"; GroupDescription: "Verknuepfungen:"; Flags: unchecked

[Files]
; Explicit allowlist. Never install or remove the user's Daten directory.
Source: "{#PayloadDir}\RigPilot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\ANLEITUNG.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\UPDATE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\VERSION.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\RigPilot"; Filename: "{app}\RigPilot.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\RigPilot"; Filename: "{app}\RigPilot.exe"; WorkingDir: "{app}"; Tasks: desktopicon

; No automatic launch and no UninstallDelete entries: profiles stay on disk.
