#!/usr/bin/env bash
# Ephemeral cloud container'da oturum başına ortam hazırlığı (idempotent).
# Sıcak çalıştırmalarda tamamlanmış adımları atlar (restore CACHE'li — bkz. adım 2).
# Hiçbir adım oturumu düşürmez: opsiyonel adımlar başarısız olsa da exit 0.
#
# ÇALIŞTIRMA: SessionStart hook'u bunu ASYNC başlatır (.claude/settings.json:
#   `nohup ... &`) → oturum açılışı BEKLEMEZ. Çıktı: /tmp/racar-setup.log (tail ile izle).
#   Sıcak makinede restore cache-isabetiyle ~1-2sn'de biter; taze/soğuk ortamda arka planda
#   tam hazırlık sürer (ilk `dotnet build` zaten talep-anı restore yapar → boşluk kapanır).
#
# PostgreSQL sağlama stratejisi (ortama göre):
#   - Bu kapalı-ağ ortamında Docker Hub blob host'u (cloudfront.docker.com) egress
#     politikasıyla engelli → image pull yapılamaz. Bu yüzden öncelik apt ile kurulan
#     yerel postgres cluster'ıdır (pg_ctlcluster). Docker Hub erişilebilen ortamlarda
#     docker-compose alternatifi de bırakılmıştır.
#   - Entegrasyon testleri admin bağlantısını RACAR_TEST_PG_ADMIN env'inden okur;
#     yoksa Host=127.0.0.1;Username=postgres;Password=postgres varsayımına düşer.
set -uo pipefail
cd "$(dirname "$0")/.." || exit 0

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

log() { printf '[setup] %s\n' "$*"; }

# 1) .NET 10 SDK (TFM ile birlikte değişen apt paketi).
if ! command -v dotnet >/dev/null 2>&1; then
  log "dotnet bulunamadı, kuruluyor (dotnet-sdk-10.0)..."
  apt-get update -y >/dev/null 2>&1 || true
  apt-get install -y dotnet-sdk-10.0 >/dev/null 2>&1 || log "UYARI: dotnet kurulamadı"
else
  log "dotnet mevcut: $(dotnet --version 2>/dev/null)"
fi

# 2) Bağımlılıklar + ef aracı — CACHE'li.
# Asıl yavaşlık burasıydı: NuGet global paket cache'i olsa da `dotnet restore` graph çözümü
# sıcak makinede bile ~20sn sürüyor ve HER oturumda koşuluyordu. Artık manifest'lerin
# (Directory.Packages.props + RentACar.slnx + dotnet-tools.json + tüm *.csproj) içerik hash'i
# değişmediyse restore ATLANIR. İşaret: .git/ altında (her klon için ayrı, git izlemez;
# taze klonda işaret yok → tam restore koşar). cksum = POSIX, hem macOS hem Linux'ta var.
if command -v dotnet >/dev/null 2>&1; then
  marker=".git/racar-restore.marker"
  want=$( { cat Directory.Packages.props RentACar.slnx .config/dotnet-tools.json 2>/dev/null
            find . -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' \
                 -not -path '*/.git/*' -not -path '*/.claude/*' -print0 2>/dev/null \
              | sort -z | xargs -0 cat 2>/dev/null
          } | cksum 2>/dev/null | tr ' ' '_' )
  if [ -n "$want" ] && [ -f "$marker" ] && [ "$(cat "$marker" 2>/dev/null)" = "$want" ]; then
    log "restore atlandı (manifest değişmedi — cache isabet)"
  else
    dotnet tool restore >/dev/null 2>&1 || log "UYARI: dotnet tool restore başarısız"
    if dotnet restore RentACar.slnx >/dev/null 2>&1; then
      [ -n "$want" ] && printf '%s\n' "$want" > "$marker" 2>/dev/null || true
      log "restore tamam (cache güncellendi)"
    else
      log "UYARI: dotnet restore başarısız"
    fi
  fi
fi

# 3) PostgreSQL (testler + manuel smoke için).
if command -v pg_ctlcluster >/dev/null 2>&1; then
  # apt postgres (bu ortamın tercihi)
  if ! pg_lsclusters -h 2>/dev/null | grep -q online; then
    log "yerel postgres cluster başlatılıyor..."
    pg_ctlcluster 16 main start >/dev/null 2>&1 || log "UYARI: cluster başlatılamadı"
  else
    log "yerel postgres zaten çalışıyor"
  fi
  # admin (postgres) rolüne TCP parola erişimi sağla (test fixture & smoke için)
  su - postgres -c "psql -p 5432 -tAc \"ALTER USER postgres PASSWORD 'postgres';\"" >/dev/null 2>&1 \
    || log "UYARI: postgres parolası ayarlanamadı"
elif command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  log "postgres başlatılıyor (docker compose)..."
  docker compose up -d db >/dev/null 2>&1 || log "UYARI: docker compose up başarısız (image pull engelli olabilir)"
else
  log "postgres sağlanamadı → testler için RACAR_TEST_PG_ADMIN ayarlayın."
fi

log "tamam."
exit 0
