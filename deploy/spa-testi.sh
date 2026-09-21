#!/usr/bin/env bash
# yayinla.sh'ın SPA artifact adımını (indir → sha256 → aç → chunk taşı) SAHTE bir GitHub API'sine karşı
# gerçekten çalıştırır. Sunucu, systemd, dotnet publish ya da gerçek token GEREKMEZ.
#
#   ./deploy/spa-testi.sh          (python3, curl, jq, tar gerekir)
#
# Kanıtlananlar: doğru artifact açılır; checksum uyuşmazlığı, eksik artifact, yanlış SHA ve geçersiz
# token REDDEDİLİR (yarım release kalmaz, current değişmez); önceki release'in YALNIZ kendi chunks.txt'i
# taşınır (birikmez); token yönlendirilen depolama host'una gitmez ve hiçbir çıktıya yazılmaz.
set -uo pipefail

KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GECTI=0; KALDI=0
T=$(mktemp -d)
SUNUCU_PID=""
trap '[[ -n "$SUNUCU_PID" ]] && kill "$SUNUCU_PID" 2>/dev/null; rm -rf "$T"' EXIT

ok()   { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; GECTI=$((GECTI+1)); }
kotu() { printf '\033[1;31m  ✗\033[0m %s\n' "$*"; KALDI=$((KALDI+1)); }
icerir() { grep -qF -- "$2" <<<"$1"; }

for arac in python3 curl jq tar git; do
  command -v "$arac" >/dev/null || { echo "'$arac' gerekli"; exit 2; }
done

SHA="$(git -C "$KOK" rev-parse --verify HEAD)"
BASKA_SHA="$(printf '%040d' 0 | tr 0 a)"
ETIKET="spa-$SHA"
TOKEN="deneme_token_42"
YAYIN="$T/yayin"            # sahte GitHub: $YAYIN/<etiket>/<asset>
LOG="$T/istek.log"          # her istek: "<ilk yol parçası> <Authorization var|yok>"

# ------------------------------------------------------------------ sahte GitHub API
cat > "$T/sahte_api.py" <<'PY'
import http.server, json, os, sys
kok, port_dosya, token, log = sys.argv[1:5]
ON = '/repos/sahte/repo/releases/'

class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def yaz(self, kod, govde=b'', tur='application/json', ek=None):
        self.send_response(kod)
        self.send_header('Content-Type', tur)
        self.send_header('Content-Length', str(len(govde)))
        for k, v in (ek or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(govde)

    def do_GET(self):
        auth = self.headers.get('Authorization')
        port = self.server.server_address[1]
        p = self.path
        with open(log, 'a') as f:
            f.write(p.split('/')[1] + ' ' + ('var' if auth else 'yok') + '\n')
        if p.startswith('/depo/'):  # "S3": token GELMEMELİ
            yol = os.path.join(kok, p[len('/depo/'):])
            if not os.path.isfile(yol):
                return self.yaz(404)
            with open(yol, 'rb') as f:
                return self.yaz(200, f.read(), 'application/octet-stream')
        if auth != 'Bearer ' + token:
            return self.yaz(401, b'{"message":"Bad credentials"}')
        if p.startswith(ON + 'tags/'):
            etiket = p[len(ON + 'tags/'):]
            d = os.path.join(kok, etiket)
            if not os.path.isdir(d):
                return self.yaz(404, b'{"message":"Not Found"}')
            assets = [{'name': a, 'url': 'http://127.0.0.1:%d%sassets/%s/%s' % (port, ON, etiket, a)}
                      for a in sorted(os.listdir(d))]
            return self.yaz(200, json.dumps({'tag_name': etiket, 'assets': assets}).encode())
        if p.startswith(ON + 'assets/'):
            if self.headers.get('Accept') != 'application/octet-stream':
                return self.yaz(415)
            # Gerçekteki gibi BAŞKA host'a yönlendir (localhost ≠ 127.0.0.1).
            return self.yaz(302, ek={'Location': 'http://localhost:%d/depo/%s' % (port, p[len(ON + 'assets/'):])})
        self.yaz(404)

s = http.server.ThreadingHTTPServer(('127.0.0.1', 0), H)
with open(port_dosya, 'w') as f:
    f.write(str(s.server_address[1]))
s.serve_forever()
PY
mkdir -p "$YAYIN"
python3 "$T/sahte_api.py" "$YAYIN" "$T/port" "$TOKEN" "$LOG" &
SUNUCU_PID=$!
disown "$SUNUCU_PID" 2>/dev/null || true
for _ in $(seq 1 50); do [[ -s "$T/port" ]] && break; sleep 0.1; done
PORT="$(cat "$T/port" 2>/dev/null)"
[[ -n "$PORT" ]] || { echo "sahte API başlamadı"; exit 2; }

# ------------------------------------------------------------------ sahte artifact
# artifact_yap <SURUM'a yazılacak sha> <sha256 dosyasındaki arşiv adı> [bozuk-checksum]
artifact_yap() {
  local surum="$1" sha_ad="$2" bozuk="${3:-}" s="$T/sahne" d="$YAYIN/$ETIKET"
  rm -rf "$s" "$d"; mkdir -p "$s/browser/media" "$d"
  printf '<!doctype html><title>yeni</title>\n' > "$s/browser/index.html"
  printf 'yeni-main\n'  > "$s/browser/main-YENI1234.js"
  printf 'YENI-ortak\n' > "$s/browser/chunk-ORTAK123.js"
  printf 'yeni-font\n'  > "$s/browser/media/font-YENIFNT1.woff2"
  printf 'main-YENI1234.js\nchunk-ORTAK123.js\nmedia/font-YENIFNT1.woff2\n' > "$s/chunks.txt"
  printf '%s\n' "$surum" > "$s/SURUM"
  COPYFILE_DISABLE=1 tar -czf "$d/$ETIKET.tar.gz" -C "$s" browser chunks.txt SURUM
  local h
  h="$(shasum -a 256 "$d/$ETIKET.tar.gz" | awk '{print $1}')"
  [[ -n "$bozuk" ]] && h="$(printf '%064d' 0)"
  printf '%s  %s\n' "$h" "$sha_ad" > "$d/$ETIKET.tar.gz.sha256"
}

# ------------------------------------------------------------------ önceki release (current)
ESKI="$T/opt/releases/20000101-000000"
mkdir -p "$ESKI/app/browser/media"
printf 'eski-main\n'  > "$ESKI/app/browser/main-ESKI1234.js"
printf 'ESKI-ortak\n' > "$ESKI/app/browser/chunk-ORTAK123.js"
printf 'eski-font\n'  > "$ESKI/app/browser/media/font-ESKIFNT1.woff2"
# Daha eski bir yayından TAŞINMIŞ dosya: eski release'in kendi listesinde YOK → bir daha taşınmamalı.
printf 'cok-eski\n'   > "$ESKI/app/browser/chunk-COKESKI9.js"
printf 'main-ESKI1234.js\r\nchunk-ORTAK123.js\n\n../../../../etc/passwd\nmedia/font-ESKIFNT1.woff2' \
  > "$ESKI/app/chunks.txt"
ln -sfn "$ESKI" "$T/opt/current"

mkdir -p "$T/dp"; : > "$T/dp/key.xml"
printf 'RACAR_DP_KEYS=%s\nRACAR_GH_TOKEN=%s\nRACAR_GH_REPO=sahte/repo\n' "$T/dp" "$TOKEN" > "$T/racar.env"
sed "s/^RACAR_GH_TOKEN=.*/RACAR_GH_TOKEN=yanlis_token_1/" "$T/racar.env" > "$T/yanlis.env"

# yayinla.sh'ı SPA test kipinde koşar; KOD ve CIKTI ana kabukta set edilir (alt kabuk değil).
KOD=0; CIKTI=""
kos() {
  RACAR_TEST=spa RACAR_ENV_FILE="${1:-$T/racar.env}" RACAR_APP_DIR="$T/opt" \
    RACAR_GH_API="http://127.0.0.1:$PORT" RACAR_GH_PROTO="=http,https" TMPDIR="$T" \
    bash "$KOK/deploy/yayinla.sh" > "$T/cikti" 2>&1
  KOD=$?
  CIKTI="$(cat "$T/cikti")"
}
release_sayisi() { find "$T/opt/releases" -mindepth 1 -maxdepth 1 -type d | wc -l | tr -d ' '; }
current_eski_mi() { [[ "$(readlink "$T/opt/current")" == "$ESKI" ]]; }
reddedildi_mi() {  # <ad> <çıktı> <beklenen metin>
  if [[ $KOD -ne 0 ]] && icerir "$2" "$3" && [[ "$(release_sayisi)" == "1" ]] && current_eski_mi; then
    ok "$1 → reddedildi (çıkış $KOD), yarım release yok, current değişmedi"
  else kotu "$1 → REDDEDİLMEDİ ya da iz bıraktı (çıkış $KOD, release: $(release_sayisi)): $2"; fi
}

echo "yayinla.sh SPA artifact testleri (commit $SHA)"

# 1) Doğru artifact
artifact_yap "$SHA" "$ETIKET.tar.gz"
: > "$LOG"
kos
YENI="$(sed -n 's/^SPA_HAZIR //p' <<<"$CIKTI")"
if [[ $KOD -eq 0 && -n "$YENI" && -f "$YENI/app/browser/index.html" && "$(cat "$YENI/app/SURUM")" == "$SHA" ]]; then
  ok "doğru SHA'nın artifact'ı indirildi, sha256 doğrulandı, releases/<ts>/app/'e açıldı"
else kotu "doğru artifact açılamadı (çıkış $KOD): $CIKTI"; fi
if [[ -f "$YENI/app/browser/main-ESKI1234.js" && -f "$YENI/app/browser/media/font-ESKIFNT1.woff2" ]]; then
  ok "önceki release'in chunk'ları taşındı (alt dizin dahil, CRLF satırı dahil)"
else kotu "önceki chunk'lar taşınmadı"; fi
if [[ "$(cat "$YENI/app/browser/chunk-ORTAK123.js" 2>/dev/null)" == "YENI-ortak" ]]; then
  ok "aynı adlı dosya EZİLMEDİ (cp -n)"
else kotu "yeni sürümün dosyası eskisiyle ezildi"; fi
if [[ ! -e "$YENI/app/browser/chunk-COKESKI9.js" ]] && ! grep -q ESKI "$YENI/app/chunks.txt"; then
  ok "yalnız önceki release'in KENDİ listesi taşındı; yeni chunks.txt taşınanları içermiyor (birikmez)"
else kotu "taşıma sınırsız: daha eski chunk taşındı ya da yeni listeye girdi"; fi
if icerir "$CIKTI" "Geçersiz chunk adı atlandı" && [[ ! -e "$YENI/app/browser/etc" && ! -e "$T/etc" ]]; then
  ok "'..' içeren chunk adı atlandı"
else kotu "'..' içeren chunk adı atlanmadı"; fi
if current_eski_mi; then ok "spa test kipi current'a dokunmadı"; else kotu "current değişti"; fi
if grep -q '^repos var$' "$LOG" && ! grep -q '^repos yok$' "$LOG" && grep -q '^depo yok$' "$LOG" && ! grep -q '^depo var$' "$LOG"; then
  ok "token yalnız API'ye gitti, yönlendirilen depolama host'una GİTMEDİ"
else kotu "Authorization başlığı beklenmedik: $(sort "$LOG" | uniq -c | tr '\n' ' ')"; fi
if ! icerir "$CIKTI" "$TOKEN"; then ok "token çıktıda yok"; else kotu "TOKEN ÇIKTIYA SIZDI"; fi
rm -rf "$YENI"

# 2) Checksum uyuşmuyor
artifact_yap "$SHA" "$ETIKET.tar.gz" bozuk
kos; reddedildi_mi "checksum uyuşmazlığı" "$CIKTI" "CHECKSUM UYUŞMUYOR"

# 3) sha256 dosyası başka bir SHA'nın arşivini tarif ediyor
artifact_yap "$SHA" "spa-$BASKA_SHA.tar.gz"
kos; reddedildi_mi "sha256'da yanlış SHA" "$CIKTI" "yanlış SHA"

# 4) Arşiv başka commit'in (SURUM) ama checksum tutarlı — açıldıktan sonra yakalanır, yarım release silinir
artifact_yap "$BASKA_SHA" "$ETIKET.tar.gz"
kos; reddedildi_mi "arşiv içinde yanlış SHA" "$CIKTI" "başka bir commit'e ait"

# 5) Bu SHA için release hiç yok (CI bitmemiş / main'e push edilmemiş)
rm -rf "${YAYIN:?}/$ETIKET"
kos; reddedildi_mi "artifact yok" "$CIKTI" "SPA artifact'ı YOK"

# 6) Release var ama .sha256 asset'i eksik
artifact_yap "$SHA" "$ETIKET.tar.gz"
rm -f "$YAYIN/$ETIKET/$ETIKET.tar.gz.sha256"
kos; reddedildi_mi "sha256 asset'i eksik" "$CIKTI" "tar.gz.sha256' yok"

# 7) Geçersiz token
artifact_yap "$SHA" "$ETIKET.tar.gz"
kos "$T/yanlis.env"; reddedildi_mi "geçersiz token" "$CIKTI" "HTTP 401"
if ! icerir "$CIKTI" "yanlis_token_1"; then ok "geçersiz token da çıktıda yok"; else kotu "TOKEN ÇIKTIYA SIZDI"; fi

printf '\n%d geçti · %d kaldı\n' "$GECTI" "$KALDI"
[[ "$KALDI" -eq 0 ]] || exit 1
