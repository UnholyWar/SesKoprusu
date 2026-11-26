[Setup]
AppName=SesKöprüsü
AppVersion=1.0
DefaultDirName={autopf}\SesKoprusu
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=SesKoprusu-Setup
[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs
Source: "{#GetEnv('SESKOPRUSU_DRIVER')}\*"; DestDir: "{app}\drivers\VB-CABLE"; Flags: recursesubdirs
[Icons]
Name: "{commonprograms}\SesKöprüsü"; Filename: "{app}\SesKoprusu.exe"
[Run]
Filename: "{app}\drivers\VB-CABLE\VBCABLE_Setup_x64.exe"; StatusMsg: "Installing VB-CABLE…"; Flags: waituntilterminated
