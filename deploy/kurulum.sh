#!/usr/bin/env bash
# RentACar — SUNUCU İLK KURULUM (Ubuntu 24.04, tek VPS: uygulama + PostgreSQL + Caddy).
#
# Bu script `docs/ops/deploy-checklist.md`'in elle yapılan adımlarını otomatikleştirir.
# TAMAMEN IDEMPOTENT: iki kez çalıştırmak güvenlidir, var olanı bozmaz.
#
# Kullanım (sunucuda, repo kökünden):
#   sudo ./deploy/kurulum.sh
#
# Ne YAPMAZ (bilinçli):
# - Uygulama yayınlamaz  → onu `yayinla.sh` yapar (kurulum ile deploy ayrı sorumluluk).
# - Var olan /etc/racar/racar.env'i ASLA ezmez (bkz. aşağıdaki ölümcül-anahtar uyarısı).
# - DNS kaydı açmaz — wildcard A kaydı SENİN sağlayıcında olmalı (script sonda uyarır).
set -euo pipefail

APP_USER="${RACAR_USER:-racar}"
APP_DIR="/opt/racar"
ENV_DIR="/etc/racar"
ENV_FILE="$ENV_DIR/racar.env"
DP_DIR="/var/lib/racar/dp-keys"
LOG_DIR="/var/log/racar"
YEDEK_DIR="/var/backups/racar"
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

bilgi() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
tamam() { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; }
uyari() { printf '\033[1;33m  ! \033[0m%s\n' "$*"; }
hata()  { printf '\033[1;31mHATA:\033[0m %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || hata "root gerekir: sudo ./deploy/kurulum.sh"

# ---------------------------------------------------------------- 1. paketler
bilgi "Paketler"
if ! command -v psql >/dev/null; then
    apt-get update -qq
    apt-get install -y -qq postgresql postgresql-contrib
fi
tamam "PostgreSQL $(psql --version | awk '{print $3}')"

if ! command -v caddy >/dev/null; then
    apt-get install -y -qq debian-keyring debian-archive-keyring apt-transport-https curl gnupg
    curl -fsSL https://dl.cloudsmith.io/public/caddy/stable/gpg.key \
        | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
    echo "deb [signed-by=/usr/share/keyrings/caddy-stable-archive-keyring.gpg] https://dl.cloudsmith.io/public/caddy/stable/deb/debian any-version main" \
        > /etc/apt/sources.list.d/caddy-stable.list
    apt-get update -qq && apt-get install -y -qq caddy
fi
tamam "Caddy $(caddy version | head -1)"

# SDK (runtime DEĞİL): yayinla.sh .NET'i sunucuda `dotnet publish` ile derler. `command -v dotnet`
# yetmezdi — yalnız runtime kurulu bir makinede geçer, publish ise ilk yayında patlar.
if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    apt-get install -y -qq dotnet-sdk-10.0 \
        || hata "dotnet-sdk-10.0 bulunamadı — Microsoft paket deposunu ekleyip tekrar dene (checklist §1)."
fi
dotnet --list-sdks | grep -q '^10\.' || hata ".NET SDK 10 kurulamadı (dotnet --list-sdks)."
tamam ".NET SDK $(dotnet --list-sdks | grep '^10\.' | tail -n 1 | awk '{print $1}')"

# yayinla.sh: SPA artifact'ını GitHub'dan indirir (curl) ve release JSON'unu okur (jq).
# Node/npm KURULMAZ (bilinçli): SPA sunucuda derlenmez, CI artifact'ı kullanılır.
for paket in curl jq; do
    command -v "$paket" >/dev/null || apt-get install -y -qq "$paket"
done
tamam "curl + jq (SPA artifact indirme)"
if command -v node >/dev/null || command -v npm >/dev/null; then
    uyari "Sunucuda Node/npm var — yayın için GEREKMEZ ve kullanılmaz (SPA CI'da derlenir)."
fi

# ---------------------------------------------------------------- 2. kullanıcı + dizinler
bilgi "Kullanıcı ve dizinler"
id -u "$APP_USER" >/dev/null 2>&1 || useradd --system --create-home --shell /usr/sbin/nologin "$APP_USER"
install -d -o "$APP_USER" -g "$APP_USER" -m 0750 "$APP_DIR" "$LOG_DIR"
# DP key-ring /opt/racar ALTINDA DEĞİL: deploy dizini değişince ring ile birlikte PII gitmesin.
install -d -o "$APP_USER" -g "$APP_USER" -m 0700 "$DP_DIR"
install -d -m 0700 "$YEDEK_DIR"
install -d -m 0750 "$ENV_DIR"
tamam "$APP_USER · $APP_DIR · $DP_DIR · $LOG_DIR"

# ---------------------------------------------------------------- 3. PostgreSQL rolleri
bilgi "PostgreSQL rolleri (idempotent)"
sudo -u postgres psql -q -f "$REPO_DIR/scripts/db-init-roles.sql" >/dev/null
# GÜVENLİK KİLİDİ: tenant izolasyonunun TAMAMI racar_app'in RLS'i atlayamamasına dayanır.
if sudo -u postgres psql -tAc \
    "select rolbypassrls or rolsuper from pg_roles where rolname='racar_app'" | grep -qi '^t'; then
    hata "racar_app RLS'i atlayabiliyor (BYPASSRLS/SUPERUSER). Tenant izolasyonu ÇÖKER. Düzelt:
       sudo -u postgres psql -c 'ALTER ROLE racar_app NOSUPERUSER NOBYPASSRLS;'"
fi
tamam "racar_app NOBYPASSRLS doğrulandı"

# ---------------------------------------------------------------- 4. ortam dosyası
bilgi "Ortam dosyası"
if [[ -f "$ENV_FILE" ]]; then
    tamam "$ENV_FILE zaten var — DOKUNULMADI (anahtarlar korundu)"
else
    # İlk kurulum: güçlü anahtarları BİZ üretelim ki kimse örnek değerlerle prod'a çıkmasın.
    APP_PW="$(openssl rand -base64 24 | tr -d '/+=' | head -c 24)"
    OWNER_PW="$(openssl rand -base64 24 | tr -d '/+=' | head -c 24)"
    sed -e "s|DEGISTIR_APP_PW|$APP_PW|" \
        -e "s|DEGISTIR_OWNER_PW|$OWNER_PW|" \
        -e "s|Pii__HmacKey=DEGISTIR_openssl_rand_base64_48|Pii__HmacKey=$(openssl rand -base64 48)|" \
        -e "s|Jwt__Key=DEGISTIR_openssl_rand_base64_48|Jwt__Key=$(openssl rand -base64 48)|" \
        "$REPO_DIR/deploy/ortam.ornek" > "$ENV_FILE"
    sudo -u postgres psql -q -c "ALTER ROLE racar_app  WITH PASSWORD '$APP_PW';"
    sudo -u postgres psql -q -c "ALTER ROLE racar_owner WITH PASSWORD '$OWNER_PW';"
    uyari "$ENV_FILE üretildi; Platform__AdminUser/AdminPasswordHash HÂLÂ 'DEGISTIR' — doldurmadan ERP açılmaz."
fi
chown root:"$APP_USER" "$ENV_FILE"; chmod 0640 "$ENV_FILE"
# Var olan dosya ezilmediği için F2.2'den önce kurulmuş sunucularda bu satır elle eklenir.
grep -q '^RACAR_GH_TOKEN=' "$ENV_FILE" \
    || uyari "$ENV_FILE içinde RACAR_GH_TOKEN yok — yayinla.sh SPA artifact'ını indiremez (docs/ops/f2-2-sunucu-adimlari.md)."
tamam "$ENV_FILE (0640 root:$APP_USER)"

# ---------------------------------------------------------------- 5. systemd
bilgi "systemd servisleri"
install -m 0644 "$REPO_DIR"/deploy/systemd/racar-*.service /etc/systemd/system/
systemctl daemon-reload
# enable ama START ETME: /opt/racar/current henüz yok (yayinla.sh dolduracak).
systemctl enable -q racar-web racar-publicsite
tamam "racar-web, racar-publicsite etkin (API'yi kullanacaksan: systemctl enable racar-api)"

# ---------------------------------------------------------------- 6. yedekleme
bilgi "Günlük yedek (systemd timer)"
install -m 0755 "$REPO_DIR/scripts/db-backup.sh" /usr/local/bin/racar-db-backup
cat > /etc/systemd/system/racar-backup.service <<UNIT
[Unit]
Description=RentACar gunluk veritabani yedegi
[Service]
Type=oneshot
User=postgres
Environment=RACAR_BACKUP_DIR=$YEDEK_DIR
ExecStart=/usr/local/bin/racar-db-backup
UNIT
cat > /etc/systemd/system/racar-backup.timer <<'UNIT'
[Unit]
Description=RentACar gunluk yedek zamanlayici
[Timer]
OnCalendar=*-*-* 03:30:00
Persistent=true
[Install]
WantedBy=timers.target
UNIT
systemctl daemon-reload && systemctl enable -q --now racar-backup.timer
tamam "Her gün 03:30 — $YEDEK_DIR"

# ---------------------------------------------------------------- 7. özet
echo
bilgi "Kurulum tamam. SIRADAKİ ADIMLAR (script bunları yapmaz):"
cat <<SON

  1) $ENV_FILE içindeki 'DEGISTIR' kalanları doldur:
       Platform__AdminUser / Platform__AdminPasswordHash
       RACAR_GH_TOKEN  (SPA artifact'ı için SALT-OKUR GitHub token'ı — docs/ops/f2-2-sunucu-adimlari.md)
     grep DEGISTIR $ENV_FILE

  2) Caddyfile: deploy/Caddyfile.ornek → /etc/caddy/Caddyfile (domainleri kendi domaininle değiştir)
     DNS ÖN KOŞULU: *.senindomainin.com için sunucu IP'sine A kaydı ÖNCEDEN kurulu olmalı,
     yoksa hiçbir tenant subdomain'i sertifika ALAMAZ.

  3) Uygulamayı yayınla:   ./deploy/yayinla.sh
  4) Doğrula:              ./deploy/dogrula.sh senindomainin.com

  YEDEKLE (kaybolursa veri KURTARILAMAZ, üçü birden):
     $ENV_FILE   (Pii__HmacKey)
     $DP_DIR     (DataProtection key-ring)
     $YEDEK_DIR  (pg_dump)

SON
