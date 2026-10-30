# Harita — "X nerede?" sorusunun tek giriş noktası

Bir soru ya da iş geldiğinde önce burada konuya uyan satırı bul, yalnız o dosyaları aç.
- Satır yoksa ya da dosya tarifle uyuşmuyorsa harita bayattır: söyle, sonra grafiğe sor
  (`graphify query "<soru>"`; kod grafiği `graphify-out/`, her commit'te kendiliğinden tazelenir), en son grep.
- Satır eklemek de düzeltmek de normal iştir; haritayı değiştiren PR'a dahil et.

Kurallar ve kararlar burada TEKRARLANMAZ. Kurallar için `CLAUDE.md`, mimarinin anlatımı için `docs/MIMARI.md`,
kalıcı kararlar için `docs/KARARLAR.md`, son durum için `docs/roadmap/DEVIR.md` §1.

## Katmanlar (yön içe doğru)
| Katman | Yol | Ne var |
|---|---|---|
| Domain | `src/RentACar.Domain/` | Entity'ler (İngilizce sınıf, Türkçe alan), enum'lar, `Money`, `DocumentNo` |
| Application | `src/RentACar.Application/<Modül>/` | Servis + repository arayüzü + doğrulama + yetki guard'ı |
| Infrastructure | `src/RentACar.Infrastructure/` | EF Core, repository'ler, migration'lar, job üreticileri, dış adaptörler |
| Web (ERP) | `src/RentACar.Web/` | `/api/ui/v1` uçları (`Api/<Modül>/`), job'lar (`Jobs/`), SPA barındırma (`Spa/`) |
| Arayüz | `src/RentACar.Frontend/` | Angular 21 SPA (`/app`); kurallar `AGENTS.md`, ekranlar `src/app/features/` |
| Firma siteleri | `src/RentACar.PublicSite/` | Kiracı başına halka açık site (Blazor SSR), temalar `Components/Themes/` |
| Dış API | `src/RentACar.Api/` | JWT'li REST `/api/v1` |
| Testler | `tests/RentACar.IntegrationTests/` | Gerçek PostgreSQL; dosya adı = modül/uç adı |

## Konu → başla buradan
| Konu / soru | Dosyalar |
|---|---|
| Kiracı yalıtımı, RLS, `app.tenant_id` | `Infrastructure/Persistence/Interceptors/TenantConnectionInterceptor.cs`, `AppDbContext.cs` (merkezî filtre), migration'lardaki RLS blokları |
| Yetki matrisi, rol → izin | `Application/Authorization/RolePermissions.cs`, `PermissionGuard.cs`, `AdminRoleGuard.cs` |
| Şube kapsamı (operatör yalnız kendi şubesi) | `Application/Authorization/BranchScope.cs` |
| Uç izinleri, Admin rol kapısı | `Web/Identity/AuthExtensions.cs`; çit testleri `UcIzinKapsamaTests`, `DeletePermissionFenceTests` |
| Oturum, giriş, güvenlik damgası | `Web/Identity/`, `Web/Api/Session/` |
| Defter (çift taraflı), dengeli küme | `Infrastructure/Persistence/Repositories/LedgerPoster.cs`, `LedgerSet.cs`, `Domain/Entities/AccountLedgerEntry.cs` |
| Fatura, iade, KDV | `Application/Finance/InvoiceService.cs`, `VatMath.cs`, `Infrastructure/.../InvoiceRepository.cs` |
| e-Fatura / e-Arşiv (kuyruk, UBL, durum) | `Application/EInvoicing/`, `Infrastructure/Persistence/EDocumentDispatcher.cs`, `EDocumentQueue.cs`; açık işler PR #394 yorumu |
| Kasa/banka, tahsilat, virman | `Application/Finance/CashService.cs`, `DepositService.cs` |
| Belge numarası, boşluksuz sıra | `Infrastructure/Persistence/DocumentNoGenerator.cs`, `SequenceAllocator.cs`, `Domain/Common/DocumentNo*.cs` |
| Kira hesabı, dönüş, uzatma | `Application/Bookings/RentalService.cs`, `RentalCalculationService.cs`, `ReturnMath.cs`, `BookingMath.cs` |
| Rezervasyon, teklif, takvim | `Application/Bookings/ReservationService.cs`, `QuotationService.cs`, `CalendarService.cs` |
| Fiyat motoru, kampanya, doluluk çarpanı | `Application/Pricing/PricingService.cs`, `Application/OccupancyPricing/`, `Application/RentalRules/` |
| Kur (TCMB, sabit kur) | `Application/ExchangeRates/ExchangeRateResolver.cs`, `Web/Jobs/TcmbExchangeRateJob.cs` |
| Dönemsel faturalama | `Application/InvoicePeriods/`, `Infrastructure/Persistence/PeriodInvoiceGenerator.cs` |
| HGS içe aktarma ve yansıtma | `Application/Hgs/`; canlıda önce `docs/ops/blazor-saat-kaymasi.md` §5 |
| KABİS kuyruğu | `Application/Kabis/`, `Infrastructure/Persistence/KabisNotificationGenerator.cs`, `Web/Jobs/KabisRetryJob.cs` |
| Dış entegrasyon portları (SMS, POS, e-posta, stub'lar) | `Application/Integrations/`, `Infrastructure/Integrations/StubAdapters.cs` (dürüst stub kuralı) |
| Müşteri bildirimleri, şablonlar | `Application/Notifications/`, `Infrastructure/Persistence/CustomerNotificationGenerator.cs` |
| Arka plan işleri | `Web/Jobs/` + `Infrastructure/Persistence/*Generator.cs` (üretici = `RunAsync(db, tenant, now)`) |
| Müşteri PII (TC şifreli, blind-index) | `Infrastructure/.../CustomerRepository.cs`, `Application/Common/IPiiHasher.cs`, `ISecretProtector.cs` |
| Raporlar | `Application/Reporting/`, `Web/Api/Reports/`, `Infrastructure/Persistence/SharedQueries.cs` |
| Menü (rol → görünen ekran) | `Web/Api/Menu/MenuRegistry.cs` |
| Bir ekranın SPA kodu | `Frontend/src/app/features/<modül>/`, rota `pages.ts`, çeviri `src/i18n/bloklar/<blok>.json` |
| SPA çekirdeği (API istemcisi, form, oturum) | `Frontend/src/app/core/`, `src/app/shared/` |
| Eski Blazor adresi nereye gidiyor | `Web/Spa/Cutover.cs`, `SpaHosting.cs` |
| Firma sitesi teması, paleti, önizleme | `PublicSite/Components/Themes/README.md` (sözleşme), `PublicSite/SitePaletteCss.cs`, `SitePreview.cs` |
| Firma sitesi alan adı, sertifika | `PublicSite` doğrulama ucu `/dogrulama/ask`, `deploy/Caddyfile.ornek` |
| Platform (süper admin) konsolu | `Web/Api/Platform/`, `Frontend/src/app/features/platform/` |
| Migration ekleme | `CLAUDE.md` §5 reçetesi |
| Yayın, sunucu, yedek | `deploy/` (`yayinla.sh`, `kurulum.sh`, `systemd/`), `docs/ops/f2-2-sunucu-adimlari.md`, `docs/ops/yedekleme.md` |
| Gözlemlenebilirlik (log, metrik, sağlık) | `docs/ops/observability.md`, `Web/Jobs/OpsWatchdogJob.cs` |
| Para PR'ı inceleme şablonu | `docs/roadmap/DEVIR.md` §4 |

## Repo dışı
| Konu | Yer |
|---|---|
| RentPro satış (pazarlama) sitesi | `github.com/BurakkYuce/rentpro-site` (yerelde `~/Desktop/demo-apps/rentpro-site`) |
| Demo ortamı (3 örnek firma) | `~/Desktop/demo-apps/demo-ortam/` (`baslat.sh` / `durdur.sh`) |
| Araştırma raporları, koordinasyon notları | `~/Desktop/demo-apps/koordinasyon/` |
