# M0 wire proof: start the throwaway SAS host, boot QSP_DemoOpenGL against it, dump the wire.
#
#   .\tools\qxcom-tcp\run-m0.ps1                 general polls + long poll 1F at address 1
#   .\tools\qxcom-tcp\run-m0.ps1 -Sweep          probe every address to find the configured one
[CmdletBinding()]
param(
    [int]$Address = 1,
    [int]$Port = 9760,
    [int]$Seconds = 40,
    [switch]$Sweep,
    [string]$OutDir = "$env:TEMP\saswb-m0"
)

$ErrorActionPreference = 'Stop'
$demoDir = "C:\Progetti\QSP_DemoOpenGL\bin\SasWorkbenchM0\x64"
$demoExe = Join-Path $demoDir 'OpenGL_Game.exe'
$probe = "C:\PRogetti\SasWorkbench\tools\qxcom-tcp\host_probe.py"

if (-not (Test-Path $demoExe)) { throw "Demo not built: $demoExe" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$wireLog = Join-Path $OutDir "wire-$stamp.log"
$egmLog = Join-Path $OutDir "egm-$stamp.log"
$transportLog = Join-Path $OutDir "transport-$stamp.log"

$probeArgs = @($probe, '--port', $Port, '--address', $Address, '--wait', $Seconds, '--settle', '3')
if ($Sweep) { $probeArgs += @('--sweep', '--interval', '0.05') }

Write-Host "host probe  -> $wireLog"
Write-Host "EGM output  -> $egmLog"
Write-Host "transport   -> $transportLog"

$probeProc = Start-Process -FilePath python -ArgumentList $probeArgs -PassThru -NoNewWindow `
    -RedirectStandardOutput $wireLog -RedirectStandardError "$wireLog.err"
Start-Sleep -Milliseconds 800

$env:SASWB_QXCOM_ENDPOINT = "127.0.0.1:$Port"
$env:SASWB_QXCOM_LOG = $transportLog
$demoProc = Start-Process -FilePath $demoExe -WorkingDirectory $demoDir -PassThru `
    -RedirectStandardOutput $egmLog -RedirectStandardError "$egmLog.err"

$probeProc.WaitForExit([int]($Seconds * 1000) + 15000) | Out-Null
Start-Sleep -Seconds 2

# Ask the demo to close its window first: a hard kill loses the buffered stdout that
# carries QSP's own diagnostics.
if (-not $demoProc.HasExited) {
    taskkill /PID $demoProc.Id 2>&1 | Out-Null
    $demoProc.WaitForExit(8000) | Out-Null
}
foreach ($p in @($demoProc, $probeProc)) {
    try { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } } catch {}
}

Write-Host "`n===== wire =====" -ForegroundColor Cyan
Get-Content $wireLog -ErrorAction SilentlyContinue
Write-Host "`n(EGM log: $egmLog)"
