@echo off
REM ---------------------------------------------------------------------------
REM Runs the full four-process MMO stack against loopback, from a Windows build.
REM
REM   1. Build the project (Windows x86_64, MMO init scene at index 0).
REM   2. Put serverConfig.local.json at <build>\Config\serverConfig.json.
REM   3. Set BUILD_DIR and EXE_NAME below, then run this file.
REM
REM Each server gets its own console window so you can read its log live.
REM Close them with stop-local-stack.bat (map servers are children of the
REM spawner and die with it).
REM ---------------------------------------------------------------------------

set "BUILD_DIR=D:\1. Unity projekt\Builds\Windows"
set "EXE_NAME=MMORPG.exe"

if not exist "%BUILD_DIR%\%EXE_NAME%" (
  echo Cannot find "%BUILD_DIR%\%EXE_NAME%" - edit BUILD_DIR/EXE_NAME in this file.
  pause
  exit /b 1
)
if not exist "%BUILD_DIR%\Config\serverConfig.json" (
  echo Missing "%BUILD_DIR%\Config\serverConfig.json" - copy serverConfig.local.json there first.
  pause
  exit /b 1
)

cd /d "%BUILD_DIR%"

REM Order matters. Central opens a database client as it starts, so the
REM database manager has to be listening first; the map spawner registers with
REM central's cluster server, so central has to be up before it.
echo Starting database manager...
start "MMO Database" "%EXE_NAME%" -batchmode -nographics -startDatabaseServer -logFile "%BUILD_DIR%\Logs\database.log"
timeout /t 5 /nobreak >nul

echo Starting central server...
start "MMO Central" "%EXE_NAME%" -batchmode -nographics -startCentralServer -logFile "%BUILD_DIR%\Logs\central.log"
timeout /t 5 /nobreak >nul

echo Starting map spawn server...
start "MMO MapSpawn" "%EXE_NAME%" -batchmode -nographics -startMapSpawnServer -logFile "%BUILD_DIR%\Logs\mapspawn.log"

echo.
echo Stack starting. Give the map server ~20s to load the scene, then launch
echo two clients with launch-two-clients.bat.
echo Logs are in %BUILD_DIR%\Logs\.
