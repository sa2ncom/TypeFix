# Publishes the self-contained win-x64 app and compiles TypeFix-Setup.exe.
# Requires the .NET 10 SDK and Inno Setup 6 (ISCC.exe).

$ErrorActionPreference = 'Stop'
$installerDir = $PSScriptRoot
$root = Split-Path -Parent $installerDir

Write-Host "Publishing TypeFix (win-x64, self-contained)..."
dotnet publish (Join-Path $root 'TypeFix.csproj') -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishedExe = Join-Path $root 'publish\win-x64\TypeFix.exe'
if (-not (Test-Path $publishedExe)) {
    throw "Published executable was not found: $publishedExe"
}

$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php and run this script again."
}

Write-Host "Compiling installer with $iscc"
& $iscc (Join-Path $installerDir 'TypeFix.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Installer: $(Join-Path $installerDir 'output\TypeFix-Setup.exe')"
