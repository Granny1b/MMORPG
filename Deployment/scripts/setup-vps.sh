#!/usr/bin/env bash
# One-time VPS preparation for the MMORPG Kit server stack.
# Target: Debian 12 / Ubuntu 22.04+ with root access.
# Run as root on the VPS:  bash setup-vps.sh
set -euo pipefail

MMO_USER="${MMO_USER:-mmo}"
MMO_ROOT="${MMO_ROOT:-/srv/mmo}"
DB_NAME="${DB_NAME:-mmorpg_kit}"
DB_USER="${DB_USER:-mmo}"

if [[ $EUID -ne 0 ]]; then
  echo "Run as root." >&2
  exit 1
fi

echo "==> Installing packages"
apt-get update
# Unity Linux player links against libc6, and URP still pulls in a few X/GL
# symbols even for a dedicated-server build with -nographics.
apt-get install -y \
  mariadb-server \
  ufw \
  rsync \
  libc6 \
  ca-certificates

echo "==> Creating service user and directories"
id -u "$MMO_USER" >/dev/null 2>&1 || useradd --system --create-home --home-dir "$MMO_ROOT" --shell /usr/sbin/nologin "$MMO_USER"
mkdir -p "$MMO_ROOT/server" "$MMO_ROOT/server/Config" "$MMO_ROOT/logs" "$MMO_ROOT/backups"
chown -R "$MMO_USER:$MMO_USER" "$MMO_ROOT"

echo "==> Creating database"
DB_PASS="$(openssl rand -base64 24 | tr -d '/+=' | head -c 24)"
mysql <<SQL
CREATE DATABASE IF NOT EXISTS \`${DB_NAME}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '${DB_USER}'@'localhost' IDENTIFIED BY '${DB_PASS}';
GRANT ALL PRIVILEGES ON \`${DB_NAME}\`.* TO '${DB_USER}'@'localhost';
FLUSH PRIVILEGES;
SQL

# MariaDB/MySQL must not listen off-box: the kit's DB credentials live in a
# plaintext prefab field, so the blast radius of an exposed port is total.
if [[ -f /etc/mysql/mariadb.conf.d/50-server.cnf ]]; then
  sed -i 's/^bind-address.*/bind-address = 127.0.0.1/' /etc/mysql/mariadb.conf.d/50-server.cnf
  systemctl restart mariadb
fi

echo "==> Configuring firewall"
ufw --force reset
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp comment 'ssh'
# Central server - clients connect here first. LiteNetLib is UDP; the TCP rule
# only matters if you switch the transport to WebSocket for WebGL clients.
ufw allow 7000/udp comment 'mmo central'
ufw allow 7000/tcp comment 'mmo central websocket (only if useWebSocket)'
# Map servers, one port each starting at spawnStartPort. Widen if you add maps.
ufw allow 8000:8010/udp comment 'mmo map servers'
# 6001 (map spawn), 6010 (cluster) and 6100 (database manager) are deliberately
# absent - they are loopback-only on a single-box deploy and the database
# manager protocol has no authentication at all.
ufw --force enable

echo
echo "==================================================================="
echo " Done. Database credentials - store these in your password manager:"
echo "   host     127.0.0.1:3306"
echo "   database ${DB_NAME}"
echo "   user     ${DB_USER}"
echo "   password ${DB_PASS}"
echo
echo " Next:"
echo "   1. Import the schema:"
echo "        mysql -u ${DB_USER} -p ${DB_NAME} < mysql_main.sql"
echo "   2. Upload the server build to ${MMO_ROOT}/server (see deploy.sh)."
echo "   3. Put serverConfig.json in ${MMO_ROOT}/server/Config/."
echo "   4. Install the systemd units from Deployment/systemd/."
echo "==================================================================="
