[Setup]
AppName=ReControl
AppVersion=1.0
DefaultDirName={pf}\ReControl
DefaultGroupName=ReControl
OutputDir=InstallerOutput
Compression=lzma
SolidCompression=yes

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\ReControl"; Filename: "{app}\ReControl.exe"
Name: "{autodesktop}\ReControl"; Filename: "{app}\ReControl.exe"

[Tasks]
Name: "autostart"; Description: "Start ReControl when Windows starts"; GroupDescription: "Additional options:"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
ValueType: string; ValueName: "ReControl"; \
ValueData: """{app}\ReControl.exe"""; Tasks: autostart
