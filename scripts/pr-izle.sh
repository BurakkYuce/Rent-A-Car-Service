#!/usr/bin/env bash
# PR'ın CI kontrollerini YALNIZ İZLER; asla merge etmez.
#
# Kullanım: scripts/pr-izle.sh <pr-no> [en-az-kontrol=6] [beklenen-head-sha-öneki]
#
# Çıkış: tüm kontroller sonuçlanınca (hiçbiri pending değil) kontrol listesini, mergeable durumunu ve
# head SHA'yı basar, 0 ile çıkar. Head beklenenden farklıysa "HEAD DEĞİŞTİ" basar, 0 ile çıkar.
# 45 dk içinde sonuçlanmazsa 1 ile çıkar.
#
# Neden bu kadar temkinli (bkz. docs/roadmap/DEVIR.md "Kalıcı dersler"):
# - `gh pr checks --watch` kontroller hâlâ pending iken erken çıkabiliyor.
# - Ağ dalgalanınca `gh pr checks` BOŞ çıktı döndürüyor; boş çıktı "bitti" DEĞİLDİR.
# - İzleme ile merge aynı komutta zincirlenirse kırmızı kontrol merge'den sonra fark edilir.
# Merge, bu betiğin çıktısı OKUNDUKTAN sonra ayrı komutla yapılır:
#   gh pr merge <no> --merge --match-head-commit <sha>
set -u
pr="${1:?pr no gerekli}"
en_az="${2:-6}"
beklenen="${3:-}"

gh auth switch --user BurakkYuce >/dev/null 2>&1 || true

for _ in $(seq 1 90); do
  out=$(gh pr checks "$pr" --json name,state 2>/dev/null || true)
  head=$(gh pr view "$pr" --json headRefOid -q .headRefOid 2>/dev/null || true)
  if [ -n "$beklenen" ] && [ -n "$head" ] && [ "${head#"$beklenen"}" = "$head" ]; then
    echo "HEAD DEĞİŞTİ: ${head} (beklenen önek: ${beklenen})"
    exit 0
  fi
  n=$(printf '%s' "$out" | jq 'length' 2>/dev/null || echo 0)
  bekleyen=$(printf '%s' "$out" | jq '[.[]|select(.state=="PENDING" or .state=="IN_PROGRESS" or .state=="QUEUED")]|length' 2>/dev/null || echo 1)
  if [ -n "$out" ] && [ "${n:-0}" -ge "$en_az" ] && [ "${bekleyen:-1}" -eq 0 ]; then
    printf '%s' "$out" | jq -r '.[]|"\(.name)\t\(.state)"'
    gh pr view "$pr" --json mergeable,headRefOid -q '"mergeable=\(.mergeable) head=\(.headRefOid)"'
    exit 0
  fi
  sleep 30
done
echo "ZAMAN AŞIMI (45 dk); son durum:"
printf '%s\n' "$out"
exit 1
