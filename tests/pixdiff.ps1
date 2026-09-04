# Pixel-compares two screenshots (from run-tests.ps1 -Shots, say, before and
# after a rendering change) and lists the rows that differ.
param([Parameter(Mandatory=$true)][string]$Before, [Parameter(Mandatory=$true)][string]$After, [int]$Rows = 20)
Add-Type -AssemblyName System.Drawing
$a = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Before).Path)
$b = [System.Drawing.Bitmap]::FromFile((Resolve-Path $After).Path)
$w = [Math]::Min($a.Width, $b.Width); $h = [Math]::Min($a.Height, $b.Height)
$lines = New-Object System.Collections.ArrayList; $total = 0
for ($y = 0; $y -lt $h; $y++) {
  $n = 0; $minx = $w; $maxx = -1
  for ($x = 0; $x -lt $w; $x++) {
    if ($a.GetPixel($x, $y) -ne $b.GetPixel($x, $y)) { $n++; if ($x -lt $minx) { $minx = $x }; if ($x -gt $maxx) { $maxx = $x } }
  }
  if ($n -gt 0) { $total += $n; [void]$lines.Add("  y=$y x=$minx-$maxx ($n)") }
}
$size = ''; if ($a.Size -ne $b.Size) { $size = " (sizes differ: $($a.Width)x$($a.Height) vs $($b.Width)x$($b.Height))" }
Write-Output "$total pixels differ in $($lines.Count) rows$size"
$lines | Select-Object -First $Rows
$a.Dispose(); $b.Dispose()
