# Resize profiler: builds PolyPerf against the sources and runs it against
# the 12-card profile in tests\bin\perf. Numbers are CPU ms per size tick.
$ErrorActionPreference = "Stop"
$tests = Split-Path -Parent $MyInvocation.MyCommand.Path
$root  = Split-Path -Parent $tests
$bin   = Join-Path $tests "bin"
$csc   = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src   = Get-ChildItem -Path $root -Filter *.cs | Where-Object { $_.Name -ne "BuildInfo.cs" } | ForEach-Object { $_.FullName }
& $csc /nologo /optimize+ /target:exe "/out:$bin\PolyPerf.exe" /main:Polyclicker.PerfMain `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $src (Join-Path $tests "PerfMain.cs")
if ($LASTEXITCODE -ne 0) { throw "compile failed" }
Get-Process Polyclicker -ErrorAction SilentlyContinue | Stop-Process -Force
$env:POLYCLICKER_DATA = Join-Path $bin "perf"
# the app saves on exit; start from the template every run
New-Item -ItemType Directory -Force (Join-Path $bin "perf\Macros") | Out-Null
Copy-Item (Join-Path $tests "perf-profile.ini") (Join-Path $bin "perf\AutoClickerProfiles.ini") -Force
& "$bin\PolyPerf.exe"
& "$bin\PolyPerf.exe"
