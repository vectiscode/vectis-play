param(
    [string]$LogDir = "$env:TEMP\VectisPlayLogs"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "runtime\chiaki-ng-Win\chiaki.exe"
if (!(Test-Path $exe)) { throw "Runtime not found: $exe" }
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$out = Join-Path $LogDir "stream-$stamp.out.log"
$err = Join-Path $LogDir "stream-$stamp.err.log"
$env:CHIAKI_VERBOSE_LOG_NOISE = "1"
$env:QT_LOGGING_RULES = "chiaki.gui.debug=true"
$process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -ArgumentList "--exit-app-on-stream-exit" -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
Write-Host "Debug stream started. Close the player when done."
Write-Host "PID: $($process.Id)"
Write-Host "Stdout: $out"
Write-Host "Stderr: $err"
