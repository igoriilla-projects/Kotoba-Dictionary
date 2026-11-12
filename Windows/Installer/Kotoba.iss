#ifndef PublishDir
  #define PublishDir "..\..\build\windows\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\build\windows"
#endif
[Setup]
AppId={{D79E176B-01ED-4C35-96BA-4B70471922D8}
AppName=Kotoba
AppVersion=1.4.1
AppPublisher=Kotoba
DefaultDirName={localappdata}\Programs\Kotoba
DefaultGroupName=Kotoba
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=Kotoba-Setup-x64
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Kotoba.exe
CloseApplications=yes
RestartApplications=no
CreateUninstallRegKey=not IsTestMode
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\Kotoba"; Filename: "{app}\Kotoba.exe"; Check: not IsTestMode
[Registry]
Root: HKCU; Subkey: "Software\Classes\yarxi-study"; ValueType: string; ValueName: ""; ValueData: "URL:Kotoba entry"; Flags: uninsdeletekey; Check: not IsTestMode
Root: HKCU; Subkey: "Software\Classes\yarxi-study"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Check: not IsTestMode
Root: HKCU; Subkey: "Software\Classes\yarxi-study\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Kotoba.exe"" ""%1"""; Check: not IsTestMode
[Run]
Filename: "{app}\Kotoba.exe"; Description: "Открыть Kotoba"; Flags: nowait postinstall skipifsilent
[Code]
function IsTestMode: Boolean;
begin
  Result := ExpandConstant('{param:TESTMODE|0}') = '1';
end;
