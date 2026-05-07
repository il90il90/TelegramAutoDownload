#define AppName "Telegram Auto Download"
#define AppVersion "2.5.1"
#define AppPublisher "TelegramAutoDownload"
#define AppURL "https://github.com/il90il90/TelegramAutoDownload"
#define AppExeName "TelegramAutoDownload.exe"
#define SourceDir "..\publish_out"
#define OutputDir ".."
#define OutputName "TelegramAutoDownload_v2.5.1_Setup"

[Setup]
AppId={{A3F2C8E1-4D7B-4E9A-B5C0-1234567890AB}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
LicenseFile=
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile=..\TelegramAutoDownload\044a51cf-2359-480c-8c20-bf372f8a4cab.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
DisableWelcomePage=no
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} v{#AppVersion}
MinVersion=10.0.17763
; Require .NET 8 Desktop Runtime
CloseApplications=yes
CloseApplicationsFilter=*TelegramAutoDownload.exe*

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";    Description: "{cm:CreateDesktopIcon}";      GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon";    Description: "Start automatically with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Main executable (self-contained — .NET runtime is bundled inside)
Source: "{#SourceDir}\{#AppExeName}";                   DestDir: "{app}"; Flags: ignoreversion

; WPF native runtime DLLs (cannot be bundled inside the single-file exe)
Source: "{#SourceDir}\*.dll";                           DestDir: "{app}"; Flags: ignoreversion

; Debug symbols (optional)
Source: "{#SourceDir}\*.pdb";                           DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; Plugins subfolder
Source: "{#SourceDir}\Plugins\*";                      DestDir: "{app}\Plugins"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; Localisation resources (MahApps etc.)
Source: "{#SourceDir}\de\*";                           DestDir: "{app}\de"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}";                     Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}";           Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";               Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Windows startup (optional task)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "TelegramAutoDownload"; \
  ValueData: """{app}\{#AppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startupicon

[Run]
; Launch app after install — works both in GUI mode (as a checkbox) and silent/auto-update mode
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/f /im {#AppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
// The app is published as a self-contained single-file executable.
// No external .NET runtime is required — everything is bundled inside TelegramAutoDownload.exe.
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
