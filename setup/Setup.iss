[Setup]
AppName=SesKöprüsü
AppVersion={#GetEnv('SESKOPRUSU_VERSION')}
UsePreviousAppDir=yes
CloseApplications=yes
DefaultDirName={autopf}\SesKoprusu
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=SesKoprusu-Setup
[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs
#if GetEnv("SESKOPRUSU_DRIVER") != ""
Source: "{#GetEnv('SESKOPRUSU_DRIVER')}\*"; DestDir: "{app}\drivers\VB-CABLE"; Flags: recursesubdirs
#endif
[Icons]
Name: "{commonprograms}\SesKöprüsü"; Filename: "{app}\SesKoprusu.exe"
#if GetEnv("SESKOPRUSU_DRIVER") != ""
[Run]
Filename: "{app}\drivers\VB-CABLE\VBCABLE_Setup_x64.exe"; StatusMsg: "Installing VB-CABLE…"; Flags: waituntilterminated
#endif
