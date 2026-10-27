#ifndef AppVersion
  #error "Passe /DAppVersion=x.y.z (o build.ps1 faz isso)"
#endif
#ifndef PublishDir
  #error "Passe /DPublishDir=<pasta do dotnet publish> (o build.ps1 faz isso)"
#endif
#ifndef RepoRoot
  #define RepoRoot ".."
#endif

[Setup]
AppId={{8C7D1E52-4B0A-4F3E-9A61-3E5D2C9B7A10}
AppName=m1vClock
AppVersion={#AppVersion}
AppVerName=m1vClock {#AppVersion}
AppPublisher=m1vClock
VersionInfoVersion={#AppVersion}
VersionInfoProductName=m1vClock
DefaultDirName={autopf}\m1vClock
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile={#RepoRoot}\src\M1vClock.App\Assets\m1vClock.ico
UninstallDisplayIcon={app}\m1vClock.exe
UninstallDisplayName=m1vClock
OutputDir={#RepoRoot}\artifacts\installer
OutputBaseFilename=m1vClock-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ptbr.RemoveData=Apagar também as configurações, os logs e o histórico de alterações do m1vClock?%n%nSe você apagar, não dá mais para desfazer pelo m1vClock as mudanças que ele fez no Windows.
en.RemoveData=Also delete m1vClock settings, logs and change history?%n%nIf you delete them, the changes m1vClock made to Windows can no longer be undone from m1vClock.
ptbr.LaunchApp=Abrir o m1vClock
en.LaunchApp=Open m1vClock

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\m1vClock"; Filename: "{app}\m1vClock.exe"
Name: "{autodesktop}\m1vClock"; Filename: "{app}\m1vClock.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\m1vClock.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  DataDir := ExpandConstant('{commonappdata}\m1vClock');
  if UninstallSilent or not DirExists(DataDir) then
    Exit;
  if MsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataDir, True, True, True);
end;
