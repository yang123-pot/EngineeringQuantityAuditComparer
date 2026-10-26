#ifndef PublishDir
  #error PublishDir must point to the portable publish directory.
#endif
#ifndef InstallerOutputDir
  #error InstallerOutputDir must point to the setup output directory.
#endif
#ifndef AppVersion
  #error AppVersion must be supplied by Publish.ps1.
#endif
#ifndef MigrationDir
  #error MigrationDir must point to the legacy migration helper.
#endif

[Setup]
AppId={{82C327EF-4957-49C4-B380-66BA7CD07321}
AppName=工程量审减分析助手
AppVersion={#AppVersion}
AppVerName=工程量审减分析助手 {#AppVersion}
AppPublisher=yang123-pot
AppPublisherURL=https://github.com/yang123-pot/EngineeringQuantityAuditComparer
AppSupportURL=https://github.com/yang123-pot/EngineeringQuantityAuditComparer/issues
DefaultDirName={localappdata}\Programs\工程量审减分析助手
DefaultGroupName=工程量审减分析助手
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
PrivilegesRequired=lowest
UsePreviousAppDir=yes
UninstallDisplayIcon={app}\工程量审减分析助手.exe
SetupIconFile=..\assets\AppIcon.ico
WizardImageFile=..\assets\InstallerWizard.png
WizardSmallImageFile=..\assets\AppIcon.png
OutputDir={#InstallerOutputDir}
OutputBaseFilename=工程量审减分析助手-{#AppVersion}-安装程序
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=工程量审减分析助手安装程序
VersionInfoCompany=yang123-pot

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#MigrationDir}\LegacyClickOnceMigration.exe"; Flags: dontcopy noencryption
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\工程量审减分析助手"; Filename: "{app}\工程量审减分析助手.exe"; WorkingDir: "{app}"; IconFilename: "{app}\工程量审减分析助手.exe"
Name: "{autodesktop}\工程量审减分析助手"; Filename: "{app}\工程量审减分析助手.exe"; WorkingDir: "{app}"; IconFilename: "{app}\工程量审减分析助手.exe"

[Code]
var
  LegacyWasRemoved: Boolean;
  LegacyDetectionError: String;

const
  LegacyUninstallRoot = 'Software\Microsoft\Windows\CurrentVersion\Uninstall';
  LegacyClickOncePrefix = 'rundll32.exe dfshim.dll,ShArpMaintain 工程量审减分析助手.app,';
  CurrentUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{82C327EF-4957-49C4-B380-66BA7CD07321}_is1';

function FindLegacyClickOnceEntry(var EntryPath: String; var UninstallCommand: String): Boolean;
var
  Keys: TArrayOfString;
  I: Integer;
  Candidate: String;
  DisplayName: String;
  Publisher: String;
  Command: String;
begin
  Result := False;
  LegacyDetectionError := '';
  if not RegGetSubkeyNames(HKCU, LegacyUninstallRoot, Keys) then
    Exit;

  for I := 0 to GetArrayLength(Keys) - 1 do
  begin
    Candidate := LegacyUninstallRoot + '\' + Keys[I];
    if not RegQueryStringValue(HKCU, Candidate, 'DisplayName', DisplayName) then
      Continue;
    if DisplayName <> '工程量审减分析助手' then
      Continue;
    if not RegQueryStringValue(HKCU, Candidate, 'Publisher', Publisher) or
      not RegQueryStringValue(HKCU, Candidate, 'UninstallString', Command) then
    begin
      LegacyDetectionError := '检测到旧版，但其卸载信息不完整。为避免新旧版共存，本次安装已停止。请先从 Windows“已安装的应用”卸载旧版后重试。';
      Exit;
    end;
    if ((Publisher <> 'yang123-pot') and (Publisher <> '内部使用')) or
      (Pos(LegacyClickOncePrefix, Command) <> 1) then
    begin
      LegacyDetectionError := '检测到同名旧版，但无法确认其自动卸载信息。为避免新旧版共存，本次安装已停止。请先从 Windows“已安装的应用”卸载旧版后重试。';
      Exit;
    end;

    EntryPath := Candidate;
    UninstallCommand := Command;
    Result := True;
    Exit;
  end;
end;

function RemovePreviousLocation: String;
var
  PreviousDir: String;
  UninstallCommand: String;
  UninstallExe: String;
  ExitCode: Integer;
begin
  Result := '';
  if not RegKeyExists(HKCU, CurrentUninstallKey) then
    Exit;
  if not RegQueryStringValue(HKCU, CurrentUninstallKey, 'InstallLocation', PreviousDir) then
  begin
    Result := '无法读取原安装目录，本次安装已停止。请先从 Windows“已安装的应用”卸载当前版本后重试。';
    Exit;
  end;
  if CompareText(AddBackslash(PreviousDir), AddBackslash(ExpandConstant('{app}'))) = 0 then
    Exit;
  if not RegQueryStringValue(HKCU, CurrentUninstallKey, 'UninstallString', UninstallCommand) then
  begin
    Result := '无法读取原版本卸载信息，本次安装已停止。';
    Exit;
  end;
  UninstallExe := RemoveQuotes(UninstallCommand);
  if (CompareText(AddBackslash(ExtractFileDir(UninstallExe)), AddBackslash(PreviousDir)) <> 0) or
    (Pos('unins', Lowercase(ExtractFileName(UninstallExe))) <> 1) or
    (CompareText(ExtractFileExt(UninstallExe), '.exe') <> 0) or
    not FileExists(UninstallExe) then
  begin
    Result := '原版本的卸载程序不可用，本次安装已停止。请先从 Windows“已安装的应用”卸载当前版本后重试。';
    Exit;
  end;
  Log('Moving installation from ' + PreviousDir + ' to ' + ExpandConstant('{app}'));
  if not Exec(UninstallExe, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
  begin
    Result := '无法卸载原安装目录中的版本，本次安装已停止。';
    Exit;
  end;
  if (ExitCode <> 0) or RegKeyExists(HKCU, CurrentUninstallKey) or
    FileExists(AddBackslash(PreviousDir) + '工程量审减分析助手.exe') then
    Result := '原安装目录中的版本未完全卸载，本次安装已停止。请关闭软件后重试。'
  else
    LegacyWasRemoved := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  EntryPath: String;
  Command: String;
  ExitCode: Integer;
begin
  Result := '';
  while FindLegacyClickOnceEntry(EntryPath, Command) do
  begin
    Log('Detected the known legacy ClickOnce installation: ' + EntryPath);
    ExtractTemporaryFile('LegacyClickOnceMigration.exe');
    if not Exec(ExpandConstant('{tmp}\LegacyClickOnceMigration.exe'),
      '"' + EntryPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    begin
      Result := '无法启动旧版自动迁移程序。新版尚未安装，请联系软件发布者。';
      Exit;
    end;

    if (ExitCode <> 0) or RegKeyExists(HKCU, EntryPath) then
    begin
      Result := '旧版自动迁移未完成（错误代码 ' + IntToStr(ExitCode) + '）。新版尚未安装；请联系软件发布者或先从 Windows“已安装的应用”卸载旧版。';
      Exit;
    end;
    LegacyWasRemoved := True;
  end;
  if LegacyDetectionError <> '' then
    Result := LegacyDetectionError
  else
    Result := RemovePreviousLocation;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectDir then
  begin
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
    WizardForm.SelectDirLabel.Caption :=
      '请选择安装磁盘和文件夹，然后点击“安装”。安装程序会自动处理旧版，并创建桌面快捷方式。';
    WizardForm.SelectDirBrowseLabel.Caption :=
      '可直接填写路径，或点击“浏览”选择磁盘和文件夹。';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  MigrationText: String;
begin
  if (CurStep <> ssPostInstall) or WizardSilent then
    Exit;

  MigrationText := '';
  if LegacyWasRemoved then
    MigrationText := '旧版已自动移除。' + #13#10;

  MsgBox('工程量审减分析助手 1.0.0.0 → {#AppVersion} 更新要点：' + #13#10 + #13#10 +
    '• 兼容新点江苏省 V10、V11 清单。' + #13#10 +
    '• 空编号、空描述的有效项目也参与对比。' + #13#10 +
    '• 新增审减分析报告，按楼栋列明核减／核增工程量与金额。' + #13#10 +
    '• 安装时可选择磁盘和文件夹，自动移除旧版并创建桌面图标。' + #13#10 +
    MigrationText + #13#10 +
    '详细内容见安装目录中的 更新说明-1.0.0.4.md。', mbInformation, MB_OK);
end;
