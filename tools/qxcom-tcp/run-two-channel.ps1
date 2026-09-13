# Two hosts on one real machine: boot QSP_DemoOpenGL with a per-port endpoint each, run the
# two-channel suite against it, and put its memory back.
#
#   .\tools\qxcom-tcp\run-two-channel.ps1
#
# The machine's memory has to carry a poll address on its second channel and a category pointed at
# it. Write one with:
#
#   sasctl nvram channels "1@01 2@02 validation=2" --out fixtures/nvram/two-channel.nvram.gz
#   sasctl nvram restore --fixture fixtures/nvram/two-channel.nvram.gz
#
# Nothing in the QSP_DemoOpenGL repository is touched: the two endpoints reach it through the
# per-port environment variables the transport already reads.
[CmdletBinding()]
param(
    [int]$FirstPort = 9781,
    [int]$SecondPort = 9782,
    [string]$Suite = 'tests/validation/two-channel',
    [string]$OutDir = "$env:TEMP\saswb-two-channel",
    [switch]$KeepMemory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$demoDir = 'C:\Progetti\QSP_DemoOpenGL\bin\SasWorkbenchM0\x64'
$demoExe = Join-Path $demoDir 'OpenGL_Game.exe'
$sasctl = Join-Path $repo 'src\SasWorkbench.Cli\bin\Debug\net10.0\sasctl.exe'

if (-not (Test-Path $demoExe)) { throw "Demo not built: $demoExe" }
if (-not (Test-Path $sasctl)) { throw "sasctl not built: $sasctl" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$egmLog = Join-Path $OutDir "egm-$stamp.log"
$transportLog = Join-Path $OutDir "transport-$stamp.log"
$runDir = Join-Path $OutDir "run-$stamp"

# The demo's own configuration names the two ports; the transport takes an endpoint per port, so
# the two channels reach two different hosts rather than both dialling one.
$env:SASWB_QXCOM_ENDPOINT__DEV_TTYS5 = "127.0.0.1:$FirstPort"
$env:SASWB_QXCOM_ENDPOINT__DEV_TTYS2 = "127.0.0.1:$SecondPort"
$env:SASWB_QXCOM_LOG = $transportLog
$env:SASWB_QPM_TRACE = '1'

Write-Host "EGM output  -> $egmLog"
Write-Host "transport   -> $transportLog"
Write-Host "run record  -> $runDir"

# The host listens on both ports, so it goes up first and the machine dials in.
$run = Start-Process -FilePath $sasctl -WorkingDirectory $repo -PassThru -NoNewWindow `
    -ArgumentList @('run', $Suite, '--stimulus', 'none', '--report', 'jsonl,extended',
                    '--out', $runDir, '--wait-for-machine', '90s')
Start-Sleep -Milliseconds 500

$demo = Start-Process -FilePath $demoExe -WorkingDirectory $demoDir -PassThru `
    -RedirectStandardOutput $egmLog -RedirectStandardError "$egmLog.err"

$run.WaitForExit(180000) | Out-Null

# Ask the demo to close its window rather than killing it: a hard kill loses the buffered stdout
# that carries the platform's own account of what it opened.
if (-not $demo.HasExited) {
    taskkill /PID $demo.Id 2>&1 | Out-Null
    $demo.WaitForExit(10000) | Out-Null
}
foreach ($p in @($demo, $run)) {
    try { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } } catch {}
}

Write-Host "`n===== what the platform said about its channels =====" -ForegroundColor Cyan
Select-String -Path $egmLog -Pattern 'sasAddSasChannel|SetClientIDChannel|Channel|Port #' `
    -ErrorAction SilentlyContinue | Select-Object -First 40 | ForEach-Object { $_.Line }

if (-not $KeepMemory) {
    Write-Host "`nputting the machine's memory back" -ForegroundColor Cyan
    & $sasctl nvram restore
}
