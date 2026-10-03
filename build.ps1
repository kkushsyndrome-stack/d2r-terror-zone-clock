# Builds the Terror Zone Clock with the C# compiler that ships with Windows (.NET Framework 4.x).
# Output: calendar-publish\D2RTerrorZoneClock.exe by default. The artwork in assets\ is embedded in the exe.
param([string]$OutDir = (Join-Path $PSScriptRoot 'calendar-publish'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = $OutDir
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force $out | Out-Null

& $csc /nologo /target:winexe /optimize+ `
    "/out:$out\D2RTerrorZoneClock.exe" `
    "/win32manifest:$root\app.manifest" `
    "/win32icon:$root\app.ico" `
    "/resource:$root\assets\art.png,art.png" `
    "/resource:$root\assets\frame.png,frame.png" `
    /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll `
    "$root\CalendarTimeTool.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $out\D2RTerrorZoneClock.exe"
