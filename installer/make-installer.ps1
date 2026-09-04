# Build the exe, then the installer. Needs Inno Setup 6 (free):
#
#   winget install JRSoftware.InnoSetup
#
# Output: dist\Polyclicker-Setup-<version>.exe. Unsigned - SmartScreen will
# warn on first run until a certificate is added (see the README).
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here

$iscc = @(
    "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    Write-Host "Inno Setup 6 not found. Install it with:"
    Write-Host "    winget install JRSoftware.InnoSetup"
    exit 1
}

& (Join-Path $root "build.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $iscc /Q (Join-Path $here "Polyclicker.iss")
if ($LASTEXITCODE -ne 0) { "INSTALLER FAILED ($LASTEXITCODE)"; exit $LASTEXITCODE }
Get-ChildItem (Join-Path $root "dist") -Filter "Polyclicker-Setup-*.exe" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
    ForEach-Object { "built: $($_.FullName)  ({0:N0} bytes)" -f $_.Length }
