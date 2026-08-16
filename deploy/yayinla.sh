#!/usr/bin/env bash
# RentACar — YAYINLAMA (ilk deploy ve sonraki güncellemeler için AYNI script).
#
# Sunucuda, repo kökünden:  sudo ./deploy/yayinla.sh
#
# Nasıl çalışır:
#   /opt/racar/releases/<zaman>/{web,publicsite,api}   ← yeni sürüm buraya açılır
#   /opt/racar/current -> releases/<zaman>              ← symlink ATOMİK çevrilir
#   sağlık geçmezse symlink ESKİ sürüme geri döner ve servisler yeniden başlar.
#
# İki bilinçli karar:
#   1) `-r linux-x64` ile publish: taşınabilir publish 611 MB (SkiaSharp'ın TÜM platform ikilileri
#      + 80 MB'lık .pdb sembolleri); RID hedefliyken 122 MB. 5 kat küçük disk, 5 kat hızlı deploy.
#   2) Sağlık kapısı /health/ready (canlılık DEĞİL): DB + migrator + DataProtection key-ring'i
#      kontrol eder. Süreç ayakta ama DB'ye ulaşamıyorsa "başarılı deploy" demek yanlış olurdu.
set -euo pipefail

APP_USER="${RACAR_USER:-racar}"
# Yollar env ile geçersiz kılınabilir — YALNIZ test içindir (deploy/guard-testi.sh güvenlik
# guard'larını gerçekten çalıştırabilsin diye). Üretimde hiçbiri set edilmez, varsayılan geçerlidir.
APP_DIR="${RACAR_APP_DIR:-/opt/racar}"
ENV_FILE="${RACAR_ENV_FILE:-/etc/racar/racar.env}"
REL_DIR="$APP_DIR/releases"
CURRENT="$APP_DIR/current"
TUT="${RACAR_KEEP_RELEASES:-3}"
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_DAHIL="${RACAR_WITH_API:-0}"

bilgi() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
tamam() { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; }
hata()  { printf '\033[1;31mHATA:\033[0m %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 || "${RACAR_TEST:-0}" == "1" ]] || hata "root gerekir: sudo ./deploy/yayinla.sh"
[[ -f "$ENV_FILE" ]] || hata "$ENV_FILE yok — önce ./deploy/kurulum.sh çalıştır."

if grep -q 'DEGISTIR' "$ENV_FILE"; then
    hata "$ENV_FILE içinde doldurulmamış 'DEGISTIR' değerleri var:
$(grep -n 'DEGISTIR' "$ENV_FILE" | sed 's/=.*/=.../')"
fi

# DP key-ring KONTROLÜ — deploy'un en tehlikeli noktası. Ring /opt/racar altında olsaydı bu script
# onu her sürümde yeni bir dizine taşır ve şifreli PII'yi KALICI olarak çözülemez hale getirirdi.
DP="$(grep -E '^RACAR_DP_KEYS=' "$ENV_FILE" | cut -d= -f2-)"
[[ -n "$DP" ]] || hata "RACAR_DP_KEYS tanımsız."
case "$DP" in
    "$APP_DIR"/*) hata "RACAR_DP_KEYS ($DP) yayın dizininin ALTINDA — her deploy'da key-ring kaybolur.
       /var/lib/racar/dp-keys gibi yayından BAĞIMSIZ bir yola taşı." ;;
esac
[[ -d "$DP" ]] || hata "RACAR_DP_KEYS dizini yok: $DP"
tamam "DP key-ring güvende: $DP ($(find "$DP" -type f | wc -l | tr -d ' ') dosya)"

# Guard testi buraya kadar koşar: publish/systemd gerektirmeden tüm ölümcül kontroller bitti.
[[ "${RACAR_TEST:-0}" == "1" ]] && { echo "GUARDLAR_GECTI"; exit 0; }

# ---------------------------------------------------------------- publish
DAMGA="$(date +%Y%m%d-%H%M%S)"
YENI="$REL_DIR/$DAMGA"
bilgi "Yayın hazırlanıyor: $DAMGA"
mkdir -p "$YENI"

yayinla_proje() {
    local proje="$1" hedef="$2"
    dotnet publish "$REPO_DIR/src/$proje" -c Release -r linux-x64 --self-contained false \
        -o "$YENI/$hedef" --nologo -v q >/dev/null \
        || hata "$proje publish başarısız."
    # Semboller sunucuda gereksiz (80+ MB) — üretim diskini şişirmesin.
    find "$YENI/$hedef" -name '*.pdb' -delete
    tamam "$proje → $(du -sh "$YENI/$hedef" | cut -f1)"
}
yayinla_proje RentACar.Web web
yayinla_proje RentACar.PublicSite publicsite
[[ "$API_DAHIL" == "1" ]] && yayinla_proje RentACar.Api api

chown -R "$APP_USER:$APP_USER" "$YENI"

# ---------------------------------------------------------------- symlink çevir
ONCEKI=""
[[ -L "$CURRENT" ]] && ONCEKI="$(readlink -f "$CURRENT")"
ln -sfn "$YENI" "$CURRENT"
tamam "current → $DAMGA${ONCEKI:+ (önceki: $(basename "$ONCEKI"))}"

SERVISLER=(racar-web racar-publicsite)
[[ "$API_DAHIL" == "1" ]] && SERVISLER+=(racar-api)

bilgi "Servisler yeniden başlatılıyor"
# Web ÖNCE: açılışta migration'ı o çalıştırır (DbInitializer.MigrateAndSeedAsync).
systemctl restart racar-web
systemctl restart racar-publicsite
[[ "$API_DAHIL" == "1" ]] && systemctl restart racar-api || true

# ---------------------------------------------------------------- sağlık kapısı
saglik() {
    local ad="$1" url="$2"
    for _ in $(seq 1 30); do
        curl -fsS -o /dev/null --max-time 5 "$url" 2>/dev/null && { tamam "$ad hazır"; return 0; }
        sleep 2
    done
    printf '\033[1;31m  ✗\033[0m %s HAZIR DEĞİL (%s)\n' "$ad" "$url"
    return 1
}

bilgi "Sağlık kontrolü (en çok 60 sn)"
BASARILI=1
saglik "ERP"  "http://127.0.0.1:5220/health/ready" || BASARILI=0
saglik "Site" "http://127.0.0.1:5230/health/live"  || BASARILI=0
[[ "$API_DAHIL" == "1" ]] && { saglik "API" "http://127.0.0.1:5240/health/ready" || BASARILI=0; }

if [[ "$BASARILI" -eq 0 ]]; then
    echo
    printf '\033[1;31m--- son log satırları ---\033[0m\n'
    journalctl -u racar-web -u racar-publicsite -n 25 --no-pager || true
    if [[ -n "$ONCEKI" ]]; then
        bilgi "GERİ ALINIYOR → $(basename "$ONCEKI")"
        ln -sfn "$ONCEKI" "$CURRENT"
        systemctl restart "${SERVISLER[@]}"
        saglik "ERP (geri alınmış)" "http://127.0.0.1:5220/health/ready" \
            && tamam "Eski sürüme dönüldü; sistem ayakta." \
            || printf '\033[1;31mUYARI: eski sürüm de açılmıyor — elle müdahale gerekir.\033[0m\n'
        echo
        printf '\033[1;33mNOT:\033[0m Geri alma İKİLİLERİ döndürür, VERİTABANI ŞEMASINI DÖNDÜRMEZ.\n'
        printf '      Yeni sürüm migration uyguladıysa şema ileridedir; eski kod uyumsuzsa yedekten dön.\n'
    else
        hata "İlk yayın başarısız ve dönülecek sürüm yok. Log'a bak, düzelt, tekrar çalıştır."
    fi
    exit 1
fi

# ---------------------------------------------------------------- eski sürümleri temizle
bilgi "Eski sürümler (son $TUT tutulur)"
cd "$REL_DIR"
# shellcheck disable=SC2012
ls -1t | tail -n +$((TUT + 1)) | while read -r eski; do
    [[ "$REL_DIR/$eski" == "$(readlink -f "$CURRENT")" ]] && continue
    rm -rf "${REL_DIR:?}/$eski" && tamam "silindi: $eski"
done

echo
tamam "YAYIN TAMAM — $DAMGA"
echo "     Doğrulama:  ./deploy/dogrula.sh <domain>"
