# Regenerates docs\screenshot-light.png and docs\screenshot-dark.png from a
# sample profile, using the sources as they are. Leave the desktop alone
# while it runs; the window is captured from the screen.
$ErrorActionPreference = "Stop"
$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$root  = Split-Path -Parent $tools
$bin   = Join-Path $tools "bin"
$data  = Join-Path $bin "readme-data"
$csc   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force $bin, (Join-Path $data "Profiles"), (Join-Path $data "Macros") | Out-Null

$src = Get-ChildItem -Path $root -Filter *.cs | Where-Object { $_.Name -ne "BuildInfo.cs" } | ForEach-Object { $_.FullName }
& $csc /nologo /optimize+ /target:winexe "/out:$bin\ShotReadme.exe" /main:Polyclicker.ShotReadme `
    "/resource:$root\app.ico,Polyclicker.app.ico" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $src (Join-Path $tools "ShotReadme.cs")
if ($LASTEXITCODE -ne 0) { throw "compile failed" }

$slots = @"
[Slot1]
Name=Ore miner
Input=Left Click
Hotkey=F6
Interval=50
PosMode=Current Position
Color=teal
[Slot2]
Name=Harvest run
Input=Macro
Macro=harvest-run.macro
Hotkey=F9
Interval=1500
MacroLoop=1
Color=peach
[Slot3]
Name=Sprint hold
Input=Custom Key
CustomKey=Space
Hotkey=F7
Interval=120
Mode=Hold
Color=lavender
[Slot4]
Name=Forge tap
Input=Left Click
Hotkey=F8
Interval=200
PosMode=Fixed Position
X=1480
Y=620
Color=mint
[Slot5]
Name=Idle clicker
Input=Left Click
Hotkey=F10
Interval=100
WinTitle=ahk_exe program.exe
Collapsed=1
Color=sky
[Meta]
Count=5
"@
$slots | Out-File (Join-Path $data "Profiles\Farming.ini") -Encoding Unicode
"# Polyclicker macro v1`r`n0.000 0 640 400`r`n120.000 1 1 0`r`n180.000 2 1 0`r`n" |
    Out-File (Join-Path $data "Macros\harvest-run.macro") -Encoding UTF8

foreach ($theme in "light", "dark") {
    @"
[Global]
Profile=Farming
Theme=$theme
[Window]
W=1120
H=475
$slots
"@ | Out-File (Join-Path $data "AutoClickerProfiles.ini") -Encoding Unicode
    $env:POLYCLICKER_DATA = $data
    $out = Join-Path $root "docs\screenshot-$theme.png"
    Start-Process -Wait (Join-Path $bin "ShotReadme.exe") -ArgumentList "`"$out`""
    "wrote $out"
}
