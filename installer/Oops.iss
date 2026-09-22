; Inno Setup script for oops
; Build prerequisites:
;   1) Run `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
;      in repo root. Output ends up in:
;      Oops\bin\Release\net8.0-windows\win-x64\publish\
;   2) Install Inno Setup 6: https://jrsoftware.org/isinfo.php
;   3) Compile this file with iscc.exe (or open in Inno Setup IDE and press F9)
;
; Alternatively run installer\build.ps1 which does both steps automatically.

#define MyAppName       "oops"
; Версию можно передать снаружи: ISCC /DMyAppVersion=1.2.3 (так делает CI).
#ifndef MyAppVersion
  #define MyAppVersion  "1.3.0"
#endif
#define MyAppPublisher  "oops"
#define MyAppURL        "https://github.com/vladvysotsky/oops"
#define MyAppExeName    "oops.exe"
#define PublishDir      "..\Oops\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{7F3C1E42-9A6D-4B58-8E0F-2C5D74A19B33}
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
OutputBaseFilename=oops-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\Oops\Resources\icon.ico
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

; Свои строки — обязательно через [CustomMessages], а не текстом в Description.
; Установщик уже двуязычный ([Languages] выше), и зашитая русская строка в
; английской установке выглядела ровно тем, чем является: недоделкой.
[CustomMessages]
russian.AutostartTask=Запускать при входе в Windows
russian.LaunchTask=Запустить {#MyAppName} после установки
russian.ExtraOptions=Дополнительные опции:
russian.UninstallIcon=Удалить {#MyAppName}
russian.LaunchAfterInstall=Запустить {#MyAppName}
english.AutostartTask=Start when Windows starts
english.LaunchTask=Run {#MyAppName} after installation
english.ExtraOptions=Additional options:
english.UninstallIcon=Uninstall {#MyAppName}
english.LaunchAfterInstall=Run {#MyAppName}

[Tasks]
Name: "desktopicon";    Description: "{cm:CreateDesktopIcon}";  GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart";      Description: "{cm:AutostartTask}"; GroupDescription: "{cm:ExtraOptions}"; Flags: unchecked
Name: "launchonfinish"; Description: "{cm:LaunchTask}"; GroupDescription: "{cm:ExtraOptions}";

[Files]
; В publish либо один exe, либо папка с зависимостями — забираем всё
; содержимое в любом случае.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";                  Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallIcon}";            Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";            Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Опциональный автозапуск через реестр.
;
; Check следит, чтобы автозапусков не стало ДВА. В настройках можно выбрать
; «ранний» способ — задачу в планировщике; если она есть, а установщик допишет
; ещё и запись в Run, программа при входе будет запускаться дважды. Вторая
; копия упрётся в single-instance mutex и покажет окно «программа уже
; запущена» — при каждом входе в систему.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "Oops"; ValueData: """{app}\{#MyAppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart; Check: NoSchedulerTask

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchAfterInstall}"; \
    Flags: nowait postinstall skipifsilent; Tasks: launchonfinish

[UninstallRun]
; Гарантированно прибиваем процесс перед удалением (на случай если CloseApplications не сработал)
Filename: "{cmd}"; Parameters: "/c taskkill /f /im {#MyAppExeName} 2>nul"; Flags: runhidden

[Code]
// Есть ли уже задача автозапуска в планировщике. Имя обязано совпадать с
// Autostart.TaskName в коде программы.
function NoSchedulerTask: Boolean;
var
  ResultCode: Integer;
begin
  if Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN "Oops Autostart"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := ResultCode <> 0      // 0 значит задача есть — запись в Run не нужна
  else
    Result := True;                // не смогли спросить — ведём себя как раньше
end;
