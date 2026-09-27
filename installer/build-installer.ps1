# Publishes self-contained win-x64, win-x86, and win-arm64 builds
# and compiles one setup exe per architecture.
# Requires the .NET 10 SDK and Inno Setup 6 (ISCC.exe).

$ErrorActionPreference = 'Stop'
$installerDir = $PSScriptRoot
$root = Split-Path -Parent $installerDir

$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php and run this script again."
}

$versionLine = Select-String -Path (Join-Path $installerDir 'TypeFix.iss') -Pattern '^#define AppVersion "([^"]+)"' | Select-Object -First 1
if (-not $versionLine) {
    throw "AppVersion was not found in TypeFix.iss."
}
$appVersion = $versionLine.Matches[0].Groups[1].Value

$architectures = @('x64', 'x86', 'arm64')

foreach ($arch in $architectures) {
    $rid = "win-$arch"
    Write-Host "Publishing TypeFix ($rid, self-contained)..."
    dotnet publish (Join-Path $root 'TypeFix.csproj') -p:PublishProfile=$rid
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $publishedExe = Join-Path $root "publish\$rid\TypeFix.exe"
    if (-not (Test-Path $publishedExe)) {
        throw "Published executable was not found: $publishedExe"
    }

    Write-Host "Compiling $arch installer with $iscc"
    & $iscc "/DArch=$arch" (Join-Path $installerDir 'TypeFix.iss')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $setup = Join-Path $installerDir "output\TypeFix-$appVersion-Setup-$arch.exe"
    Write-Host "Installer: $setup"
}
