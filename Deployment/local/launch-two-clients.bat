@echo off
REM Two client windows on one PC - the actual "is it networked" test.
REM Register two different accounts, put both characters in the world, and
REM check that each one sees the other move.
REM
REM -popupwindow -screen-width/-height keep them side by side rather than
REM fullscreen fighting each other for the display.

set "BUILD_DIR=D:\1. Unity projekt\Builds\Windows"
set "EXE_NAME=MMORPG.exe"

cd /d "%BUILD_DIR%"
start "Client A" "%EXE_NAME%" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -popupwindow
timeout /t 3 /nobreak >nul
start "Client B" "%EXE_NAME%" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -popupwindow
