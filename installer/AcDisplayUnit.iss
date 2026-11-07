#define MyAppName "AC Display Unit"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Yefta Asyel"
#define MyAppURL "https://github.com/yeftakun/ac-hud"
#define MyAppExeName "AcHud.exe"

[Setup]
AppId={{D5B6D6F2-3A25-4D4D-A7F0-ACDISPLAYUNIT100}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL=https://github.com/yeftakun/ac-hud
AppUpdatesURL=https://github.com/yeftakun/ac-hud
DefaultDirName={autopf}\AC Display Unit
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=AC-Display-Unit-v{#MyAppVersion}-Setup
SetupIconFile=..\assets\logo.ico
UninstallDisplayIcon={app}\assets\logo.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "..\release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\AC Display Unit"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\assets\logo.ico"
Name: "{group}\AC Display Unit"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\assets\logo.ico"
Name: "{group}\Uninstall AC Display Unit"; Filename: "{uninstallexe}"

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AC Display Unit TCP 3001"" dir=in action=allow protocol=TCP localport=3001 profile=private,domain"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Launch AC Display Unit"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AC Display Unit TCP 3001"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallRule"
