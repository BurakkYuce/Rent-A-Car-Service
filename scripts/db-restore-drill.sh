#!/usr/bin/env bash
# RentACar geri-yükleme tatbikatı (P0-1): en son yedeği GEÇİCİ bir DB'ye açar,
# kritik tablo satır sayılarını KAYNAK DB ile karşılaştırır, geçici DB'yi siler.
# Yedek "alınmış" değil "geri dönülebilir" olduğunda görev tamamdır — bunu kanıtlar.
#
# Kullanım (ayda bir / her kurulum değişikliğinde):
#   RACAR_DB=racar RACAR_PG_USER=racar_owner RACAR_BACKUP_DIR=/var/backups/racar ./db-restore-drill.sh
# Çıkış kodu: 0 = tatbikat başarılı; ≠0 = YEDEĞİNE GÜVENME.
set -euo pipefail

DB="${RACAR_DB:-racar}"
PG_USER="${RACAR_PG_USER:-racar_owner}"
PG_HOST="${RACAR_PG_HOST:-localhost}"
PG_PORT="${RACAR_PG_PORT:-5432}"
DIR="${RACAR_BACKUP_DIR:-$HOME/racar-backups}"
DRILL_DB="${RACAR_DRILL_DB:-racar_restore_drill}"
# Karşılaştırılacak tablolar: platform + para + operasyon (kanıt için temsilî küme)
TABLES="${RACAR_DRILL_TABLES:-Tenants Users Vehicles Customers AccountLedgerEntries Invoices Rentals}"

PSQL=(psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" --no-password -tA)

LATEST="$(ls -1t "$DIR"/racar-*.dump 2>/dev/null | head -1 || true)"
[ -n "$LATEST" ] || { echo "[tatbikat] HATA: $DIR içinde yedek yok" >&2; exit 2; }
echo "[tatbikat] yedek: $LATEST"

cleanup() { "${PSQL[@]}" -d postgres -c "DROP DATABASE IF EXISTS \"$DRILL_DB\";" > /dev/null || true; }
trap cleanup EXIT
cleanup

"${PSQL[@]}" -d postgres -c "CREATE DATABASE \"$DRILL_DB\";" > /dev/null
echo "[tatbikat] geri yükleniyor → $DRILL_DB"
pg_restore -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" --no-password \
    -d "$DRILL_DB" --no-owner --exit-on-error "$LATEST"

FAIL=0
for T in $TABLES; do
    SRC="$("${PSQL[@]}" -d "$DB" -c "SELECT COUNT(*) FROM \"$T\";")"
    DST="$("${PSQL[@]}" -d "$DRILL_DB" -c "SELECT COUNT(*) FROM \"$T\";")"
    if [ "$SRC" = "$DST" ]; then
        echo "[tatbikat] $T: $SRC = $DST ✓"
    else
        echo "[tatbikat] $T: kaynak $SRC ≠ yedek $DST ✗" >&2
        FAIL=1
    fi
done

# RLS policy'ler de geri gelmeli — policy kaybı, geri dönüşte SESSİZ tenant-izolasyon deliği olur.
SRC_POL="$("${PSQL[@]}" -d "$DB" -c "SELECT COUNT(*) FROM pg_policies;")"
DST_POL="$("${PSQL[@]}" -d "$DRILL_DB" -c "SELECT COUNT(*) FROM pg_policies;")"
if [ "$SRC_POL" = "$DST_POL" ] && [ "$SRC_POL" -gt 0 ]; then
    echo "[tatbikat] RLS policy: $SRC_POL = $DST_POL ✓"
else
    echo "[tatbikat] RLS policy: kaynak $SRC_POL ≠ yedek $DST_POL ✗ (İZOLASYON RİSKİ)" >&2
    FAIL=1
fi

# Not: kaynak DB canlı yazım altındaysa küçük sapmalar olabilir — tatbikatı sakin saatte koşun.
[ "$FAIL" -eq 0 ] && echo "[tatbikat] BAŞARILI — yedek geri dönülebilir." || echo "[tatbikat] BAŞARISIZ" >&2
exit "$FAIL"
