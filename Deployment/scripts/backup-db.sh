#!/usr/bin/env bash
# Nightly database dump with rotation. Install on the VPS as a cron job:
#   0 4 * * * /srv/mmo/backup-db.sh >> /srv/mmo/logs/backup.log 2>&1
#
# Player characters, inventories, guilds and storage all live in this one
# database. A build can be rebuilt from git; this cannot.
set -euo pipefail

DB_NAME="${DB_NAME:-mmorpg_kit}"
DB_USER="${DB_USER:-mmo}"
BACKUP_DIR="${BACKUP_DIR:-/srv/mmo/backups}"
KEEP_DAYS="${KEEP_DAYS:-14}"

mkdir -p "$BACKUP_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"
OUT="$BACKUP_DIR/${DB_NAME}-${STAMP}.sql.gz"

# --single-transaction keeps InnoDB tables consistent without locking players
# out mid-dump. Credentials come from ~/.my.cnf so they stay out of the crontab
# and out of the process list.
mysqldump --single-transaction --quick --user="$DB_USER" "$DB_NAME" | gzip -9 > "$OUT"
echo "$(date -Is) wrote $OUT ($(du -h "$OUT" | cut -f1))"

find "$BACKUP_DIR" -name "${DB_NAME}-*.sql.gz" -mtime "+${KEEP_DAYS}" -delete
