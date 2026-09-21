#!/usr/bin/env bash
# WhatsApp sandbox denemesi — kimlik dosyasını doğrular, ERP'yi onunla başlatır, TEK test mesajı
# gönderir ve Twilio'nun cevabını raporlar.
#
#   ./deploy/whatsapp-dene.sh +905321112233
#
# Kimlik dosyası: ~/.racar-twilio.env  (değerler ekrana BASILMAZ)
set -uo pipefail

HEDEF="${1:-}"
ENVF="$HOME/.racar-twilio.env"
PORT=5225

hata() { printf '\033[1;31mHATA:\033[0m %s\n' "$*" >&2; exit 1; }
ok()   { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; }

[[ -n "$HEDEF" ]] || hata "Hedef numara ver: ./deploy/whatsapp-dene.sh +905321112233"
[[ -f "$ENVF" ]]  || hata "$ENVF yok."
# ERP giriş kimliği ortamdan (repoda sabit parola YOK): RACAR_GIRIS_SIFRE zorunlu,
# firma/kullanıcı varsayılanı seed (yucerent/umit).
GIRIS_FIRMA="${RACAR_GIRIS_FIRMA:-yucerent}"
GIRIS_KULLANICI="${RACAR_GIRIS_KULLANICI:-umit}"
[[ -n "${RACAR_GIRIS_SIFRE:-}" ]] || hata "RACAR_GIRIS_SIFRE ver (ERP giriş parolası; seed için Seed:Parola / açılış logu)."

# --- kimlik DOĞRULAMA (değer basmadan) ---
sid_len=$(awk -F= '/^Twilio__AccountSid=/{print length($2)}' "$ENVF")
tok_len=$(awk -F= '/^Twilio__AuthToken=/{print length($2)}' "$ENVF")
sid_pre=$(awk -F= '/^Twilio__AccountSid=/{print substr($2,1,2)}' "$ENVF")

[[ "$sid_len" == "34" ]] || hata "Account SID $sid_len karakter — 34 olmalı (AC + 32 hex). Şablon değeri kalmış olabilir."
[[ "$sid_pre" == "AC" ]] || hata "Account SID 'AC' ile başlamalı."
[[ "$tok_len" == "32" ]] || hata "Auth Token $tok_len karakter — 32 olmalı. Şablon değeri kalmış olabilir."
ok "Kimlik biçimi doğru (SID 34, token 32)"

# --- ERP'yi bu kimlikle ayrı portta başlat ---
set -a
# shellcheck disable=SC1090  # yol kullanıcıya göre değişir (~/.racar-twilio.env)
. "$ENVF"
set +a
export ASPNETCORE_URLS="http://localhost:$PORT"
# --no-launch-profile ortam değişkenini de düşürüyor → uygulama Production sanıp guard'lara
# takılıyordu (Platform:AdminUser zorunlu). Bu YEREL bir deneme; ortamı açıkça Development yap.
export ASPNETCORE_ENVIRONMENT=Development
pkill -f "RentACar.Web" 2>/dev/null; sleep 2
# --no-launch-profile ŞART: launchSettings.json kendi applicationUrl'ini dayatıyor ve
# ASPNETCORE_URLS'i EZİYOR (ilk denemede uygulama 5220'de açıldı, sağlık kontrolü 5225'i
# yokladığı için "açılmadı" sandık). Profil devre dışı kalınca env değişkeni geçerli olur.
dotnet run --project src/RentACar.Web --no-launch-profile > /tmp/wa-dene.log 2>&1 &
APP=$!
for _ in $(seq 1 40); do
    curl -fsS -o /dev/null "http://localhost:$PORT/health/live" 2>/dev/null && break
    sleep 3
done
curl -fsS -o /dev/null "http://localhost:$PORT/health/live" 2>/dev/null || {
    tail -20 /tmp/wa-dene.log; kill $APP 2>/dev/null; hata "ERP açılmadı."
}
ok "ERP ayakta (:$PORT), Twilio config yüklendi"

# --- giriş + test gönderimi (ERP'nin KENDİ ucu; üretimdeki kod yolu) ---
J=/tmp/wa-cookies.txt; rm -f $J
tok() { curl -s -c $J -b $J "$1" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+"' | head -1 | sed 's/.*value="//;s/"//'; }
T=$(tok "http://localhost:$PORT/login")
curl -s -c $J -b $J -o /dev/null -X POST "http://localhost:$PORT/auth/login" \
  -d "__RequestVerificationToken=$T" -d "firma=$GIRIS_FIRMA" -d "kullanici=$GIRIS_KULLANICI" \
  --data-urlencode "sifre=$RACAR_GIRIS_SIFRE"
T2=$(tok "http://localhost:$PORT/ayarlar")
LOC=$(curl -s -c $J -b $J -o /dev/null -D - -X POST "http://localhost:$PORT/ayarlar/whatsapp-test" \
  -d "__RequestVerificationToken=$T2" -d "testNo=$HEDEF" | grep -i '^location:' | tr -d '\r' | sed 's/^[Ll]ocation: *//')

echo
# Sonuç UCUN KENDİ CEVABINDAN okunur (log tahmininden değil): uç ya ?ok=1 ya da ?hata=<mesaj>
# döner ve o mesaj Twilio'nun gerçek teslim durumundan üretilir.
if [[ "$LOC" == *"ok=1"* ]]; then
    printf '\033[1;32mTESLIM EDILDI\033[0m - telefonunu kontrol et.\n'
elif [[ "$LOC" == *"hata="* ]]; then
    MSG=$(printf '%s' "${LOC#*hata=}" | python3 -c "import sys,urllib.parse;print(urllib.parse.unquote(sys.stdin.read()))")
    printf '\033[1;31mBASARISIZ\033[0m %s\n' "$MSG"
else
    printf '\033[1;33mBELIRSIZ\033[0m - uc beklenmedik yanit verdi: %s\n' "$LOC"
fi
kill $APP 2>/dev/null
