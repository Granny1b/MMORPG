@echo off
REM Kills every process started from the build, servers and clients alike.
set "EXE_NAME=MMORPG.exe"
taskkill /F /IM "%EXE_NAME%" /T 2>nul
echo Stopped all "%EXE_NAME%" processes.
