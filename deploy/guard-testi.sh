#!/usr/bin/env bash
# yayinla.sh'ın ÖLÜMCÜL guard'larını gerçekten çalıştırır (publish/systemd gerektirmeden).
#
#   ./deploy/guard-testi.sh
#
# Neden var: bu guard'lar veri kaybını önlüyor (DataProtection key-ring kaybı = şifreli PII'nin
# KALICI olarak çözülemez hale gelmesi). Hiç çalıştırılmamış bir guard, çalıştığı varsayılan
# bir guard'dır. Her biri burada TETİKLENİYOR ve doğru mesajla durduğu doğrulanıyor.
set -uo pipefail

KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GECTI=0; KALDI=0
T=$(mktemp -d)
trap 'rm -rf "$T"' EXIT

ok()   { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; GECTI=$((GECTI+1)); }
kotu() { printf '\033[1;31m  ✗\033[0m %s\n' "$*"; KALDI=$((KALDI+1)); }

# yayinla.sh'ı test kipinde çalıştırır; guard'lar geçerse "GUARDLAR_GECTI" basıp durur.
#
# Çıktı BORUYA değil DEĞİŞKENE alınır: guard tetiklendiğinde yayinla.sh 1 ile çıkıyor ve
# `set -o pipefail` yüzünden `kos | grep -q` boru hattı grep BULSA BİLE başarısız dönüyordu —
# yani testler guard çalıştığı hâlde "çalışmadı" diyordu (ilk koşuda 4 yanlış-negatif).
kos() {
  RACAR_TEST=1 RACAR_ENV_FILE="$1" RACAR_APP_DIR="${2:-$T/opt}" \
    bash "$KOK/deploy/yayinla.sh" 2>&1 || true
}
# Beklenen metni içeriyor mu (çıkış kodundan BAĞIMSIZ).
icerir() { grep -q "$2" <<<"$1"; }

echo "yayinla.sh guard testleri"

# 1) Ortam dosyası YOK
CIKTI=$(kos "$T/olmayan.env")
if icerir "$CIKTI" "yok — önce"; then
  ok "ortam dosyası yoksa durur"
else kotu "ortam dosyası yokken durmadı"; fi

# 2) Doldurulmamış DEGISTIR değeri
cat > "$T/degistir.env" <<EOF
Twilio__X=y
Pii__HmacKey=DEGISTIR_openssl_rand_base64_48
RACAR_DP_KEYS=$T/dp
EOF
CIKTI=$(kos "$T/degistir.env")
if icerir "$CIKTI" "DEGISTIR"; then
  ok "doldurulmamış DEGISTIR değeri yakalanıyor"
else kotu "DEGISTIR değeri yakalanmadı"; fi

# 3) RACAR_DP_KEYS yayın dizininin ALTINDA — en tehlikeli hata (her deploy key-ring'i siler)
mkdir -p "$T/opt/dp-keys"
printf 'RACAR_DP_KEYS=%s\n' "$T/opt/dp-keys" > "$T/altinda.env"
CIKTI=$(kos "$T/altinda.env" "$T/opt")
if icerir "$CIKTI" "yayın dizininin ALTINDA"; then
  ok "key-ring yayın dizini altındaysa REDDEDİLİYOR"
else kotu "key-ring yayın dizini altındayken geçti (VERİ KAYBI RİSKİ)"; fi

# 4) DP dizini hiç yok
printf 'RACAR_DP_KEYS=%s\n' "$T/olmayan-dizin" > "$T/yokdizin.env"
CIKTI=$(kos "$T/yokdizin.env")
if icerir "$CIKTI" "dizini yok"; then
  ok "key-ring dizini yoksa durur"
else kotu "olmayan key-ring dizini geçti"; fi

mkdir -p "$T/dpkeys"; : > "$T/dpkeys/key.xml"

# 5) RACAR_DP_KEYS satırı HİÇ yok — eskiden pipefail yüzünden mesajsız ölüyordu
printf 'RACAR_GH_TOKEN=deneme_token_1\n' > "$T/dpsiz.env"
CIKTI=$(kos "$T/dpsiz.env")
if icerir "$CIKTI" "RACAR_DP_KEYS tanımsız"; then
  ok "RACAR_DP_KEYS satırı yoksa açık mesajla durur"
else kotu "RACAR_DP_KEYS satırı yokken açık mesaj yok"; fi

# 6) SPA artifact token'ı YOK (F2.2)
printf 'RACAR_DP_KEYS=%s\n' "$T/dpkeys" > "$T/tokensiz.env"
CIKTI=$(kos "$T/tokensiz.env")
if icerir "$CIKTI" "RACAR_GH_TOKEN tanımsız"; then
  ok "GitHub token'ı yoksa durur"
else kotu "GitHub token'ı yokken geçti"; fi

# 7) Token biçimi bozuk (curl config enjeksiyonu) — ve değer ÇIKTIYA YAZILMAZ
printf 'RACAR_DP_KEYS=%s\nRACAR_GH_TOKEN=gizli deger"x\n' "$T/dpkeys" > "$T/bozuktoken.env"
CIKTI=$(kos "$T/bozuktoken.env")
if icerir "$CIKTI" "biçimi geçersiz" && ! icerir "$CIKTI" "gizli deger"; then
  ok "bozuk token reddediliyor, değeri yazdırılmıyor"
else kotu "bozuk token geçti ya da değeri çıktıya sızdı"; fi

# 8) Hepsi doğruyken guard'lar GEÇMELİ (yanlış-pozitif yok) — token değeri çıktıda GÖRÜNMEZ
printf 'RACAR_DP_KEYS=%s\nRACAR_GH_TOKEN=deneme_token_1\n' "$T/dpkeys" > "$T/saglam.env"
CIKTI=$(kos "$T/saglam.env")
if icerir "$CIKTI" "GUARDLAR_GECTI" && ! icerir "$CIKTI" "deneme_token_1"; then
  ok "geçerli yapılandırmada guard'lar geçiyor (token yazdırılmadı)"
else kotu "geçerli yapılandırma reddedildi (yanlış pozitif) ya da token sızdı"; fi

printf '\n%d geçti · %d kaldı\n' "$GECTI" "$KALDI"
[[ "$KALDI" -eq 0 ]] || exit 1
