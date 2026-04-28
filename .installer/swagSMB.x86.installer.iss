#define AppName "swagSMB"
#define AppDisplayName "swagSMB (x86)"
#ifndef AppVersion
#define AppVersion "0.0.0"
#endif
#define AppPublisher "fosterbarnes"
#define AppURL "https://github.com/fosterbarnes/swagSMB"
#define ExeName "swagSMB.exe"

[Setup]
AppId={{8F3C2A91-4E0B-5D7F-A6C2-1B9E4D8F0A3C}
AppName={#AppName}
UninstallDisplayName={#AppDisplayName}
AppVersion={#AppVersion}
DisableDirPage=auto
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={localappdata}\{#AppName}\app
UninstallDisplayIcon={app}\{#ExeName}
ArchitecturesAllowed=x86compatible
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
OutputDir=Output
OutputBaseFilename=swagSMB-x86-installer
SetupIconFile=..\.resources\icon\swag.ico
SolidCompression=yes
WizardStyle=classic dark

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupWindowTitle=swagSMB v{#AppVersion} installer (x86)

[CustomMessages]
CreateStartMenuIcon=Create Start Menu shortcut

[Tasks]
Name: "desktopicon"; Description: "Create Desktop shortcut"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "{cm:CreateStartMenuIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\win-x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
