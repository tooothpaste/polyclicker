# Renders polyclicker.svg to app-dev.ico (the dev build's icon: muted
# petals, terminal badge). Add -Release to write app.ico instead.
param([switch]$Release)
$ErrorActionPreference = "Stop"
$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$root  = Split-Path -Parent $tools
$bin   = Join-Path $tools "bin"
New-Item -ItemType Directory -Force $bin | Out-Null
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /optimize+ /target:exe "/out:$bin\MakeIcon.exe" `
    /r:System.dll /r:System.Drawing.dll /r:System.Xml.dll (Join-Path $tools "MakeIcon.cs")
if ($LASTEXITCODE -ne 0) { throw "compile failed" }
$svg = Join-Path $root "polyclicker.svg"
if ($Release) { & "$bin\MakeIcon.exe" $svg (Join-Path $root "app.ico") }
else          { & "$bin\MakeIcon.exe" $svg (Join-Path $root "app-dev.ico") -dev }
exit $LASTEXITCODE
