; Music Scan Integrity installer (Inno Setup 6+).
;
; Built from the finished portable build: tools\publish.ps1 fills
; artifacts\publish, then this script wraps it. The installed and portable
; versions are therefore the same files.
;
;   iscc /DAppVersion=1.2.3 installer\MusicScanIntegrity.iss

#ifndef AppVersion
  #error Version is not set: pass /DAppVersion=1.2.3
#endif

#define AppName        "Music Scan Integrity"
#define AppExeName     "Music_Scan_Integrity.exe"
#define AppPublisher   "J-udgW05"
#define AppUrl         "https://github.com/J-udgW05/MusicScan"
#define SourceRoot     SourcePath + "..\"
#define PublishDir     SourceRoot + "artifacts\publish"
#define LicensePath    SourceRoot + "LICENSE.txt"

[Setup]
; The same AppId for every version, so setup finds and upgrades an existing
; installation instead of adding a second copy.
AppId={{7A3F2C48-9E51-4B6D-8C0A-2F4D6B1E9C73}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; 64-bit only; there is nothing to install on 32-bit Windows.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

; Installs into Program Files, which only an administrator can write to.
PrivilegesRequired=admin
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}

; The licence page appears only if the licence file exists. The build log note
; makes a silently missing page visible: otherwise the build passes and the
; wizard step is simply gone.
#if FileExists(LicensePath)
  #pragma message "Licence found, agreement page enabled: " + LicensePath
LicenseFile={#LicensePath}
#else
  #pragma message "NO LICENCE, the agreement page is skipped: " + LicensePath
#endif

OutputDir={#SourceRoot}artifacts
OutputBaseFilename=Music_Scan_Integrity-{#AppVersion}-setup
SetupIconFile={#SourceRoot}src\MusicScanIntegrity.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=yes

; If the application is running, setup offers to close it rather than failing
; on locked files.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenuicon"; Description: "{cm:CreateStartMenuIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[CustomMessages]
russian.CreateStartMenuIcon=Создать значок в меню Пуск
english.CreateStartMenuIcon=Create a Start Menu icon
russian.LaunchAfterInstall=Запустить {#AppName}
english.LaunchAfterInstall=Launch {#AppName}

[Files]
; The entire portable build as is: exe, .NET and BASS libraries, resources.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove config and data folders next to the program on uninstall. An installed
; copy normally keeps them in the user profile, since it cannot write to
; Program Files.
Type: filesandordirs; Name: "{app}\config"
Type: filesandordirs; Name: "{app}\data"
