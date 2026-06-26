# TürevRent ERP (yeniden inşa)

ASP.NET Core + EF Core + PostgreSQL ile sıfırdan inşa edilen, **çok-kiracılı (multi-tenant)**
araç kiralama / filo yönetim ERP'si. Para mantığı (fiyat/fatura/tahsilat) henüz YOK — şema
iskeleti + tenant izolasyonu kanıtı + operasyonel çekirdek dikey dilimleri.

İçerik (Faz 1 — operasyonel MVP):
- **PR #1** — multi-tenant iskelet (RLS+FORCE, audit, Money/ledger şeması, 2-aşamalı auth, CI) + Araç CRUD.
- **PR #2** — Cari (Müşteri) CRUD: Bireysel/Kurumsal/Servis, TC Kimlik checksum, tenant-içi TC/Vergi No benzersizliği.
- **PR #3** — Müsaitlik + Rezervasyon→Kira durum makinesi; DB-seviyesi double-booking koruması
  (GiST exclusion constraint) + eşzamanlılık testi; tenant-başına boşluksuz no (TenantSequence).
- **PR #4** — Teslim (çıkış KM/yakıt) → Dönüş (fazla km/eksik yakıt/uzatma) → GenelToplam.
- **PR #5** — Nakit Tahsilat + çift-taraflı cari ledger + ters kayıt + DB-seviyesi değişmezlik
  (immutability trigger); çok-dövizli bakiye + ekstre.
- **PR #6** — Faz 1 uçtan-uca kabul testi + entegrasyon adapter port'ları (e-Fatura/POS/KABIS/HGS/
  SMS/WhatsApp/Calendar — v1 stub).

**Fiyat motoru ERTELENDİ:** parite/golden doğrulaması canlı TürevRent'e bağlı; o host bu ortamda
egress politikasıyla erişilemez (403). PR #3+ fiyatı manuel günlük ücretle hesaplar (placeholder).

## Mimari

Clean Architecture, .NET 10 (LTS):

| Proje | Sorumluluk |
|------|------------|
| `RentACar.Domain` | Entity'ler, value object'ler (`Money`), arayüzler (`ITenantOwned`, `ITenantContext`, `ICurrentUser`, `IAuditable`). Referanssız. |
| `RentACar.Application` | İş mantığı (`VehicleService`), DTO, doğrulama, repo arayüzleri. |
| `RentACar.Infrastructure` | EF Core `AppDbContext`, interceptor'lar, repository, migration + RLS. |
| `RentACar.Web` | Blazor (static SSR) + 2 aşamalı cookie login + DI kompozisyon. |
| `RentACar.IntegrationTests` | Gerçek PostgreSQL'e karşı izolasyon / CRUD / audit testleri. |

### Tenant izolasyonu (iki katman)
1. **EF Core global query filter** — her sorgu otomatik tenant'a daraltılır (ergonomi/perf).
2. **Postgres Row-Level Security (RLS)** — asıl güvenlik sınırı. Uygulama **kısıtlı `racar_app`
   rolüyle** bağlanır (NOSUPERUSER, NOBYPASSRLS). Her bağlantıda `app.tenant_id` GUC'u set edilir;
   policy'ler satırları bu GUC'a göre filtreler. Tenant yoksa → **default-deny** (0 satır).
   `FORCE ROW LEVEL SECURITY` ile tablo sahibi bile RLS'e tabidir. Migration/DDL ayrı `racar_owner`
   rolüyle yapılır.

### Para & denetim
`Money` (tutar + döviz + kur), `AccountLedgerEntry` (çift-taraflı defter — PR #1'de iskelet),
`AuditLog` (kim/ne zaman/eski-yeni; `SaveChanges` interceptor'ı ile otomatik). Hepsi tenant-owned + RLS.

## Çalıştırma

Önkoşullar: .NET 10 SDK, PostgreSQL 16 (yerel cluster **veya** docker). `scripts/setup.sh` bunları
hazırlar (idempotent).

```bash
# 1) Ortam (dotnet kur + restore + postgres başlat)
bash scripts/setup.sh

# 2) Uygulama (migration + seed otomatik; firma=yucerent/demo, kullanıcı=umit, şifre=umit1376)
dotnet run --project src/RentACar.Web
```

Bağlantılar `src/RentACar.Web/appsettings.json` içinde: `Default` = racar_app (runtime, RLS),
`Migrator` = racar_owner (DDL/seed).

## Test

Testler gerçek PostgreSQL ister (RLS rol/policy doğrulaması için). Admin bağlantısı
`RACAR_TEST_PG_ADMIN` env'inden ya da yerel varsayılandan (`postgres/postgres`) gelir. Fixture,
çalıştırma başına benzersiz bir DB + rolleri oluşturup migration'ı uygular.

```bash
export RACAR_TEST_PG_ADMIN="Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres;Database=postgres"
dotnet test
```

> Not: Bu ortamda Docker Hub blob host'u egress politikasıyla engelli olduğundan Testcontainers
> yerine ortam-sağlanan PostgreSQL kullanılır. CI'da `services: postgres` container'ı aynı env'i sağlar
> (bkz. `.github/workflows/ci.yml`).

## Migration

```bash
export RACAR_MIGRATOR_CONN="Host=127.0.0.1;Port=5432;Database=racar;Username=racar_owner;Password=racar_owner_pw"
dotnet ef migrations add <Ad> --project src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure
dotnet ef database update --project src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure
```

## .NET sürümünü değiştirme (TFM swap)
Birlikte güncellenen yerler: `Directory.Build.props` (`<TargetFramework>`),
`Directory.Packages.props` (EF Core/Npgsql major), `scripts/setup.sh` (apt paketi),
`.github/workflows/ci.yml` (setup-dotnet).

## PR #1 kapsam dışı (sonraki PR'lar)
Fiyat motoru, rezervasyon/kira, fatura, entegrasyonlar (e-Fatura/POS/KABIS/HGS/SMS/WhatsApp),
tam ABAC, Araç'ın 80-alanlık tam formu, antiforgery (mutation uçlarında şu an devre dışı), interaktif
Blazor formları, public API.
