param([Parameter(Mandatory)][string]$CablePackageDirectory, [Parameter(Mandatory)][string]$CodexDirectory)
$ErrorActionPreference = 'Stop'
$env:SESKOPRUSU_DRIVER = (Resolve-Path $CablePackageDirectory).Path
if (!(Test-Path "$env:SESKOPRUSU_DRIVER/VBCABLE_Setup_x64.exe")) { throw 'Tam VB-CABLE x64 sürücü paketini seçin.' }
dotnet publish "$PSScriptRoot/../SesKoprusu/SesKoprusu.csproj" -c Release -r win-x64 --self-contained true -o "$PSScriptRoot/publish"
if ($LASTEXITCODE) { throw 'Uygulama derlenemedi.' }
$codexPath = Join-Path $CodexDirectory 'codex.exe'
if (!(Test-Path $codexPath -PathType Leaf)) { throw 'Windows x64 Codex CLI paketinin codex.exe içeren klasörünü seçin.' }
Copy-Item -Path "$CodexDirectory/*" -Destination "$PSScriptRoot/publish" -Recurse -Force
ISCC.exe "$PSScriptRoot/Setup.iss"
if ($LASTEXITCODE) { throw 'Setup derlenemedi.' }
