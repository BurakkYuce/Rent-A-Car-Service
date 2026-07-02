#!/usr/bin/env bash
# RentACar günlük veritabanı yedeği (P0-1).
# - pg_dump custom format (-Fc): sıkıştırılmış, pg_restore ile seçmeli geri yükleme.
# - Rotasyon: RACAR_BACKUP_KEEP günden eski yedekler silinir (varsayılan 14).
# - Off-site: RACAR_BACKUP_REMOTE tanımlıysa rclone ile kopyalanır (ör. "b2:racar-yedek").
#
# Kullanım (cron/systemd-timer, günlük):
#   RACAR_DB=racar RACAR_PG_USER=racar_owner RACAR_BACKUP_DIR=/var/backups/racar ./db-backup.sh
# Şifre: ~/.pgpass (0600) — script şifre TUTMAZ.
set -euo pipefail

DB="${RACAR_DB:-racar}"
PG_USER="${RACAR_PG_USER:-racar_owner}"
PG_HOST="${RACAR_PG_HOST:-localhost}"
PG_PORT="${RACAR_PG_PORT:-5432}"
DIR="${RACAR_BACKUP_DIR:-$HOME/racar-backups}"
KEEP_DAYS="${RACAR_BACKUP_KEEP:-14}"
REMOTE="${RACAR_BACKUP_REMOTE:-}"

mkdir -p "$DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"
FILE="$DIR/racar-$STAMP.dump"

echo "[yedek] $DB → $FILE"
pg_dump -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -Fc --no-password -f "$FILE" "$DB"

# Bütünlük: dosya okunabilir bir arşiv mi? (bozuk yedek sessizce dönmesin)
pg_restore --list "$FILE" > /dev/null
SIZE="$(du -h "$FILE" | cut -f1)"
echo "[yedek] tamam ($SIZE)"

# Rotasyon
find "$DIR" -name "racar-*.dump" -type f -mtime +"$KEEP_DAYS" -print -delete | sed 's/^/[rotasyon] silindi: /' || true

# Off-site (opsiyonel)
if [ -n "$REMOTE" ]; then
    if command -v rclone > /dev/null; then
        echo "[off-site] rclone → $REMOTE"
        rclone copy "$FILE" "$REMOTE" --no-traverse
    else
        echo "[off-site] UYARI: RACAR_BACKUP_REMOTE tanımlı ama rclone kurulu değil" >&2
        exit 3
    fi
fi
