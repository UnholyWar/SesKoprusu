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
Source: "{#GetEnv('SESKOPRUSU_DRIVER')}\*"; DestDir: "{tmp}\VB-CABLE"; Flags: recursesubdirs deleteafterinstall
[Icons]
Name: "{commonprograms}\SesKöprüsü"; Filename: "{app}\SesKoprusu.exe"
[Run]
Filename: "{tmp}\VB-CABLE\VBCABLE_Setup_x64.exe"; StatusMsg: "VB-CABLE kuruluyor…"; Flags: waituntilterminated
