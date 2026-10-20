#ifndef Bundle
  #error Supply /DBundle=absolute-path-to-clean-bundle
#endif
#ifndef ReleaseDir
  #error Supply /DReleaseDir=absolute-output-directory
#endif
#ifndef AppVersion
  #define AppVersion "0.1.1"
#endif

[Setup]
AppId={{95D7C18A-1DB0-4F64-8A84-4314BA71F0DB}
AppName=澄音 · ClearTone
AppVersion={#AppVersion}
AppPublisher=yantonglyu
AppPublisherURL=https://github.com/yantonglyu/ClearTone
AppSupportURL=https://github.com/yantonglyu/ClearTone/issues
AppUpdatesURL=https://github.com/yantonglyu/ClearTone/releases
DefaultDirName={localappdata}\Programs\ClearTone
DefaultGroupName=ClearTone
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#ReleaseDir}
OutputBaseFilename=ClearTone-{#AppVersion}-win-x64-Setup
SetupIconFile=..\src\ClearTone\Assets\AppIcon.ico
UninstallDisplayIcon={app}\app\ClearTone.exe
WizardStyle=modern light windows11 hidebevels
WizardBackColor=#fffaf8
WizardImageFile=assets\welcome.bmp
WizardSmallImageFile=assets\header.bmp
WizardImageStretch=yes
WizardSizePercent=115,110
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableReadyPage=yes
LicenseFile={#Bundle}\INSTALL-LICENSE.txt
DisableDirPage=no
ShowLanguageDialog=no
LanguageDetectionMethod=none
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
UninstallDisplayName=澄音 · ClearTone
VersionInfoDescription=ClearTone Windows installer
VersionInfoVersion={#AppVersion}.0

[Languages]
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[LangOptions]
DialogFontName=Microsoft YaHei UI
DialogFontSize=10
WelcomeFontName=Microsoft YaHei UI

[Messages]
zh.WelcomeLabel1=澄音，陪你听歌与专注。
zh.WelcomeLabel2=音乐轻轻流动，时间慢慢积累。%n%n沉浸歌词 · 网易云歌单 · 专注计时%n%n安装后登录你自己的账号，即可开始。%n学习记录保存在这台电脑上，卸载时也会保留。%n%n人声与节拍模型可在安装后单独配置。
zh.SelectDirLabel3=选择澄音的安装位置。账号与学习记录会单独保存在当前用户的数据目录。
zh.FinishedHeadingLabel=准备好了，听首喜欢的歌吧。
zh.FinishedLabel=澄音已安装完成。%n%n打开播放器，点击网易云图标登录，或从设置添加歌单与 B站音频。
zh.ButtonNext=继续(&N)
zh.ButtonInstall=安装澄音(&I)
zh.ClickNext=点击“继续”开始设置。
en.WelcomeLabel1=Music flows. Focus grows.
en.WelcomeLabel2=Immersive lyrics, your playlists, and a quiet focus timer.%n%nSign in with your own music account after installation.%nYour study records stay on this computer, even after uninstalling.%n%nVocal and beat models can be configured separately later.
en.FinishedHeadingLabel=Ready for your next favorite song.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#Bundle}\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Bundle}\runtime\*"; DestDir: "{app}\runtime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Bundle}\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Bundle}\analysis\*"; DestDir: "{app}\analysis"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Bundle}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Bundle}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bundle}\INSTALL-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bundle}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bundle}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bundle}\SECURITY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bundle}\setup-analysis.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ClearTone"; Filename: "{app}\app\ClearTone.exe"; WorkingDir: "{app}\app"; Check: not IsTestInstall
Name: "{autodesktop}\ClearTone"; Filename: "{app}\app\ClearTone.exe"; WorkingDir: "{app}\app"; Tasks: desktopicon; Check: not IsTestInstall

[Run]
Filename: "{app}\app\ClearTone.exe"; Description: "{cm:LaunchProgram,ClearTone}"; WorkingDir: "{app}\app"; Flags: nowait postinstall skipifsilent

[Code]
function IsTestInstall: Boolean;
begin
  Result := ExpandConstant('{param:TESTINSTALL|0}') = '1';
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Font.Size := 19;
  WizardForm.FinishedHeadingLabel.Font.Size := 18;
end;

// Never overwrite an unmanaged installation (which may include private data).
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then
    if DirExists(ExpandConstant('{app}')) and
       not FileExists(ExpandConstant('{app}\unins000.exe')) then
    begin
      MsgBox('请选择一个新的空目录，避免覆盖已有程序或个人数据。' + #13#10 +
        'Choose a new folder to protect an existing installation.', mbError, MB_OK);
      Result := False;
    end;
end;
