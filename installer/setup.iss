#define AppName "Telegram Auto Download"
#define AppVersion "2.2.2"
#define AppPublisher "TelegramAutoDownload"
#define AppURL "https://github.com/il90il90/TelegramAutoDownload"
#define AppExeName "TelegramAutoDownload.exe"
#define SourceDir "..\publish_out"
#define OutputDir ".."
#define OutputName "TelegramAutoDownload_v2.2.2_Setup"

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
; Main application files
Source: "{#SourceDir}\{#AppExeName}";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.dll";                   DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.json";                  DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.pdb";                   DestDir: "{app}"; Flags: ignoreversion

; Localization resources
Source: "{#SourceDir}\de\*";                    DestDir: "{app}\de";                    Flags: ignoreversion recursesubdirs createallsubdirs

; Plugin DLLs are in the root alongside the main exe (loaded dynamically at runtime)
; yt-dlp is downloaded automatically on first run to %APPDATA%\TelegramAutoDownload\tools\

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
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/f /im {#AppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
// Check for .NET 8 Desktop Runtime
function IsDotNet8Installed(): Boolean;
var
  Key: string;
begin
  Key := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  Result := RegKeyExists(HKLM, Key) or RegKeyExists(HKCU, Key);
  if not Result then
  begin
    // Fallback: check if exe can be found
    Result := FileExists(ExpandConstant('{pf64}\dotnet\dotnet.exe')) or
              FileExists(ExpandConstant('{pf}\dotnet\dotnet.exe'));
  end;
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not IsDotNet8Installed() then
  begin
    if MsgBox('.NET 8 Desktop Runtime is required but was not found.' + #13#10 +
              'The installer will open the .NET download page.' + #13#10 + #13#10 +
              'After installing .NET 8, run this installer again.' + #13#10 + #13#10 +
              'Open download page now?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open',
        'https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.0-windows-x64-installer',
        '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;
    Result := False;
  end;
end;
