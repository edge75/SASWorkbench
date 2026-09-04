# Puts the MSVC x64 toolchain, cmake and ninja on PATH for the current PowerShell session.
# Dot-source it:  . .\tools\qxcom-tcp\devenv.ps1
#
# vcvars64.bat is invoked with a deliberately minimal PATH: this machine's PATH is long
# enough that cmd.exe rejects the batch file with "The input line is too long".
$vs = "C:\Program Files\Microsoft Visual Studio\18\Enterprise"
$vcvars = "$vs\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found at $vcvars" }

$originalPath = $env:PATH
$minimal = "C:\Windows\system32;C:\Windows;C:\Windows\System32\Wbem;C:\Windows\System32\WindowsPowerShell\v1.0;${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"

$vcPath = $null
cmd /c "set PATH=$minimal && call `"$vcvars`" >nul && set" | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') {
        if ($matches[1] -ieq 'PATH') { $vcPath = $matches[2] }
        else { Set-Item -Path "env:$($matches[1])" -Value $matches[2] }
    }
}
if (-not $vcPath) { throw "vcvars64.bat did not produce an environment" }

$env:PATH = "$vs\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin;" +
            "$vs\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja;" +
            "$vcPath;$originalPath"

Write-Host "cmake:" (Get-Command cmake).Source
Write-Host "ninja:" (Get-Command ninja).Source
Write-Host "cl:   " (Get-Command cl).Source
