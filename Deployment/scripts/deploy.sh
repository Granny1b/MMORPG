#!/usr/bin/env bash
# Push a locally built Linux dedicated-server build to the VPS and restart
# the stack. Run from your dev machine (Git Bash / WSL / macOS / Linux).
#
#   VPS_HOST=1.2.3.4 BUILD_DIR=../Builds/LinuxServer ./deploy.sh
#
# The Config/ directory on the server is preserved - serverConfig.json lives
# there and must not be clobbered by a build that doesn't contain it.
set -euo pipefail

VPS_HOST="${VPS_HOST:?set VPS_HOST=user@host or an ssh alias}"
BUILD_DIR="${BUILD_DIR:?set BUILD_DIR to the folder holding the server binary}"
REMOTE_ROOT="${REMOTE_ROOT:-/srv/mmo/server}"
SERVICES="mmo-mapspawn mmo-central mmo-database"

[[ -d "$BUILD_DIR" ]] || { echo "No such build dir: $BUILD_DIR" >&2; exit 1; }

echo "==> Stopping services (reverse dependency order)"
ssh "$VPS_HOST" "sudo systemctl stop $SERVICES"

echo "==> Uploading build"
# --delete keeps the remote tree honest after asset renames, but Config/ and
# any SQLite file are excluded so live data survives a redeploy.
rsync -avz --delete \
  --exclude 'Config/' \
  --exclude '*.sqlite3' \
  --exclude '*_BurstDebugInformation_DoNotShip/' \
  --exclude '*_BackUpThisFolder_ButDontShipItWithYourGame/' \
  "$BUILD_DIR"/ "$VPS_HOST:$REMOTE_ROOT"/

echo "==> Fixing ownership and exec bit"
ssh "$VPS_HOST" "sudo chown -R mmo:mmo $REMOTE_ROOT && sudo chmod +x $REMOTE_ROOT/MMORPGServer"

echo "==> Starting services (dependency order)"
ssh "$VPS_HOST" "sudo systemctl start mmo-database && sleep 3 && sudo systemctl start mmo-central && sleep 3 && sudo systemctl start mmo-mapspawn"

echo "==> Status"
ssh "$VPS_HOST" "systemctl --no-pager --lines=0 status mmo-database mmo-central mmo-mapspawn || true"
