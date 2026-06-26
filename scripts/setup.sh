#!/usr/bin/env bash
# Ephemeral cloud container'da oturum başına ortam hazırlığı (idempotent).
# Sıcak çalıştırmalarda tamamlanmış adımları atlar.
# Hiçbir adım oturumu düşürmez: opsiyonel adımlar başarısız olsa da exit 0.
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

# 2) Bağımlılıklar + ef aracı.
if command -v dotnet >/dev/null 2>&1; then
  dotnet tool restore >/dev/null 2>&1 || log "UYARI: dotnet tool restore başarısız"
  dotnet restore RentACar.slnx >/dev/null 2>&1 || log "UYARI: dotnet restore başarısız"
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
