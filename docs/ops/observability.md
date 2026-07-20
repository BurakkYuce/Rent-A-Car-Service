# Observability (gözlemlenebilirlik)

RentACar'ın telemetri kurulumu: **yapılandırılmış log + metrik + trace**, tenant/kullanıcı korelasyonlu,
tek kutuda self-hosted backend'lerle. Uygulama telemetriyi **OTLP ile push eder** (yeni public yüzey yok);
yanında OTel-Collector + Prometheus + Loki + Tempo + Grafana toplar/gösterir/alarm verir.

## Mimari
```
  Uygulama (Web + Api)                       ops/observability/ (docker-compose)
  ├─ Serilog log (JSON, tenant/user/trace)  ─┐
  ├─ OTel metrik (RED + iş sayaçları)        ─┤ OTLP :4317 → OTel Collector ─┬─ metrik → Prometheus
  └─ OTel trace (ASP.NET/Http/Npgsql)        ─┘                              ├─ log    → Loki
                                                                            └─ trace  → Tempo
                                                     Grafana ← Prometheus/Loki/Tempo (+ Alerting → /internal/alert)
```

## Uygulama tarafı (kod — her ortamda aktif; exporter config-gated)
- **Log zenginleştirme:** her istek-log satırı `tenant_id` + `user` + `request_id` taşır (çok-kiracılı filtre).
  Dosya sink JSON (`CompactJsonFormatter`). `RequestEnrichment` (Web) / `ApiRequestEnrichment` (Api).
- **Health-split:** `/health/live` (bağımlılık yok) + `/health/ready` (DB + Web'de Migrator + DataProtection
  keyring). `/health` → readiness alias. Yanıt: `{"status":"healthy|unhealthy"}`.
- **Metrik/trace:** OTel auto (ASP.NET Core RED, HttpClient, Npgsql, Runtime) + Meter `RentACar` iş sayaçları
  (`racar_login_total`, `racar_tahsilat_total`, `racar_ledger_idempotent_reject_total`,
  `racar_ratelimit_reject_total`, `racar_job_fail_total`). **KARDİNALİTE:** tenant/kullanıcı yalnız log/trace,
  metrik etiketi DEĞİL.
- **Exporter config-gated:** `OTEL_EXPORTER_OTLP_ENDPOINT` set ise OTLP metrik+trace+log akar; UNSET ise
  hiç exporter kurulmaz (dev/test/CI etkilenmez). Prod'da: `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317`.
- **Alarm anahtarı:** `Observability:AlertToken` (env `Observability__AlertToken`) — Grafana webhook'unun
  `.env` `ALERT_TOKEN`'ıyla AYNI olmalı.

## Backend yığınını çalıştırma
```bash
cd ops/observability
cp .env.example .env    # GRAFANA_ADMIN_PASSWORD + ALERT_TOKEN doldur
docker compose -f docker-compose.observability.yml up -d
```
Sonra uygulamayı `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317` ve `Observability__AlertToken=<.env ALERT_TOKEN>`
ile başlat. Grafana: http://127.0.0.1:3000 (admin / GRAFANA_ADMIN_PASSWORD).

## Güvenlik (ZORUNLU)
- **Yalnız Grafana dışarı açılır** — Caddy ile TLS + auth arkasına al (127.0.0.1:3000 → reverse-proxy).
  Prometheus/Loki/Tempo host portu YOK (yalnız iç docker ağı); Collector OTLP yalnız `127.0.0.1:4317`.
- Grafana anonim erişim KAPALI, kayıt KAPALI, admin şifresi env'den (default admin/admin KULLANMA).
- `.env` secret taşır → commit ETME (.gitignore'da). Alarm webhook Bearer-anahtarlı; anahtar yoksa uç KAPALI.

## Retention
Prometheus 15 gün · Loki 14 gün (dosya-log ile hizalı) · Tempo 7 gün. Tek-kutu için kaynak limitleri compose'da.

## Dashboard & Alarm
- **Dashboard:** "RentACar — Genel Bakış" (RED: istek hızı/5xx/p95 + iş-KPI: login/tahsilat/idempotent-red/
  rate-limit/job-fail + son hata/uyarı logları). Grafana'da RentACar klasöründe otomatik provision.
- **Alarm:** örnek kurallar (`alerting/rules.yaml`: yüksek 5xx, job hatası) → `racar-webhook` contact-point →
  uygulama `/internal/alert` (Bearer). Uç alarmı Warning loglar (→ Loki/dosya) + best-effort WhatsApp forward
  (`Twilio:AlertPhone` + `Twilio:Templates:ops_alert` config'liyse). **Eşikleri Grafana UI'da doğrula/ayarla.**
  E-posta/Slack istiyorsan Grafana contact-point'lerine kendi kanalını ekle.

## Trace ↔ log korelasyonu
Loki logundaki `trace_id` → Tempo trace'ine tıkla-geç; Tempo span'inden `service.name` ile Loki loguna dön
(datasources.yaml'da provision). Log+trace `request_id`/`trace_id` ile aynı isteği gösterir.
