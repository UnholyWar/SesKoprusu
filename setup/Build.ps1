param([string]$CablePackageDirectory, [string]$CodexDirectory, [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
$env:SESKOPRUSU_VERSION = $Version
$env:SESKOPRUSU_DRIVER = ''
if ($CablePackageDirectory) {
    $env:SESKOPRUSU_DRIVER = (Resolve-Path $CablePackageDirectory).Path
    if (!(Test-Path "$env:SESKOPRUSU_DRIVER/VBCABLE_Setup_x64.exe")) { throw 'Select the extracted VB-CABLE x64 package.' }
}
dotnet publish "$PSScriptRoot/../SesKoprusu/SesKoprusu.csproj" -p:Version=$Version -c Release -r win-x64 --self-contained true -o "$PSScriptRoot/publish"
if ($LASTEXITCODE) { throw 'Uygulama derlenemedi.' }
if ($CodexDirectory) {
    $codexPath = Join-Path $CodexDirectory 'codex.exe'
    if (!(Test-Path $codexPath -PathType Leaf)) { throw 'Windows x64 Codex CLI paketinin codex.exe içeren klasörünü seçin.' }
    Copy-Item -Path "$CodexDirectory/*" -Destination "$PSScriptRoot/publish" -Recurse -Force
}
ISCC.exe "$PSScriptRoot/Setup.iss"
if ($LASTEXITCODE) { throw 'Setup derlenemedi.' }
