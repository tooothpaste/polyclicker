# The regression harness: the app's own sources plus one test entry point,
# built with the in-box compiler and run against a throwaway data folder.
#
#   .\tests\run-tests.ps1           engine + editor checks (real input: ~10 s)
#   .\tests\run-tests.ps1 -Repro    editor interaction checks (real keystrokes)
#   .\tests\run-tests.ps1 -Shots    render the editor and card screenshots
#
# The harness injects real input - F13-F16, the X2 button, pointer moves -
# so leave the desktop alone while it runs. A running Polyclicker is closed
# first: its hooks would otherwise eat the harness engine's clicks.
param([switch]$Repro, [switch]$Shots)
$ErrorActionPreference = "Stop"
$tests = Split-Path -Parent $MyInvocation.MyCommand.Path
$root  = Split-Path -Parent $tests
$bin   = Join-Path $tests "bin"
$data  = Join-Path $bin "data"
$csc   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$refs  = "/r:System.dll", "/r:System.Drawing.dll", "/r:System.Windows.Forms.dll",
         "/r:System.IO.Compression.dll", "/r:System.IO.Compression.FileSystem.dll"
$src   = Get-ChildItem -Path $root -Filter *.cs | Where-Object { $_.Name -ne "BuildInfo.cs" } | ForEach-Object { $_.FullName }
New-Item -ItemType Directory -Force $data | Out-Null
Get-Process Polyclicker -ErrorAction SilentlyContinue | Stop-Process -Force

function Build($main, $file, $out, $target) {
    & $csc /nologo /optimize+ "/target:$target" "/out:$out" "/main:Polyclicker.$main" `
        $refs $src (Join-Path $tests $file)
    if ($LASTEXITCODE -ne 0) { throw "compile failed: $file" }
}

$env:POLYCLICKER_DATA = $data

if (-not $Repro -and -not $Shots) {
    Build "TestMain" "TestMain.cs" (Join-Path $bin "PolyTest.exe") "exe"
    & (Join-Path $bin "PolyTest.exe")
    exit $LASTEXITCODE
}
if ($Repro) {
    Build "TestMain" "TestMain.cs" (Join-Path $bin "PolyTest.exe") "exe"
    & (Join-Path $bin "PolyTest.exe") | Out-Null      # writes the synthetic take
    Build "ReproMain" "ReproMain.cs" (Join-Path $bin "PolyRepro.exe") "exe"
    Copy-Item (Join-Path $env:TEMP "polytest-take.macro") (Join-Path $bin "repro-take.macro") -Force
    & (Join-Path $bin "PolyRepro.exe") (Join-Path $bin "repro-take.macro")
}
if ($Shots) {
    Build "TestMain" "TestMain.cs" (Join-Path $bin "PolyTest.exe") "exe"
    & (Join-Path $bin "PolyTest.exe") | Out-Null
    Build "ShotMain" "ShotMain.cs" (Join-Path $bin "PolyShot.exe") "winexe"
    Start-Process -Wait (Join-Path $bin "PolyShot.exe") -ArgumentList "`"$env:TEMP\polytest-take.macro`" `"$bin`""
    # two cards, one held, for the card-row shot
    $cards = Join-Path $bin "cards"
    New-Item -ItemType Directory -Force (Join-Path $cards "Macros") | Out-Null
    @"
[Slot1]
Name=Clicker
Input=Left Click
Hotkey=F6
Interval=250
[Slot2]
Name=Held key
Input=Custom Key
CustomKey=W
Hotkey=F7
Interval=100
HoldDown=1
[Meta]
Count=2
[Global]
UpdateCheck=0
"@ | Out-File (Join-Path $cards "AutoClickerProfiles.ini") -Encoding Unicode
    Build "ShotMain2" "ShotMain2.cs" (Join-Path $bin "PolyShot2.exe") "winexe"
    $env:POLYCLICKER_DATA = $cards
    Start-Process -Wait (Join-Path $bin "PolyShot2.exe") -ArgumentList "`"$bin`""
    Build "ShotMain3" "ShotMain3.cs" (Join-Path $bin "PolyShot3.exe") "winexe"
    $env:POLYCLICKER_DATA = $data
    Start-Process -Wait (Join-Path $bin "PolyShot3.exe") -ArgumentList "`"$bin`""
    # POLYCLICKER_UISCALE=150 in the environment renders all of these at 150%
    # (files get a -150 suffix) - a high-DPI layout check on a 100% display
    Get-ChildItem $bin -Filter *.png | ForEach-Object { $_.FullName }
}
