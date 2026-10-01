[Setup]
AppId={{C8E1D942-7F3A-4B2E-9D1B-8A7E4F2C1B0D}
AppName=Nokto
AppVersion=1.0.0
AppPublisher=Nokto Project
AppPublisherURL=https://github.com/nokto/nokto
AppSupportURL=https://github.com/nokto/nokto/issues
DefaultDirName={autopf}\Nokto
DefaultGroupName=Nokto
DisableProgramGroupPage=yes
OutputDir=..\..\artifacts\installer
OutputBaseFilename=Nokto-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Iniciar Nokto con Windows"; GroupDescription: "Opciones de inicio:"

[Files]
Source: "..\..\publish\win-x64\Nokto.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Nokto"; Filename: "{app}\Nokto.exe"
Name: "{autodesktop}\Nokto"; Filename: "{app}\Nokto.exe"; Tasks: desktopicon
Name: "{userstartup}\Nokto"; Filename: "{app}\Nokto.exe"; Tasks: startup

[Run]
Filename: "{app}\Nokto.exe"; Description: "{cm:LaunchProgram,Nokto}"; Flags: nowait postinstall skipifsilent
