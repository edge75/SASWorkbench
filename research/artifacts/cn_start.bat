@echo off
REM ============================================
REM  GSA SAS Host Simulator - One-Click Launcher
REM ============================================
REM
REM Architecture (no Bridge):
REM   SASHostSim (COM3) <-wire-> COM4 (gsat GEP)
REM   gsat SAS (COM6) <-wire-> COM5 (EGM Game)
REM
REM Start Order: DOSBox(gsat) -> SASHostSim -> EGM Game
REM

echo ============================================
echo  GSA SAS Host Simulator - Launcher
echo ============================================
echo.

REM Step 0: Kill any existing instances
echo Closing existing instances...
taskkill /F /IM dosbox-x.exe >nul 2>&1
taskkill /F /IM SASHostSim.exe >nul 2>&1
taskkill /F /IM dotnet.exe >nul 2>&1
timeout /t 2 /nobreak >nul

REM Step 1: Start DOSBox (gsat.exe Engine)
echo.
echo [1/2] Starting SAS Engine (DOSBox + gsat.exe)...

REM Generate autoexec with correct absolute path
set SAS_ENGINE_PATH=%~dp0SAS Engine
(
echo mount c "%SAS_ENGINE_PATH%"
echo c:
echo gsat.exe
) > "%~dp0DOSBox\autoexec.bat"

start "" "%~dp0DOSBox\dosbox-x\dosbox-x.exe" -conf "%~dp0DOSBox\dosbox-x.conf" -c "mount c \"%SAS_ENGINE_PATH%\"" -c "c:" -c "gsat.exe"
echo       Waiting 6 seconds for Engine to initialize...
timeout /t 6 /nobreak >nul

REM Step 2: Start SASHostSim GUI
echo [2/2] Starting SAS Host Simulator GUI...
start "" "%~dp0SASHostSim.exe"
echo.

echo ============================================
echo  Both programs launched!
echo.
echo  - SASHostSim: select COM3, wait for green light
echo  - Then start your EGM game (COM5)
echo ============================================
echo.
pause
