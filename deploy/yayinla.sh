#!/usr/bin/env bash
# RentACar — YAYINLAMA (ilk deploy ve sonraki güncellemeler için AYNI script).
#
# Sunucuda, repo kökünden:  sudo ./deploy/yayinla.sh
#
# Nasıl çalışır:
#   /opt/racar/releases/<zaman>/{web,publicsite,api}   ← .NET, checkout edilen koddan burada derlenir
#   /opt/racar/releases/<zaman>/app/browser            ← yeni arayüz (SPA): CI'ın AYNI commit için ürettiği artifact
#   /opt/racar/current -> releases/<zaman>              ← symlink ATOMİK çevrilir
#   sağlık geçmezse symlink ESKİ sürüme geri döner ve servisler yeniden başlar.
#
# Bilinçli kararlar:
#   1) `-r linux-x64` ile publish: taşınabilir publish 611 MB (SkiaSharp'ın TÜM platform ikilileri
#      + 80 MB'lık .pdb sembolleri); RID hedefliyken 122 MB. 5 kat küçük disk, 5 kat hızlı deploy.
#   2) Sağlık kapısı /health/ready (canlılık DEĞİL): DB + migrator + DataProtection key-ring'i
#      kontrol eder. Süreç ayakta ama DB'ye ulaşamıyorsa "başarılı deploy" demek yanlış olurdu.
#   3) SPA sunucuda DERLENMEZ (Node yok, `npm ci` yok): root olarak rastgele lifecycle betiği sırların
#      yanında çalışırdı, derleme OOM'u Postgres'i öldürürdü. CI, main'deki her yeşil commit için
#      `spa-<sha>.tar.gz` + `.sha256` üretir; burada checkout edilen SHA'nınki SALT-OKUR token ile
#      indirilir ve checksum doğrulanır. Artifact yoksa / checksum tutmuyorsa yayın REDDEDİLİR
#      (yarım release silinir, current'a dokunulmaz).
#   4) Açık sekmeler: önceki release'in YALNIZ KENDİ chunks.txt'indeki hash'li dosyalar `cp -n` ile
#      yeni release'e taşınır (chown'dan önce). Taşınanlar yeni chunks.txt'e girmez → birikmez.
set -euo pipefail

APP_USER="${RACAR_USER:-racar}"
# Yollar env ile geçersiz kılınabilir — YALNIZ test içindir (deploy/guard-testi.sh ve deploy/spa-testi.sh
# guard'ları ve SPA adımını gerçekten çalıştırabilsin diye). Üretimde hiçbiri set edilmez.
APP_DIR="${RACAR_APP_DIR:-/opt/racar}"
ENV_FILE="${RACAR_ENV_FILE:-/etc/racar/racar.env}"
REL_DIR="$APP_DIR/releases"
CURRENT="$APP_DIR/current"
TUT="${RACAR_KEEP_RELEASES:-3}"
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_DAHIL="${RACAR_WITH_API:-0}"
# 0 = gerçek yayın · 1 = yalnız guard'lar · spa = guard'lar + SPA indir/doğrula/aç/chunk taşı (publish yok)
TEST_KIPI="${RACAR_TEST:-0}"
# Yalnız test (sahte API, http://127.0.0.1:…) için geçersiz kılınır; üretimde varsayılanlar geçerlidir.
GH_API="${RACAR_GH_API:-https://api.github.com}"
PROTO="${RACAR_GH_PROTO:-=https}"

bilgi() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
tamam() { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; }
uyari() { printf '\033[1;33m  ! \033[0m%s\n' "$*"; }
hata()  { printf '\033[1;31mHATA:\033[0m %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 || "$TEST_KIPI" != "0" ]] || hata "root gerekir: sudo ./deploy/yayinla.sh"
[[ -f "$ENV_FILE" ]] || hata "$ENV_FILE yok — önce ./deploy/kurulum.sh çalıştır."

if grep -q 'DEGISTIR' "$ENV_FILE"; then
    hata "$ENV_FILE içinde doldurulmamış 'DEGISTIR' değerleri var:
$(grep -n 'DEGISTIR' "$ENV_FILE" | sed 's/=.*/=.../')"
fi

# Ortam dosyasından tek değer okur (dosya SOURCE EDİLMEZ: bağlantı dizelerinde ';' var). Yoksa boş döner;
# `|| true` şart — pipefail altında eşleşmeyen grep atamayı düşürür ve script mesajsız ölürdü.
ortam_degeri() {
    local deger
    deger="$({ grep -E "^$1=" "$ENV_FILE" || true; } | tail -n 1 | cut -d= -f2- | tr -d '\r')"
    deger="${deger#\"}"
    deger="${deger%\"}"
    printf '%s' "$deger"
}

# DP key-ring KONTROLÜ — deploy'un en tehlikeli noktası. Ring /opt/racar altında olsaydı bu script
# onu her sürümde yeni bir dizine taşır ve şifreli PII'yi KALICI olarak çözülemez hale getirirdi.
DP="$(ortam_degeri RACAR_DP_KEYS)"
[[ -n "$DP" ]] || hata "RACAR_DP_KEYS tanımsız."
case "$DP" in
    "$APP_DIR"/*) hata "RACAR_DP_KEYS ($DP) yayın dizininin ALTINDA — her deploy'da key-ring kaybolur.
       /var/lib/racar/dp-keys gibi yayından BAĞIMSIZ bir yola taşı." ;;
esac
[[ -d "$DP" ]] || hata "RACAR_DP_KEYS dizini yok: $DP"
tamam "DP key-ring güvende: $DP ($(find "$DP" -type f | wc -l | tr -d ' ') dosya)"

# SPA artifact'ı için SALT-OKUR GitHub token'ı. DEĞERİ HİÇBİR ZAMAN YAZDIRILMAZ.
GH_TOKEN="$(ortam_degeri RACAR_GH_TOKEN)"
[[ -n "$GH_TOKEN" ]] || hata "RACAR_GH_TOKEN tanımsız — SPA artifact'ı indirilemez.
       Salt-okur (Contents: Read) fine-grained token oluşturup $ENV_FILE dosyasına ekle:
       docs/ops/f2-2-sunucu-adimlari.md"
# Biçim kilidi: token curl'e config satırı olarak gider; tırnak/boşluk/satır sonu enjeksiyonu olmasın.
[[ "$GH_TOKEN" =~ ^[A-Za-z0-9_]+$ ]] \
    || hata "RACAR_GH_TOKEN biçimi geçersiz (yalnız harf, rakam ve _ beklenir) — değeri kontrol et."
GH_REPO="$(ortam_degeri RACAR_GH_REPO)"
GH_REPO="${GH_REPO:-BurakkYuce/Rent-A-Car-Service}"
[[ "$GH_REPO" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] \
    || hata "RACAR_GH_REPO biçimi geçersiz: $GH_REPO (sahip/repo bekleniyor)"
tamam "SPA token'ı tanımlı (değer yazdırılmaz) · repo: $GH_REPO"

for arac in curl jq tar git; do
    command -v "$arac" >/dev/null 2>&1 || hata "'$arac' yok — sudo apt-get install -y $arac"
done

# Guard testi buraya kadar koşar: publish/systemd/ağ gerektirmeden tüm ölümcül kontroller bitti.
[[ "$TEST_KIPI" == "1" ]] && { echo "GUARDLAR_GECTI"; exit 0; }

if [[ "$TEST_KIPI" == "0" ]]; then
    # Sunucu .NET'i DERLER (dotnet publish) → runtime yetmez, SDK 10 şart.
    dotnet --list-sdks 2>/dev/null | grep -q '^10\.' \
        || hata ".NET SDK 10 yok (dotnet --list-sdks). ./deploy/kurulum.sh çalıştır ya da: sudo apt-get install -y dotnet-sdk-10.0"
fi

# ---------------------------------------------------------------- commit
# safe.directory: repo başka kullanıcıya aitse root'un git'i "dubious ownership" ile reddeder.
git_repo() { git -c safe.directory="$REPO_DIR" -C "$REPO_DIR" "$@"; }
SHA="$(git_repo rev-parse --verify HEAD 2>/dev/null || true)"
[[ "$SHA" =~ ^[0-9a-f]{40}$ ]] || hata "$REPO_DIR bir git checkout'u değil ya da HEAD okunamadı."
bilgi "Commit: $SHA"
if ! git_repo diff --quiet HEAD -- 2>/dev/null; then
    uyari "Çalışma ağacında commit'lenmemiş değişiklik var: .NET bunlarla derlenir, SPA ise $SHA'nın CI çıktısıdır."
fi

# ---------------------------------------------------------------- temizlik
GECICI=""
YENI=""
CEVRILDI=0
# Hata ile çıkışta: geçici indirmeler ve (symlink henüz çevrilmediyse) yarım release silinir →
# reddedilen yayın diskte "en yeni sürüm" gibi durup saklama sırasını bozmaz.
temizle() {
    local kod=$?
    if [[ -n "$GECICI" ]]; then rm -rf "$GECICI"; fi
    if [[ $kod -ne 0 && $CEVRILDI -eq 0 && -n "$YENI" && -d "$YENI" ]]; then
        rm -rf "$YENI"
        printf '  yarım release silindi: %s (current DEĞİŞMEDİ)\n' "$(basename "$YENI")" >&2
    fi
    return 0
}
trap temizle EXIT

# ---------------------------------------------------------------- SPA artifact
sha256_hesapla() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{print $1}'
    else
        shasum -a 256 "$1" | awk '{print $1}'
    fi
}

# GitHub isteği. Token curl'e STDIN'den config olarak verilir: komut satırında (ps) görünmez.
# curl yönlendirmede (asset → depolama sunucusu) Authorization başlığını başka host'a TAŞIMAZ.
# stdout: HTTP durum kodu (ağ hatasında 000). Gövde $2'ye.
gh_istek() {
    local kod
    kod="$(curl -sS -L --proto "$PROTO" --proto-redir "$PROTO" --connect-timeout 15 --max-time 300 \
        -H "Accept: $3" -H "X-GitHub-Api-Version: 2022-11-28" -H "User-Agent: racar-yayinla" \
        -o "$2" -w '%{http_code}' -K - "$1" <<<"header = \"Authorization: Bearer $GH_TOKEN\"")" || true
    printf '%s' "${kod:-000}"
}

ETIKET="spa-$SHA"
ARSIV="$ETIKET.tar.gz"

spa_indir() {
    local d="$1" kod url ad
    kod="$(gh_istek "$GH_API/repos/$GH_REPO/releases/tags/$ETIKET" "$d/release.json" "application/vnd.github+json")"
    case "$kod" in
        200) ;;
        404) hata "SPA artifact'ı YOK: '$ETIKET' release'i bulunamadı ($GH_REPO). Yayın reddedildi.
       - Bu commit main'e push edildi mi? Artifact yalnız main'deki commit'ler için üretilir.
       - CI bitti ve YEŞİL mi? Artifact işi tüm kapılar geçince koşar (GitHub → Actions → CI).
       - Token bu repoya erişebiliyor mu? (fine-grained: Repository access = bu repo, Contents = Read)" ;;
        401|403) hata "GitHub isteği reddedildi (HTTP $kod): RACAR_GH_TOKEN geçersiz, süresi dolmuş ya da bu repoya yetkisiz." ;;
        *) hata "GitHub API'ye ulaşılamadı (HTTP $kod). Ağ/DNS kontrol et." ;;
    esac
    for ad in "$ARSIV" "$ARSIV.sha256"; do
        url="$(jq -r --arg ad "$ad" '[.assets[]? | select(.name == $ad) | .url] | first // empty' "$d/release.json")"
        [[ -n "$url" ]] \
            || hata "'$ETIKET' release'inde '$ad' yok (CI yüklemesi yarım kalmış olabilir — Actions'ta işi yeniden koştur)."
        # Token YALNIZ API host'una gider; beklenmeyen bir adres gelirse istek yapılmaz.
        [[ "$url" == "$GH_API/"* ]] || hata "Beklenmeyen asset adresi ($url) — token yalnız $GH_API'ye gönderilir."
        kod="$(gh_istek "$url" "$d/$ad" "application/octet-stream")"
        [[ "$kod" == "200" ]] || hata "$ad indirilemedi (HTTP $kod)."
    done
}

spa_dogrula() {
    local d="$1" beklenen="" ad="" gercek liste kotu
    read -r beklenen ad < "$d/$ARSIV.sha256" || true
    ad="${ad#\*}"
    [[ "$beklenen" =~ ^[0-9a-f]{64}$ ]] || hata "$ARSIV.sha256 bozuk (64 haneli hex beklenirdi). Yayın reddedildi."
    [[ "$ad" == "$ARSIV" ]] \
        || hata "sha256 dosyası başka bir arşivi tarif ediyor ('$ad' ≠ '$ARSIV') — yanlış SHA, yayın reddedildi."
    gercek="$(sha256_hesapla "$d/$ARSIV")"
    [[ "$gercek" == "$beklenen" ]] \
        || hata "CHECKSUM UYUŞMUYOR ($ARSIV): beklenen $beklenen, inen $gercek. Yayın reddedildi."
    # İçerik kilidi: yalnız browser/, chunks.txt, SURUM; '..' yok.
    liste="$(tar -tzf "$d/$ARSIV")" || hata "$ARSIV açılamadı (bozuk arşiv)."
    kotu="$(grep -Ev '^(\./)?(browser(/.*)?|chunks\.txt|SURUM)$' <<<"$liste" || true)"
    [[ -z "$kotu" ]] || hata "$ARSIV beklenmeyen girişler içeriyor: $(head -n 3 <<<"$kotu" | tr '\n' ' ')"
    if grep -Eq '(^|/)\.\.(/|$)' <<<"$liste"; then hata "$ARSIV '..' içeren yol taşıyor — reddedildi."; fi
    tamam "sha256 doğrulandı: $gercek"
}

spa_ac() {
    local d="$1" hedef="$2" surum=""
    mkdir -p "$hedef"
    tar -xzf "$d/$ARSIV" -C "$hedef" --no-same-owner || hata "$ARSIV açılamadı."
    if [[ -f "$hedef/SURUM" ]]; then surum="$(tr -d '\r\n' < "$hedef/SURUM")"; fi
    [[ "$surum" == "$SHA" ]] \
        || hata "Arşiv başka bir commit'e ait (SURUM='$surum', beklenen $SHA) — yanlış SHA, yayın reddedildi."
    [[ -f "$hedef/browser/index.html" ]] || hata "Arşivde browser/index.html yok."
    [[ -f "$hedef/chunks.txt" ]] || hata "Arşivde chunks.txt yok."
    tamam "SPA açıldı: $(wc -l < "$hedef/chunks.txt" | tr -d ' ') hash'li dosya"
}

# Önceki release'in YALNIZ kendi chunks.txt'indeki dosyaları taşır (sınırlı: taşınanlar yeni listede yok).
spa_chunk_tasi() {
    local eski="$1/app" yeni="$2/app" ad n=0
    if [[ ! -f "$eski/chunks.txt" ]]; then
        uyari "Önceki sürümde SPA chunks.txt yok — taşınacak chunk yok."
        return 0
    fi
    while IFS= read -r ad || [[ -n "$ad" ]]; do
        ad="${ad%$'\r'}"
        [[ -n "$ad" ]] || continue
        if [[ ! "$ad" =~ ^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$ || "$ad" == *..* ]]; then
            uyari "Geçersiz chunk adı atlandı: $ad"
            continue
        fi
        [[ -f "$eski/browser/$ad" && ! -e "$yeni/browser/$ad" ]] || continue
        mkdir -p "$(dirname "$yeni/browser/$ad")"
        cp -n -p "$eski/browser/$ad" "$yeni/browser/$ad"
        n=$((n + 1))
    done < "$eski/chunks.txt"
    tamam "Önceki sürümden $n chunk taşındı (açık sekmeler eski dosyaları bulur)"
}

DAMGA="$(date +%Y%m%d-%H%M%S)"
[[ ! -e "$REL_DIR/$DAMGA" ]] || hata "$REL_DIR/$DAMGA zaten var — bir saniye bekleyip tekrar çalıştır."
ONCEKI=""
if [[ -L "$CURRENT" ]]; then ONCEKI="$(readlink -f "$CURRENT")"; fi

bilgi "SPA artifact'ı: $ETIKET"
mkdir -p "$REL_DIR"
GECICI="$(mktemp -d "${TMPDIR:-/tmp}/racar-spa.XXXXXX")"
spa_indir "$GECICI"
spa_dogrula "$GECICI"

YENI="$REL_DIR/$DAMGA"
bilgi "Yayın hazırlanıyor: $DAMGA"
mkdir -p "$YENI"
spa_ac "$GECICI" "$YENI/app"
if [[ -n "$ONCEKI" && -d "$ONCEKI" ]]; then
    spa_chunk_tasi "$ONCEKI" "$YENI"
fi

[[ "$TEST_KIPI" == "spa" ]] && { echo "SPA_HAZIR $YENI"; exit 0; }

# ---------------------------------------------------------------- publish
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

# SPA dosyaları (taşınan chunk'lar dahil) da bu chown'la racar'a geçer.
chown -R "$APP_USER:$APP_USER" "$YENI"

# ---------------------------------------------------------------- symlink çevir
ln -sfn "$YENI" "$CURRENT"
CEVRILDI=1
tamam "current → $DAMGA${ONCEKI:+ (önceki: $(basename "$ONCEKI"))}"

SERVISLER=(racar-web racar-publicsite)
[[ "$API_DAHIL" == "1" ]] && SERVISLER+=(racar-api)

bilgi "Servisler yeniden başlatılıyor"
# Web ÖNCE: açılışta migration'ı o çalıştırır (DbInitializer.MigrateAndSeedAsync).
systemctl restart racar-web
systemctl restart racar-publicsite
if [[ "$API_DAHIL" == "1" ]]; then systemctl restart racar-api; fi

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
# Yeni arayüz kabuğu: bu release'in app/browser'ı servis ediliyor mu (Spa:Dizin=../app/browser).
saglik "Yeni arayüz /app/" "http://127.0.0.1:5220/app/" || BASARILI=0

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
tamam "YAYIN TAMAM — $DAMGA ($SHA)"
echo "     Doğrulama:  ./deploy/dogrula.sh <domain>"
