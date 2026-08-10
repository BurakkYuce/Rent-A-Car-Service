# RentPro — Teknik Derinlik

> `docs/MIMARI.md` sistemi **ne yaptığını** anlatır. Bu belge **nasıl yaptığını** anlatır:
> veri erişimi, önbellek, gözlemlenebilirlik, arka plan işleri, eşzamanlılık, güvenlik, dağıtım.
> Hedef okuyucu: kodu görmeden altyapıyı değerlendirmek isteyen bir mühendis.
>
> Buradaki her iddia kodda karşılığı olan bir şeye işaret eder; dosya yolları verilmiştir.

---

## 1. Veri erişimi

### Context ömrü: kısa, factory üzerinden

Blazor'ın klasik tuzağı, `DbContext`'i circuit boyunca scoped tutmaktır — uzun ömürlü context,
şişen change-tracker ve kiracı bilgisinin bayatlaması demektir. Burada tüm repository'ler
`IDbContextFactory<AppDbContext>` alır ve **her işlemde yeni context** açar.

Sonuç: `AppDbContext` kurucusunda kiracıyı bir kez yakalayabiliyor (`TenantId`), çünkü context'in
ömrü tek bir işlem kadar. Global query filter ve RLS bu sabit değere dayanıyor.

Okuma yollarında **`AsNoTracking()` varsayılan**. Değişiklik izleme yalnız yazma yollarında.

### Dört SaveChanges/bağlantı interceptor'ı

`src/RentACar.Infrastructure/Persistence/Interceptors/`

| Interceptor | Ne yapar |
|---|---|
| `TenantConnectionInterceptor` | Bağlantı açılırken `set_config('app.tenant_id', …)` — RLS politikasının okuduğu değer. |
| `AuditSaveChangesInterceptor` | Eklenen tenant-owned varlığa `TenantId` **damgalar** (uygulama elle set etmez) + `IAuditable` varlıkların create/update/delete'ini eski-yeni değerleriyle `AuditLog`'a yazar. Denetim satırları **aynı transaction'da** — atomik. |
| `OfficeBranchInterceptor` | Rezervasyon/kira belgelerinde çıkış ofisinden şube FK'sini türetir (`CikisOfisi → Location → SubeId`). |
| `BranchFkInterceptor` | Şube metin alanı ile FK'nin tutarlı kalmasını sağlar. |

Denetim kaydının en kritik detayı: `AuditLog` kendisi `IAuditable` **değildir** — yoksa sonsuz
özyineleme olurdu.

### Paylaşılan sorgular tek yerde

`OrtakSorgular` (`Infrastructure/Persistence/OrtakSorgular.cs`) birden çok tüketicisi olan
sorguların tek doğruluk kaynağı. Gerekçesi bir denetim bulgusundan geliyor: aynı vade/tahsilat
tanımı iki yerde bağımsız yazılmıştı ve parite yalnızca satır numaralı yorumlarla korunuyordu —
tanım değişince pano ile bildirim özeti **sessizce ayrışıyordu**.

Bugün manuel fatura yolu ile gece işi, aynı `FarkStateAsync` sorgusundan geçiyor.

### Sayfalama

`PagedResult<T>(Items, Total, Page, PageSize)` — liste ekranları ve REST API aynı tipi kullanır.

### Boşluksuz sıra numarası

`SequenceAllocator.NextAsync(db, tenant, "InvoiceNo")` → `FT-000042`. Kiracı başına, **kaydın
kendisiyle aynı transaction'da**. `identity`/`sequence` kullanılmamasının sebebi: Postgres sequence'i
rollback'te geri sarmaz, mali belgede numara atlaması denetimde soru işaretidir.

---

## 2. Önbellek

### `ITenantCache` — kiracı kapsamlı bellek önbelleği

`Application/Common/ITenantCache.cs` · `Infrastructure/Caching/TenantCache.cs`

```csharp
private string Key(string key) => $"tc:{tenant.TenantId ?? Guid.Empty}:{key}";
private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
```

`IMemoryCache` üzerine ince bir sarmalayıcı. **Anahtarın kiracı önekiyle üretilmesi kritik** —
paylaşımlı bir `IMemoryCache`'te önek olmasa bir kiracının araç grubu listesi diğerine servis
edilirdi. Bu, RLS'in koruyamayacağı bir sızıntı sınıfıdır (veri veritabanından değil bellekten gelir).

| | |
|---|---|
| **Ne önbelleklenir** | Master/referans tanımları: araç grubu, marka, döviz, ödeme tipi, müşteri grubu, ek hizmet, rezervasyon kaynağı, departman, iptal nedeni… (~12 servis) |
| **Ne önbelleklenmez** | **Müşteri** — PII taşıyor, bellekte çözülmüş TC/ehliyet tutmak istemiyoruz. İşlem verisi (kira, fatura, defter) da önbelleklenmez. |
| **Geçersizleştirme** | Yazma yolunda açıkça `Invalidate(key)`. TTL'e güvenilmez — kullanıcı bir tanımı düzenleyip listede göremezse ürün bozuk sayılır. |
| **Kazanç** | Master listelere dokunan ekranlarda ~9x |

TTL 10 dakika, anahtar bazında geçersiz kılınabiliyor.

### Diğer önbellekler

| Yer | Ne için |
|---|---|
| `Infrastructure/Persistence/TenantStatusCache.cs` | Kiracının platform tarafındaki durumu (erişim açık/kapalı + modül bayrakları). Web middleware'i ve API'nin JWT doğrulama kancası **aynı** önbelleği kullanır. TTL 60 sn; aynı süreçte platform konsolundan kapatma **anında** etki eder, Web↔API arası en fazla 60 sn bayat kalır. Modül bayrağı bilinçli olarak ayrı bir anahtara konmadı — ikinci bir sorgu ve ikinci bir "geçersizleştirmeyi unutma" riski demekti. |
| `PublicSite/CachedPublicTenantResolver.cs` | Halka açık sitede `host → tenant` çözümü. Her anonim ziyaretçi isteğinde domain tablosuna gitmek anlamsız. |
| `PublicSite/DomainAskCache.cs` | Doğrulanmamış/olmayan domain sorgularının negatif önbelleği — rastgele host ile gelen trafiğin veritabanını dövmesini engeller. |

Dağıtık bir önbellek (Redis) **yok** ve bilinçli olarak yok: tek makinelik dağıtım hedefi için
gereksiz bir operasyonel yük. Yatay ölçeklemeye geçildiğinde `ITenantCache` arayüzü sabit kalır,
gerçeklemesi değişir — çağıran kod etkilenmez.

---

## 3. Gözlemlenebilirlik

Tam belge: `docs/ops/observability.md`. Özet:

```
  Uygulama (Web + Api)                        ops/observability/ (docker compose)
  ├─ Serilog log (JSON, tenant/user/trace) ─┐
  ├─ OTel metrik (RED + iş sayaçları)      ─┤ OTLP :4317 → Collector ─┬─ metrik → Prometheus
  └─ OTel trace (ASP.NET/Http/Npgsql)      ─┘                         ├─ log    → Loki
                                                                     └─ trace  → Tempo
                                              Grafana ← hepsi (+ Alerting → /internal/alert)
```

### Log — Serilog

- İstek başına **tek satır**: metot, yol, durum, süre + `tenant_id`, `user`, `request_id`.
  Çok kiracılı bir sistemde kiracı etiketi olmayan log, filtrelenemediği için işe yaramaz.
- Dosya sink'i JSON (`CompactJsonFormatter`).
- **Token sızıntısı koruması:** takvim feed URL'i içinde token taşıyor; o yolun istek logu
  `Verbose`'a düşürülüyor ki log erişimi olan biri feed'e erişemesin. Hata durumunda yine
  `Error` seviyesinde loglanır.

### Metrik — OpenTelemetry

Otomatik: ASP.NET Core (RED — hız/hata/süre), HttpClient, Npgsql, .NET runtime.

İş metrikleri tek bir `Meter "RentACar"` altında (`Application/Observability/RacarMetrics.cs`):

| Metrik | Ne söyler |
|---|---|
| `racar.login.total` | `result=success\|fail` |
| `racar.tahsilat.total` | `result=ok\|fail` |
| `racar.ledger.idempotent_reject.total` | Çift gönderim yakalandı — çift tıklama koruması çalışıyor |
| `racar.ratelimit.reject.total` | `policy` etiketiyle |
| `racar.job.fail.total` | `job` etiketiyle |

> **Kardinalite kuralı (kodda yazılı):** etiketler yalnız düşük kardinaliteli olabilir.
> `tenant`, `kullanıcı`, `plaka` gibi sınırsız değerler **asla** metrik etiketi olmaz — Prometheus'ta
> kardinalite patlaması demektir. Onlar log ve trace'te.

`Meter`'ın Application katmanında (BCL, ASP.NET bağımlılığı yok) durması bilinçli: hem
Infrastructure hem host'lar aynı sayaca yazabiliyor.

### Trace

ASP.NET Core + HttpClient + **Npgsql** enstrümantasyonu. Loki'deki log satırındaki `trace_id`
Tempo trace'ine tıklanarak gidilebiliyor, geri dönüş de kurulu.

### Config-gated — kapalıyken sıfır maliyet

`OTEL_EXPORTER_OTLP_ENDPOINT` **set değilse OTel hiç kurulmaz** — ne exporter ne
instrumentation. İstek başına `Activity` bile üretilmez. "Backend yoksa telemetri bedava olsun"
kararı; iş sayaçları dinleyicisiz `Meter` olarak no-op kalır.

### Health

`/health/live` (bağımlılık yok — süreç ayakta mı) ve `/health/ready` (veritabanı + migrator +
DataProtection key-ring). Ayrımın sebebi: readiness başarısızlığında orkestratör trafiği keser
ama süreci öldürmez; liveness başarısızlığında öldürür.

### Grafana'sız yedek alarm

Gözlem yığınının kendisi çökebilir. `OpsWatchdogJob` bunun için var — uygulama içinde çalışan
hafif bir backstop, iki sinyali izler:

- **TCMB kuru bayat.** En yeni kur kaydı 5 günden eskiyse dövizli para hesapları bayat kurla
  çalışıyor demektir. Eşiğin 5 gün olması bilinçli: TCMB yalnız iş günü yayınlar, Cuma kuru
  Pazartesi ~3,5 gün eskimiş olur — 3 günlük eşik her hafta sonu yanlış alarm üretirdi.
- **Tekrarlayan job hatası.** Mevcut `racar_job_fail_total` sayacını süreç içi bir `MeterListener`
  ile dinler; job kodlarına dokunmaz.

Config-gated: bildirim kanalı tanımlı değilse pasif.

---

## 4. Arka plan işleri

Hepsi `BackgroundService`; ayrı bir zamanlayıcı altyapısı (Hangfire/Quartz) **yok**. Gerekçe:
işler seyrek ve idempotent; ayrı bir zamanlayıcı, kendi veritabanı şeması ve panosuyla birlikte
gelen bir bağımlılık olurdu.

| Job | Aralık | Ne yapar |
|---|---|---|
| `TcmbKurJob` | 6 saat | TCMB günlük kurlarını çeker, saklar |
| `VadeBildirimJob` | 12 saat | Sigorta/MTV/muayene/bakım vadesi yaklaşan kayıtlar için bildirim üretir |
| `DonemFaturaJob` | 24 saat | Dönemsel faturalama (kiracı ayarıyla açılır, varsayılan **kapalı**) |
| `PendingDomainExpireJob` | 1 saat | 48 saatte doğrulanmayan domain taleplerini düşürür |
| `OpsWatchdogJob` | 1 saat | Yukarıdaki yedek alarm |

Ortak desenler:

- **Açılışta gecikme.** Her job 20–60 saniye bekleyerek başlıyor; migration ve seed bitmeden
  veritabanına asılmasınlar.
- **Üreticiler servis değil.** İş mantığı `DonemFaturaUretici.RunAsync(db, tenant, now)` gibi
  statik üreticilerde. Sebep: job'un kimlik bağlamı yok, `PermissionGuard`'sız çalışır. Bunu bir
  servis olarak yazmak, o servisi kullanıcı yüzeyinden çağırma iştahı doğururdu ve yetki kontrolü
  baypas edilirdi. (Bu tam olarak bir fazda önerilen ve **reddedilen** şeydi.)
- **Çalışma günlüğü.** `JobCalismaLog` tablosu her koşuyu kaydeder — "gece çalıştı mı" sorusunun
  cevabı loglarda aranmaz.
- **Kiracı döngüsü.** Job tüm kiracıları dolaşır ve her biri için kiracı kapsamlı bağlantı açar.

---

## 5. Eşzamanlılık ve idempotency

Üç ayrı mekanizma, üç ayrı probleme.

### 1. Danışma kilidi (advisory lock) — kontrol ile yazma arasındaki yarış

```csharp
SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))   // k = "kapatma:{tenant}:{cariId}"
```

Transaction bitince otomatik bırakılır. Kullanıldığı yerler: toplu borç kapatma
(`CashRepository`), depozito bakiye çiti, dönem kapanış fişi, dönemsel faturalama.

> **Kural:** kontrol, kilidin **arkasında** ve yazmayla **aynı transaction'da** yapılır.
> Kilidin dışında kalmış bir kontrol yüzünden eşzamanlı iki istek birlikte geçti ve dönem tahsilatı
> çift sayıldı. Bu, "flaky test" gibi görünen gerçek bir para hatasıydı.

### 2. Kısmi unique index — çift gönderim

Form her render'da bir işlem anahtarı (GUID) basar. Anahtar defter satırının `SourceId`'si olur;
kısmi unique index aynı anahtarla ikinci yazımı reddeder, uygulama `UniqueViolation`'ı sessizce
yutar ve metrik sayacını artırır.

Anahtar tasarımının öğrenilmiş kuralı: **deterministik anahtarda monoton bir bileşen olmalı.**
Değer anlık görüntüsünden (tutar + tarih) türetilen anahtar zamansal olarak çakışır — aynı tutarlı
iki meşru aylık kira tahsilatı birbirini yutar. Anahtara işlem sırası girer.

### 3. `SELECT … FOR UPDATE` — kontrol-sonra-sil yarışı

EF'in ürettiği `DELETE` yalnız `Id` yüklemi taşır; "onaysızsa sil" kuralı iki adıma bölündüğünde
arada durum değişebilir. Çözüm: açık transaction içinde `… AND "OnayDurumu" = 0 FOR UPDATE` ile
kilitli yeniden okuma.

`ExecuteDeleteAsync` bilinçli olarak kullanılmadı — denetim interceptor'ını baypas eder.

---

## 6. Güvenlik

### Kimlik ve oturum

| | Web (yönetim) | API |
|---|---|---|
| Yöntem | Cookie (`racar.session`) | JWT Bearer |
| Sertleştirme | `HttpOnly`, `SameSite=Lax`, prod'da `Secure=Always` | İmza anahtarı config'ten, **eksikse açılış reddedilir** |
| Süre | 8 saat | Token ömrü config |

Çerez adı bilinçli olarak değiştirilmiş (`.AspNetCore.Cookies` yerine) — framework parmak izini
gizler.

Giriş iki aşamalı: önce firma (kiracı), sonra kullanıcı.

### Rate limiting

Sabit pencere, **IP bazlı**, giriş uçlarında (varsayılan 60 saniyede 10 deneme, config'ten
ayarlanır). Reddedildiğinde metrik sayacı artar.

Ayrıntı: statik SSR akışında 429 gövdesi kullanıcıya çirkin bir sayfa gösterirdi; onun yerine
giriş sayfasına anlamlı bir mesajla yönlendirilir — PRG deseniyle tutarlı.

### Güvenlik başlıkları

```
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline';
                         img-src 'self' data:; font-src 'self'; connect-src 'self';
                         form-action 'self'; frame-ancestors 'self'; base-uri 'self'; object-src 'none'
X-Content-Type-Options: nosniff
X-Frame-Options: SAMEORIGIN
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: geolocation=(), camera=(), microphone=(), payment=()
```

`script-src 'self'` — **hiçbir inline script yok**, `'unsafe-inline'` da hash de yok. Buraya
gelmek kolay olmadı: 52 inline event handler ayrı bir JS dosyasına taşındı ve Blazor'ın
`<ImportMap>` inline script'i kaldırıldı (tam statik SSR'de gereksizdi ve fingerprint'i her asset
değişiminde CSP hash'ini bozuyordu). Sonuç: CSP artık kırılgan değil.

`X-Frame-Options: SAMEORIGIN` — `DENY` değil, çünkü PDF yazdırma same-origin gizli bir iframe
kullanıyor.

### Reverse proxy arkasında

`UseForwardedHeaders` ile `X-Forwarded-For` / `X-Forwarded-Proto` işlenir. Bunun atlanması klasik
bir tuzak: proxy TLS'i sonlandırdığında uygulama isteği HTTP sanır ve `Secure` çerezi hiç
göndermez. HSTS 1 yıl + subdomain'ler; `preload` **yok** (geri alması zor).

### Açılışta reddetme

Uygulama yanlış yapılandırılmışsa **başlamaz** — sessiz fallback yok:

- JWT imza anahtarı
- `Pii:HmacKey` (blind-index anahtarı)
- `RACAR_DP_KEYS` (DataProtection key-ring kalıcı dizini) — kalıcı olmayan bir dosya sisteminde
  redeploy sonrası şifreli PII **çözülemez** hâle gelirdi
- Platform süper-admin kimlik bilgileri
- Bağlantı dizeleri

### Bağımlılık taraması

CI'da `NU1903` (bilinen zafiyet) uyarıları görünür durumda; GitGuardian secret taraması her PR'da
çalışıyor.

> **Açık bulgu:** `System.Security.Cryptography.Xml 10.0.9` iki yüksek önem dereceli tavsiyede
> geçiyor (GHSA-g8r8-53c2-pm3f, GHSA-mmjf-rqrv-855v). Şu an uyarı olarak görünüyor, sürüm
> yükseltmesi yapılmadı.

---

## 7. REST API

`src/RentACar.Api` — ayrı host, JWT korumalı, OpenAPI dokümanlı.

`AuthApi` · `CustomersApi` · `VehiclesApi` · `ReservationsApi` · `RentalsApi` · `FinanceApi` ·
`ReportsApi` · `EkHizmetlerApi` · `ModulesApi`

Uygulama katmanını **doğrudan** kullanır — iş kuralı, yetki guard'ı ve kiracı izolasyonu web ile
birebir aynı kodtan geçer. API'ye özel bir iş mantığı katmanı yok; olsaydı iki yol arasında kural
kayması kaçınılmaz olurdu.

Yetkilendirme uçta `RequirePermission(Permission.X)` ile; rate limiter burada da kurulu.

---

## 8. Halka açık site

`src/RentACar.PublicSite` — kiracıya bağlı, anonim erişimli vitrin.

`TenantHostResolutionMiddleware` gelen `Host` başlığından kiracıyı çözer
(`TenantDomain` platform tablosu). İki katman önbellek: çözülen host'lar
(`CachedPublicTenantResolver`) ve **çözülemeyen** host'lar (`DomainAskCache` — negatif önbellek,
rastgele host ile gelen trafik veritabanını dövmesin).

Domain doğrulaması DNS/dosya tabanlı (`DomainVerificationEndpoints`); doğrulanmayan talep 48
saatte düşer.

İçerik: vitrin, blog, müsaitlik ve fiyat sorgusu, talep formu, SEO uçları (sitemap/robots).
Müsaitlik ve fiyat **aynı motordan** okunur — yönetim ekranıyla farklı fiyat göstermesi yapısal
olarak imkânsız.

---

## 9. Dosya üretimi

| Format | Kütüphane | Neden |
|---|---|---|
| Excel (.xlsx) | ClosedXML | MIT, kimliksiz, saf .NET |
| PDF | QuestPDF Community | Runtime lisans anahtarı gerektirmiyor |
| Görsel/thumbnail | SkiaSharp | MIT. ImageSharp'ın split-license eşiği ticari üründe istenmeyen bir risk; SkiaSharp'ın Linux "NoDependencies" varyantı apt paketi gerektirmiyor — tek VM systemd dağıtımına sorunsuz oturuyor. |

Export'lar **test edilebilir** bir katmandan geçer: `ExportTable` + `ListExportCatalog` /
`KarneExportKatalog`. Katalog saf bir projeksiyon olduğu için kolon kümesi birim testle
kilitlenebiliyor — "gördüğün = indirdiğin" kuralı (ekrandaki filtre export'a aynen taşınır)
böyle korunuyor.

> **Bilinen açık iş:** yaklaşık 20 export girdisi tarihleri ham UTC'den yazıyor; ekran yerel gün
> bastığı için export'ta tarihler bir gün geri kayabiliyor. Kasa/banka defteri ve hukuk dosyası
> export'larında düzeltildi (`DG()` biçimleyicisi), kalanı süpürme işi olarak duruyor.

---

## 10. Dış entegrasyonlar

`Application/Integrations/IntegrationPorts.cs` — hepsi **port (arayüz)**, gerçeklemeler stub:

`ISmsService` · `IWhatsAppService` · `IGoogleCalendarService` · `IEInvoiceService` (GİB gönderim
+ gelen kutusu) · HGS · banka/POS

Kimlik bilgisi geldiğinde tek sınıf değişimiyle açılır; ekranlar ve iş kuralları kimlik beklemeden
tamamlandı.

**Çalışan gerçek entegrasyonlar:** TCMB günlük kur çekimi (6 saatte bir, sabit-kur ile kiracı
bazında ezilebilir) ve Twilio WhatsApp (config-gated; günlük operasyon özeti ve ops alarmı).

---

## 11. Test altyapısı

```
RACAR_TEST_PG_ADMIN="Host=localhost;Port=5432;Username=<superuser>;Database=postgres" \
  dotnet test RentACar.slnx -c Debug
```

- `PostgresFixture` her koşuda **benzersiz bir veritabanı** oluşturur, `racar_owner` ve
  `racar_app` rollerini kurar, migration'ları uygular. Sınıflar `[Collection("postgres")]` ile
  paylaşır.
- `host.ScopeFor(tenant, userId, userName, role, assignedBranch)` ile servisler gerçek bir kimlik
  bağlamında çağrılır — yetki ve şube kapsamı testleri böyle yazılır.
- **İzolasyon testleri `racar_app` ile bağlanır.** `racar_owner` RLS'i esnetebildiği için onunla
  yazılmış bir izolasyon testi hiçbir şey kanıtlamaz.
- **~2.110 test**, tam koşu yerelde ~1,5 dakika.

### CI

GitHub Actions, `postgres:16` service container. Restore → Build (Release) → Test.
Her PR'da ayrıca GitGuardian secret taraması.

> **Yerel yeşil ≠ CI yeşil.** PostgreSQL `timestamptz` mikrosaniye çözünürlüklü, .NET tick 100 ns.
> Mac'te tesadüfen hizalı olan bir tarih eşitliği Linux CI'da düşer. Test tarih tabanları tam
> saniyeye hizalanır:
> ```csharp
> var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(3), DateTimeKind.Utc), TimeSpan.Zero);
> t = t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
> ```

---

## 12. Dağıtım

Tam kontrol listesi: `docs/ops/deploy-checklist.md`.

| | |
|---|---|
| Hedef | Tek VM: uygulama + PostgreSQL + reverse proxy |
| Boyut | 2–4 vCPU, 4–8 GB RAM, 40–80 GB SSD |
| OS | Ubuntu 24.04 LTS |
| Proxy | Caddy (otomatik TLS) |
| Süreç | systemd |
| Runtime | `aspnetcore-runtime-10.0` (SDK gerekmiyor) |
| Migration | **Açılışta otomatik** (`racar_owner` bağlantısıyla); elle adım yok |

Uygulama stateless — yatay ölçekleme için tek koşul DataProtection key-ring'inin paylaşımlı bir
volume'de olması.

**Yedekleme** (`docs/ops/yedekleme.md`): veritabanı dökümü + `Pii:HmacKey` + DataProtection
key-ring. Son ikisi olmadan veritabanı dökümü **işe yaramaz** — şifreli PII çözülemez.
`scripts/db-restore-drill.sh` geri yükleme tatbikatı için var; yedeğin geri yüklenebildiği
denenmeden yedek sayılmaz.

---

## 13. Yapılandırma ve özellik anahtarları

| Katman | Nerede |
|---|---|
| Altyapı (bağlantı, anahtarlar, OTLP, rate limit, Twilio) | `appsettings` / ortam değişkenleri |
| **Kiracıya özel iş kuralları** | `TenantSettings` tablosu — varsayılan KDV oranı, dönemsel faturalama açık/kapalı, otomatik tahsilat, kur elle giriş kilidi, varsayılan fiyat türü, tema renkleri |

Ayrım net: bir ayar "bu kurulum nasıl çalışır" ise config'te, "bu firma nasıl çalışır" ise
veritabanında.

Riskli anahtarların varsayılanı **kapalı** (dönemsel faturalama job'u, otomatik tahsilat) — bir
kiracı açıkça istemeden para yazan bir iş gece çalışmaz.

---

## 14. Kasıtlı olarak yapılmayan teknik seçimler

| Seçim | Gerekçe |
|---|---|
| Redis / dağıtık önbellek | Tek makine hedefi için gereksiz operasyonel yük. Arayüz hazır, gerçekleme değişir. |
| Hangfire / Quartz | 5 seyrek ve idempotent job için kendi şeması ve panosu olan bir bağımlılık. |
| CQRS / MediatR | Servisler doğrudan çağrılıyor. Ek bir dolaylılık katmanının bu boyutta karşılığı yok. |
| Repository yerine doğrudan `DbContext` | Repository katmanı `AsNoTracking`, kiracı kapsamı ve advisory kilit gibi kuralları tek yerde tutuyor. |
| In-memory test veritabanı | RLS'i test edemez — yani asıl korumak istediğiniz şeyi test etmemiş olursunuz. |
| `ExecuteDeleteAsync` / `ExecuteUpdateAsync` | Denetim interceptor'ını baypas eder. Mali sistemde kabul edilemez. |
| Blazor interaktif circuit | Bölüm 2, `docs/MIMARI.md`. |
| Mikroservis | Tek bir mali defter etrafında dönen bir sistem; dağıtık transaction'a bölmek problem üretir, çözmez. |

---

## 15. Bir mühendisin sorabileceği zor sorular

**"Kiracı izolasyonunu nasıl kanıtlıyorsunuz?"**
İki bağımsız katman (EF filtresi + Postgres RLS), uygulama `NOBYPASSRLS` bir rolle bağlanıyor, ve
her modülün testinde `racar_app` ile yazılmış bir izolasyon testi var. Ayrıca bir model testi, her
`ITenantOwned` varlığın filtre aldığını doğruluyor — yeni tablo eklerken unutmak imkânsız.

**"Aynı anda iki kullanıcı aynı tahsilatı yaparsa?"**
Deterministik işlem anahtarı + kısmi unique index ikinciyi reddeder; uygulama sessizce yutar ve
`racar_ledger_idempotent_reject_total` sayacını artırır. Bakiye çiti gibi kontroller advisory
kilidin arkasında ve yazmayla aynı transaction'da.

**"Defter bozulursa nasıl anlarsınız?"**
Bozulamaz: `LedgerPoster` dengesiz kümeyi yazmayı reddediyor, mali tablolarda `UPDATE`/`DELETE`
trigger ile engelli, düzeltme yalnız ters kayıtla. Raporlar tek kaynaktan (defter) okuyor.

**"Performans?"**
Şu anki ölçekte darboğaz yok; ölçüm noktaları kurulu (Npgsql trace, RED metrikleri, p95
dashboard'u). Bilinen maliyetli yollar önbelleklenmiş (master tanımlar) ve paylaşılan sorgular tek
yere toplanmış. Yük testi yapılmadı — dürüst cevap bu.

**"Kod incelemesi olmadan bu kaliteyi nasıl tutuyorsunuz?"**
Bkz. `docs/MIMARI.md` bölüm 8: gerçek veritabanına karşı testler, bağımsız oracle ilkesi ve para
tutan her değişiklikte zorunlu adversarial inceleme. Bulunan her gerçek hata kalıcı bir regresyon
testine dönüşüyor.
