param([Parameter(Mandatory)][string]$CablePackageDirectory)
$ErrorActionPreference = 'Stop'
$env:SESKOPRUSU_DRIVER = (Resolve-Path $CablePackageDirectory).Path
if (!(Test-Path "$env:SESKOPRUSU_DRIVER/VBCABLE_Setup_x64.exe")) { throw 'Tam VB-CABLE x64 sürücü paketini seçin.' }
dotnet publish "$PSScriptRoot/../SesKoprusu/SesKoprusu.csproj" -c Release -r win-x64 --self-contained true -o "$PSScriptRoot/publish"
if ($LASTEXITCODE) { throw 'Uygulama derlenemedi.' }
ISCC.exe "$PSScriptRoot/Setup.iss"
if ($LASTEXITCODE) { throw 'Setup derlenemedi.' }
