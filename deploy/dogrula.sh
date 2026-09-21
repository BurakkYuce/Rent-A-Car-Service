#!/usr/bin/env bash
# RentACar — GO-LIVE DOĞRULAMA. deploy-checklist.md §9'daki güvenlik kontrollerini otomatik koşar.
#
#   ./deploy/dogrula.sh erp.senindomainin.com
#
# Her deploy'dan sonra çalıştır. Çıkış kodu: 0 = hepsi geçti, 1 = en az bir KRİTİK bulgu.
# Sessizce bozulan şeyleri arar — hepsi bir kez gerçekten yaşanmış ya da adversarial incelemede
# yakalanmış hatalardır, "olur da" listesi değildir.
set -uo pipefail

DOMAIN="${1:-}"
GECTI=0; KALDI=0; ATLANDI=0

ok()    { printf '\033[1;32m  ✓\033[0m %s\n' "$*"; GECTI=$((GECTI+1)); }
kotu()  { printf '\033[1;31m  ✗\033[0m %s\n' "$*"; KALDI=$((KALDI+1)); }
atla()  { printf '\033[1;33m  –\033[0m %s\n' "$*"; ATLANDI=$((ATLANDI+1)); }
baslik(){ printf '\n\033[1;34m%s\033[0m\n' "$*"; }

baslik "1. Servisler"
for s in racar-web racar-publicsite; do
    if systemctl is-active --quiet "$s"; then ok "$s çalışıyor"
    else kotu "$s ÇALIŞMIYOR — journalctl -u $s -n 30"; fi
done
systemctl is-active --quiet racar-api && ok "racar-api çalışıyor" || atla "racar-api kapalı (API kullanılmıyorsa normal)"

baslik "2. Sağlık uçları (iç ağ)"
curl -fsS -o /dev/null --max-time 5 http://127.0.0.1:5220/health/ready \
    && ok "ERP /health/ready" || kotu "ERP /health/ready başarısız (DB / migrator / DP key-ring)"
curl -fsS -o /dev/null --max-time 5 http://127.0.0.1:5230/health/live \
    && ok "Site /health/live" || kotu "Site /health/live başarısız"

baslik "3. Ağ sınırları"
# KRİTİK: 5230 dışarı açık olsaydı, Caddy atlanıp sahte Host header'ıyla tenant çözümlemesi ve
# özel-domain doğrulama flip'i tetiklenebilirdi (PR-5).
if ss -tlnH 2>/dev/null | awk '{print $4}' | grep -qE '^(0\.0\.0\.0|\[::\]|\*):(5220|5230|5240)$'; then
    kotu "Uygulama portu DIŞARI açık! ASPNETCORE_URLS 127.0.0.1 olmalı:
       $(ss -tlnH | awk '{print $4}' | grep -E ':(5220|5230|5240)$' | tr '\n' ' ')"
else
    ok "5220/5230/5240 yalnız 127.0.0.1"
fi
if ss -tlnH 2>/dev/null | awk '{print $4}' | grep -qE ':5432$' | grep -qv '127.0.0.1'; then
    kotu "PostgreSQL dışarı açık olabilir — kontrol et"
else
    ok "PostgreSQL dışarı kapalı"
fi

baslik "4. Veritabanı izolasyonu"
if sudo -u postgres psql -tAc "select rolbypassrls or rolsuper from pg_roles where rolname='racar_app'" 2>/dev/null | grep -qi '^t'; then
    kotu "racar_app RLS'i ATLAYABİLİYOR — tüm tenant izolasyonu çöker!"
else
    ok "racar_app NOBYPASSRLS"
fi
RLSSIZ=$(sudo -u postgres psql -d racar -tAc \
    "select count(*) from pg_tables t join pg_class c on c.relname=t.tablename
     where t.schemaname='public' and not c.relrowsecurity
       and exists (select 1 from information_schema.columns
                   where table_name=t.tablename and column_name='TenantId')" 2>/dev/null || echo "?")
if [[ "$RLSSIZ" == "0" ]]; then ok "TenantId taşıyan tüm tablolarda RLS açık"
elif [[ "$RLSSIZ" == "?" ]]; then atla "RLS kapsamı sorgulanamadı"
else kotu "$RLSSIZ tenant tablosunda RLS KAPALI"; fi

baslik "5. Anahtarlar ve yedek"
DP="$(grep -E '^RACAR_DP_KEYS=' /etc/racar/racar.env 2>/dev/null | cut -d= -f2-)"
if [[ -n "$DP" && -d "$DP" ]] && [[ $(find "$DP" -type f | wc -l) -gt 0 ]]; then
    ok "DataProtection key-ring dolu ($DP)"
    case "$DP" in /opt/racar/*) kotu "key-ring YAYIN DİZİNİ altında — bir sonraki deploy PII'yi yok eder!";; esac
else
    kotu "DataProtection key-ring boş/yok ($DP) — şifreli PII çözülemez hale gelebilir"
fi
grep -qE '^Pii__HmacKey=.{20,}' /etc/racar/racar.env 2>/dev/null \
    && ok "Pii__HmacKey dolu" || kotu "Pii__HmacKey eksik/kısa"
grep -q 'DEGISTIR' /etc/racar/racar.env 2>/dev/null \
    && kotu "racar.env içinde doldurulmamış DEGISTIR var" || ok "racar.env'de placeholder kalmamış"
if SON=$(ls -t /var/backups/racar/*.dump 2>/dev/null | head -1) && [[ -n "$SON" ]]; then
    YAS=$(( ( $(date +%s) - $(stat -c %Y "$SON") ) / 86400 ))
    [[ $YAS -le 1 ]] && ok "Son yedek $YAS gün önce" || kotu "Son yedek $YAS GÜN ÖNCE — timer çalışmıyor olabilir"
else
    kotu "Hiç yedek yok (/var/backups/racar) — timer: systemctl status racar-backup.timer"
fi

baslik "6. Dışarıdan (HTTPS) kontroller"
if [[ -z "$DOMAIN" ]]; then
    atla "Domain verilmedi — ./deploy/dogrula.sh <domain> ile çalıştır"
else
    BASLIK=$(curl -sI --max-time 10 "https://$DOMAIN/" 2>/dev/null || true)
    if [[ -z "$BASLIK" ]]; then
        kotu "https://$DOMAIN/ yanıt vermedi (DNS / Caddy / sertifika)"
    else
        ok "HTTPS yanıt veriyor"
        for h in content-security-policy x-content-type-options x-frame-options referrer-policy strict-transport-security; do
            grep -qi "^$h:" <<<"$BASLIK" && ok "başlık: $h" || kotu "başlık EKSİK: $h"
        done
        # EN KRİTİK: antiforgery ve HSTS ikisi de !IsDevelopment()'e kapılı. Env yanlışsa İKİSİ BİRDEN
        # sessizce kapanır — token'sız POST 400 dönmüyorsa ASPNETCORE_ENVIRONMENT yanlıştır.
        KOD=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 -X POST "https://$DOMAIN/kiralar/create" -d x=1 2>/dev/null || echo 000)
        [[ "$KOD" == "400" ]] && ok "CSRF koruması aktif (token'sız POST → 400)" \
            || kotu "Token'sız POST → $KOD (400 bekleniyordu). ASPNETCORE_ENVIRONMENT=Production mu?"
        # Yeni arayüz (F2.2): kabuk anonim 200, veri uçları oturumsuz 401 (JSON; /login'e 302 DEĞİL).
        # -w çıktısı hata olsa da basılır; `|| true` "000" üstüne ikinci bir değer eklemesin diye.
        KOD=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "https://$DOMAIN/app/" 2>/dev/null || true)
        if [[ "$KOD" == "200" ]]; then ok "Yeni arayüz /app/ → 200"
        else kotu "/app/ → ${KOD:-000} (200 bekleniyordu). releases/<ts>/app/browser var mı? (yayinla.sh SPA adımı)"; fi
        KOD=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "https://$DOMAIN/api/ui/v1/oturum/ben" 2>/dev/null || true)
        if [[ "$KOD" == "401" ]]; then ok "/api/ui/v1/oturum/ben oturumsuz → 401"
        else kotu "/api/ui/v1/oturum/ben oturumsuz → ${KOD:-000} (401 bekleniyordu)"; fi
    fi
fi

printf '\n\033[1m%d geçti · %d kaldı · %d atlandı\033[0m\n' "$GECTI" "$KALDI" "$ATLANDI"
[[ "$KALDI" -eq 0 ]] || { printf '\033[1;31mKRİTİK bulgu var — go-live ÖNCESİ düzelt.\033[0m\n'; exit 1; }
printf '\033[1;32mHepsi temiz.\033[0m\n'
