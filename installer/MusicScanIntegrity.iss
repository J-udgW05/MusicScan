; Установщик Music Scan Integrity (Inno Setup 6+).
;
; Собирается из уже готовой переносимой сборки: сначала tools\publish.ps1
; кладёт всё в artifacts\publish, потом этот сценарий заворачивает её в setup.
; Так установленная и переносимая версии — заведомо одни и те же файлы.
;
;   iscc /DAppVersion=1.2.3 installer\MusicScanIntegrity.iss

#ifndef AppVersion
  #error Версия не задана: передайте /DAppVersion=1.2.3
#endif

#define AppName        "Music Scan Integrity"
#define AppExeName     "Music_Scan_Integrity.exe"
#define AppPublisher   "J-udgW05"
#define AppUrl         "https://github.com/J-udgW05/MusicScan"
#define SourceRoot     SourcePath + "..\"
#define PublishDir     SourceRoot + "artifacts\publish"
#define LicensePath    SourceRoot + "LICENSE.txt"

[Setup]
; Один и тот же код на все версии: по нему установщик находит прежнюю
; установку и обновляет её, а не плодит вторую копию рядом.
AppId={{7A3F2C48-9E51-4B6D-8C0A-2F4D6B1E9C73}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; Программа только 64-битная — на 32-битной Windows её ставить нечем.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

; Ставится в Program Files, а туда пишет только администратор.
PrivilegesRequired=admin
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}

; Страница соглашения появляется, только если файл лицензии на месте. Отметка
; в журнале сборки нужна затем, что молча пропущенная страница выглядит ровно
; так же, как её отсутствие: сборка проходит, а шага в мастере нет.
#if FileExists(LicensePath)
  #pragma message "Лицензия найдена, страница соглашения включена: " + LicensePath
LicenseFile={#LicensePath}
#else
  #pragma message "ЛИЦЕНЗИИ НЕТ, страницы соглашения не будет: " + LicensePath
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

; Если программа запущена, установщик предложит её закрыть, а не упрётся
; в занятые файлы.
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
; Вся переносимая сборка как есть: exe, библиотеки .NET и BASS, ресурсы.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Настройки и база истории установленной программы лежат в профиле
; пользователя и удаляются вместе с ней. Папки рядом с программой пусты:
; в Program Files она писать не может и туда ничего не кладёт.
Type: filesandordirs; Name: "{app}\config"
Type: filesandordirs; Name: "{app}\data"
