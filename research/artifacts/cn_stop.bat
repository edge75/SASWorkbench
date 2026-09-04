@echo off
REM ============================================
REM  GSA SAS Host Simulator - Shutdown
REM ============================================
echo Closing SAS Host Simulator...
taskkill /F /IM SASHostSim.exe >nul 2>&1
echo Closing DOSBox (SAS Engine)...
taskkill /F /IM dosbox-x.exe >nul 2>&1
echo.
echo All closed.
timeout /t 2 /nobreak >nul
