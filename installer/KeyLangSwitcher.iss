; Inno Setup script for KeyLangSwitcher
; Build prerequisites:
;   1) Run `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
;      in repo root. Output ends up in:
;      KeyLangSwitcher\bin\Release\net8.0-windows\win-x64\publish\
;   2) Install Inno Setup 6: https://jrsoftware.org/isinfo.php
;   3) Compile this file with iscc.exe (or open in Inno Setup IDE and press F9)
;
; Alternatively run installer\build.ps1 which does both steps automatically.

#define MyAppName       "KeyLangSwitcher"
; Версию можно передать снаружи: ISCC /DMyAppVersion=1.2.3 (так делает CI).
#ifndef MyAppVersion
  #define MyAppVersion  "0.1.0"
#endif
#define MyAppPublisher  "KeyLangSwitcher"
#define MyAppURL        "https://github.com/vladvysotsky/KeyLangSwitcher"
#define MyAppExeName    "KeyLangSwitcher.exe"
#define PublishDir      "..\KeyLangSwitcher\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{A1B2C3D4-E5F6-4789-ABCD-1234567890AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=KeyLangSwitcher-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\KeyLangSwitcher\Resources\icon.ico
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";    Description: "{cm:CreateDesktopIcon}";  GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart";      Description: "Запускать при входе в Windows"; GroupDescription: "Дополнительные опции:"; Flags: unchecked
Name: "launchonfinish"; Description: "Запустить {#MyAppName} после установки"; GroupDescription: "Дополнительные опции:";

[Files]
; Publish folder contains either a single-file exe or a folder with deps —
; either way пихаем всё содержимое.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";                  Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Удалить {#MyAppName}";          Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";            Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Опциональный автозапуск через реестр
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "KeyLangSwitcher"; ValueData: """{app}\{#MyAppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; \
    Flags: nowait postinstall skipifsilent; Tasks: launchonfinish

[UninstallRun]
; Гарантированно прибиваем процесс перед удалением (на случай если CloseApplications не сработал)
Filename: "{cmd}"; Parameters: "/c taskkill /f /im {#MyAppExeName} 2>nul"; Flags: runhidden
