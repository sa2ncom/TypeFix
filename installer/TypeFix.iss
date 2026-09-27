; TypeFix Windows installer.
; Per-user install, uninstall entry in Settings > Apps, desktop shortcut
; Finished page: desktop shortcut and launch checkboxes, both checked by default.

#define AppName "TypeFix"
#define AppVersion "1.0.0"
#define AppPublisher "TypeFix"
#define AppExe "TypeFix.exe"
#define AppId "{{7E4A9C2D-1B6F-4E83-A5D0-9C8F2E6B41A3}"

#ifndef Arch
  #define Arch "x64"
#endif

#if Arch == "x64"
  #define PublishRid "win-x64"
  #define ArchAllowed "x64compatible"
#elif Arch == "x86"
  #define PublishRid "win-x86"
  #define ArchAllowed "x86compatible"
#elif Arch == "arm64"
  #define PublishRid "win-arm64"
  #define ArchAllowed "arm64"
#else
  #error Unknown Arch. Pass /DArch=x64, /DArch=x86, or /DArch=arm64.
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=TypeFix-{#AppVersion}-Setup-{#Arch}
SetupIconFile=..\TypeFix.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed={#ArchAllowed}
#if Arch != "x86"
ArchitecturesInstallIn64BitMode={#ArchAllowed}
#endif
MinVersion=10.0
CloseApplications=yes
CloseApplicationsFilter={#AppExe}
RestartApplications=no
UsePreviousAppDir=yes
DisableWelcomePage=no
DisableFinishedPage=no
AllowNoIcons=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "persian"; MessagesFile: "Languages\Persian.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "chinese"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
english.LaunchProgram=Launch TypeFix
persian.DesktopShortcut=ساخت میانبر روی دسکتاپ
persian.LaunchProgram=اجرای TypeFix
arabic.DesktopShortcut=إنشاء اختصار على سطح المكتب
arabic.LaunchProgram=تشغيل TypeFix
french.DesktopShortcut=Créer un raccourci sur le bureau
french.LaunchProgram=Lancer TypeFix
italian.DesktopShortcut=Crea un collegamento sul desktop
italian.LaunchProgram=Avvia TypeFix
spanish.DesktopShortcut=Crear un acceso directo en el escritorio
spanish.LaunchProgram=Iniciar TypeFix
turkish.DesktopShortcut=Masaüstüne kısayol oluştur
turkish.LaunchProgram=TypeFix'i çalıştır
chinese.DesktopShortcut=在桌面创建快捷方式
chinese.LaunchProgram=启动 TypeFix
korean.DesktopShortcut=바탕 화면에 바로 가기 만들기
korean.LaunchProgram=TypeFix 실행

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram}"; Flags: nowait postinstall; Check: ShouldLaunch

[Files]
Source: "..\publish\{#PublishRid}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#AppName}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{#AppName}"

[Code]
var
  DesktopShortcutCheck: TNewCheckBox;
  FinishedRunListTop: Integer;
  FinishedLayoutReady: Boolean;

function CmdLineHas(const Param: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Param) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

function DesktopShortcutPath: String;
begin
  Result := ExpandConstant('{userdesktop}\{#AppName}.lnk');
end;

procedure RemoveDesktopShortcut;
begin
  if FileExists(DesktopShortcutPath) then
  begin
    Log('Removing desktop shortcut: ' + DesktopShortcutPath);
    DeleteFile(DesktopShortcutPath);
  end;
end;

function ShouldLaunch: Boolean;
begin
  if WizardSilent then
    Result := not CmdLineHas('/NOLAUNCH')
  else
    Result := True;
end;

procedure PlaceFinishedChecks;
begin
  if not FinishedLayoutReady then
  begin
    FinishedRunListTop := WizardForm.RunList.Top;
    FinishedLayoutReady := True;
  end;

  DesktopShortcutCheck.Caption := CustomMessage('DesktopShortcut');
  DesktopShortcutCheck.Left := WizardForm.RunList.Left;
  DesktopShortcutCheck.Width := WizardForm.RunList.Width;
  if DesktopShortcutCheck.Width < ScaleX(180) then
    DesktopShortcutCheck.Width := WizardForm.FinishedPage.ClientWidth - ScaleX(40);
  DesktopShortcutCheck.Top := FinishedRunListTop;
  DesktopShortcutCheck.Height := ScaleY(20);
  DesktopShortcutCheck.Visible := True;

  WizardForm.RunList.Top := DesktopShortcutCheck.Top + DesktopShortcutCheck.Height + ScaleY(6);
  WizardForm.RunList.Height := ScaleY(40);
  WizardForm.RunList.Visible := True;
end;

procedure InitializeWizard;
begin
  DesktopShortcutCheck := TNewCheckBox.Create(WizardForm);
  DesktopShortcutCheck.Parent := WizardForm.FinishedPage;
  DesktopShortcutCheck.Caption := CustomMessage('DesktopShortcut');
  DesktopShortcutCheck.Checked := True;
  PlaceFinishedChecks;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    PlaceFinishedChecks;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { The desktop icon is created with the other icons. Silent setup skips the
    finished page, so /NODESKTOPICON removes it. The checkbox defaults to checked. }
  if (CurStep = ssPostInstall) and WizardSilent and CmdLineHas('/NODESKTOPICON') then
    RemoveDesktopShortcut;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpFinished) and (not WizardSilent) and (not DesktopShortcutCheck.Checked) then
    RemoveDesktopShortcut;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RunValue, InstallExe: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  InstallExe := '"' + ExpandConstant('{app}\{#AppExe}') + '"';
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}', RunValue) then
    if CompareText(RunValue, InstallExe) = 0 then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');
end;
