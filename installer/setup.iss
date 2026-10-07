; InkGuide installer (Inno Setup 6). Per-user install, no admin required.
; Build the payload first with build\publish.cmd, then compile:
;   iscc installer\setup.iss /DAppVersion=0.1.0 /DSourceDir=dist\InkGuide-0.1.0-win-x64
#define AppVersion "0.1.0"
#ifndef SourceDir
  #define SourceDir "..\dist\InkGuide-0.1.0-win-x64"
#endif

[Setup]
AppId=inkguide.desktop
AppName=InkGuide
AppVersion={#AppVersion}
AppVerName=InkGuide {#AppVersion}
AppPublisher=InkGuide contributors
DefaultDirName={localappdata}\Programs\InkGuide
PrivilegesRequired=lowest
UsedUserAreasMode=true
OutputDir=..\dist
OutputBaseFilename=InkGuide-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=InkGuide
DisableWelcomePage=no
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Optional WebView2 Evergreen bootstrapper: drop MicrosoftEdgeWebview2Setup.exe
; next to this script and it will be offered at the end of setup.
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: ignoreversion skipifsourcedoesntexist deleteafterinstall

[Icons]
Name: "{userprograms}\InkGuide"; Filename: "{app}\InkGuide.exe"
Name: "{userdesktop}\InkGuide"; Filename: "{app}\InkGuide.exe"; Tasks: desktopicon

[Tasks]
Name: desktopicon; Description: "Create a &desktop shortcut"; Flags: unchecked

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; StatusMsg: "Installing WebView2 Runtime..."; Check: WebView2BootstrapperExists; Flags: waituntilterminated
Filename: "{app}\InkGuide.exe"; Description: "Launch InkGuide"; Flags: nowait postinstall skipifsilent

[Code]
function WebView2BootstrapperExists(): Boolean;
begin
  Result := FileExists(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\InkGuide');
    if MsgBox('Remove InkGuide user data (settings, cache, logs)?' + #13#10 +
              DataDir + #13#10 +
              'Your files on Google Drive are NOT affected.',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(DataDir, True, True, True);
    end;
  end;
end;
