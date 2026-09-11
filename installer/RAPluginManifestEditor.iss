; RA Plugin Manifest Editor -- Inno Setup script
;
; The real installer. Replaces shipping the raw self-contained publish
; renamed to .exe: this gets the app into Program Files, adds Start Menu +
; (optional) Desktop shortcuts, writes a proper Add/Remove Programs entry,
; and -- because the app is published framework-dependent, not self-contained
; -- makes sure the .NET 10 Runtime is actually on the machine before handing
; control to it. (Avalonia needs Microsoft.NETCore.App, not the WPF/WinForms
; -specific Microsoft.WindowsDesktop.App -- there is no UseWPF/UseWindowsForms
; here, so the plain runtime is the smaller, correct ask.)
;
; One script, two flavours, selected with /DFlavor=<name>:
;
;   Web      (default) small download; if the runtime is missing, fetches the
;            official installer at setup time and runs it silently. Needs
;            internet during install.
;   Offline  bundles the runtime installer inside this .exe. No internet
;            needed, ~30 MB larger.
;
; Build with:  build-installer.ps1
;   or direct: ISCC.exe /DMyAppVersion=1.2.0 /DFlavor=Offline RAPluginManifestEditor.iss
; Requires Inno Setup 6+: https://jrsoftware.org/isdl.php
;
; Expects the framework-dependent publish at:
;   ..\bin\Release\net10.0\win-x64\publish\
; (dotnet publish -c Release -r win-x64 -p:SelfContained=false
;  -p:RollForward=LatestMinor -- see build-installer.ps1, which passes these;
;  the .csproj stays RID-agnostic so the existing macOS self-contained publish
;  commands in README.md are unaffected.)
;
; Multi-file, not single-file: PublishSingleFile isn't used here because
; Avalonia's native libs (Skia, HarfBuzz, ANGLE) ship alongside the managed
; assemblies either way, so there is no size win from bundling, and a plain
; folder copy is simpler than a self-extracting exe. The publish folder also
; carries two large native-lib .pdb files (~105 MB combined) that are pure
; debug symbols, never needed at runtime -- excluded below.
;
; Deliberately minimal UI, matching the other Six Walls installers: no
; welcome page, no folder picker, no component tree. Double-click -> one UAC
; prompt -> (first run only) a short runtime-fetch step -> done.

#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif
#ifndef Flavor
  #define Flavor "Web"
#endif
#if (Flavor != "Web") && (Flavor != "Offline")
  #error Flavor must be Web or Offline
#endif

#define MyAppGuid       "B51223F2-69F9-4009-BF39-39583E629C34"
#define MyAppName       "RA Plugin Manifest Editor"
#define MyPublisher     "Six Walls"
#define MyExeName       "RAPluginManifestEditor.exe"
#define PublishDir      "..\bin\Release\net10.0\win-x64\publish"
; aka.ms/dotnet/<channel>/dotnet-runtime-win-x64.exe always resolves to the
; latest patch on that channel -- same stable-link pattern already relied on
; for the WindowsDesktop variant in the other Six Walls installers.
#define DotNetChannel   "10.0"
#define DotNetBootstrapUrl "https://aka.ms/dotnet/" + DotNetChannel + "/dotnet-runtime-win-x64.exe"

[Setup]
AppId={{{#MyAppGuid}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyPublisher}
DefaultDirName={autopf}\{#MyPublisher}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
; Ready page is hidden for a fresh install and shown (button = "Update") for
; an upgrade -- see update-aware.iss.
DisableReadyPage=no
DisableFinishedPage=no
UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\{#MyExeName}
OutputDir=dist
OutputBaseFilename=RA-Plugin-Manifest-Editor-{#MyAppVersion}-Windows-{#Flavor}
SetupIconFile=six-walls.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
#if Flavor == "Offline"
; Fetched by build-installer.ps1 into installer\vendor\ (gitignored) before
; ISCC runs -- not committed, and not present at all for a Web-flavour build.
Source: "vendor\dotnet-runtime-win-x64.exe"; DestDir: "{tmp}"; Flags: dontcopy
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyExeName}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\{#MyPublisher}\{#MyAppName}"; ValueType: string; \
    ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

[Run]
#if Flavor == "Offline"
; /passive (not /quiet) so the runtime installer shows its own progress bar --
; with /quiet there is nothing on screen for the several seconds this step
; can take, and that reads as a hang.
Filename: "{tmp}\dotnet-runtime-win-x64.exe"; Parameters: "/install /passive /norestart"; \
    StatusMsg: "Installing the .NET Runtime (one-time)..."; \
    Check: NeedsDotNetRuntimeOffline; Flags: waituntilterminated
#else
; $ProgressPreference = 'SilentlyContinue' matters, not just style: PowerShell's
; default Invoke-WebRequest progress-bar rendering is a well-known perf bug that
; can make a download 10-100x slower, which reads as a lock-up. /passive (not
; /quiet) on the runtime install itself for the same reason as the Offline
; branch above -- something visibly happening beats silence.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""$ProgressPreference = 'SilentlyContinue'; $ErrorActionPreference = 'Stop'; $u = '{#DotNetBootstrapUrl}'; $p = Join-Path $env:TEMP 'sixwalls-dotnet-runtime.exe'; Invoke-WebRequest -Uri $u -OutFile $p -UseBasicParsing; Start-Process -FilePath $p -ArgumentList '/install','/passive','/norestart' -Wait"""; \
    StatusMsg: "Getting the .NET Runtime (one-time, ~30 MB -- this can take a minute)..."; \
    Check: NeedsDotNetRuntime; Flags: waituntilterminated
#endif
Filename: "{app}\{#MyExeName}"; Description: "Launch {#MyAppName}"; Flags: postinstall skipifsilent nowait

#include "update-aware.iss"

[Code]
{ True when no installed .NET Runtime is on the 10.x line -- our net10.0 /
  RollForward=LatestMinor build needs exactly major 10; a 10.x of any
  patch/minor satisfies it, an only-.NET-11 machine does not. }
function NeedsDotNetRuntime(): Boolean;
var
  Names: TArrayOfString;
  I, Major, DotPos: Integer;
begin
  Result := True;
  if RegGetValueNames(HKLM64,
       'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App',
       Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      DotPos := Pos('.', Names[I]);
      if DotPos > 0 then
        Major := StrToIntDef(Copy(Names[I], 1, DotPos - 1), 0)
      else
        Major := StrToIntDef(Names[I], 0);
      if Major = 10 then
      begin
        Result := False;
        Exit;
      end;
    end;
  end;
end;

#if Flavor == "Offline"
{ Same check, but also extracts the bundled runtime installer out of the
  setup .exe first -- "dontcopy" files are never extracted automatically,
  only on demand via ExtractTemporaryFile, so this has to happen before the
  Run entry above tries to launch it. Only called once, right before that
  entry's Check decides whether to run it. }
function NeedsDotNetRuntimeOffline(): Boolean;
begin
  Result := NeedsDotNetRuntime();
  if Result then
    ExtractTemporaryFile('dotnet-runtime-win-x64.exe');
end;
#endif
