# CLAUDE.md — RentACar (TürevRent fonksiyonel klonu)

> Bu dosya, bu repoda çalışan her Claude Code / geliştirici oturumunun **ilk okuması gereken** bağlam ve kurallardır. Kod yapısını tekrar anlatmaz; **kararları, sözleşmeleri ve "neden"leri** taşır.

## 1. Proje nedir
`turev2.turevrac.com` adresindeki **TürevRent** (ASP.NET WebForms, ~155 ekran, rent-a-car/filo ERP) yazılımının **sıfırdan, fonksiyonel eşdeğeri**. Hedef: **çok-kiracılı (multi-tenant) SaaS**, .NET 10 üzerinde temiz mimari.

## 2. Yığın (locked — değiştirmeyin)
- **.NET 10 (LTS)**, ASP.NET Core, **Blazor Server *statik* SSR** (interaktif circuit DEĞİL — formlar `method="post"` minimal-API uçlarına gider).
- **EF Core 10 + PostgreSQL (Npgsql)**. Para = `decimal`/`numeric`.
- **Temiz mimari**, katmanlar: `RentACar.Domain` → `Application` → `Infrastructure` → `Web` (+ `tests/RentACar.IntegrationTests`). Bağımlılık yönü içe doğru; Domain hiçbir şeye bağımlı değil.
- Central Package Management (`Directory.Packages.props`), `.slnx` çözüm dosyası.
- Form alan adları ve domain alanları **Türkçe** (Plaka, Cari, Tutar, Sube…); sınıf adları İngilizce (Vehicle, Customer, Quotation).

## 3. Çalışma modeli / güven sözleşmesi (EN ÖNEMLİ)
Kullanıcı **C# kodunu incelemez**. Doğruluk şuradan gelir:
1. **Testler** — her modül entegrasyon testiyle gelir (gerçek PostgreSQL).
2. **Canlı parite** — uygulama çalıştırılıp gözle doğrulanır.
3. **Bağımsız oracle ilkesi**: testteki *beklenen değerler*, kodu yazan mantıktan DEĞİL, elle kurulmuş senaryodan türetilir (ör. "3 gün × 100 = 300" sabiti, `BookingMath`'ten değil). Beklenen değeri asla rapor/servis kodundan üretme.
4. **Küçük, gözden geçirilebilir PR'lar** — her biri ayrı commit.
5. **Para tutan her PR için zorunlu adversarial inceleme**: ayrı bir ajan, kodu *çürütmeye* çalışır (işaret hatası, çok-döviz, idempotency, RLS sızıntısı, yetki) — canlı DB'ye karşı ampirik. Critical/High/Medium bulgu varsa düzeltilmeden commit yok.

**Süreç notları:**
- Commit mesajları detaylı ve Türkçe (ne + neden + test özeti). Son satır: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Varsayılan dal `main`. Yeni iş için **önce feature branch** aç, sonra commit/push (kullanıcı onayıyla).
- Tarihsel olarak push engelliydi → bundle ile teslim ediliyordu. **Artık Mac'te `gh` ile push açık** (origin = `github.com/BurakkYuce/Rent-A-Car-Service`).

## 4. Kilitli teknik kararlar (non-negotiable)
### Çok-kiracılık (gün-0)
- Her tenant verisi `ITenantOwned` (`Guid TenantId`).
- **İki savunma katmanı**: (a) EF global query filter `HasQueryFilter(x => x.TenantId == TenantId)`; (b) **Postgres RLS** — her tenant tablosunda `ENABLE` + `FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy.
- **İki rol**: `racar_owner` (DDL/migration, RLS bypass edebilir — uygulama KULLANMAZ) ve `racar_app` (runtime, `NOSUPERUSER NOBYPASSRLS` — uygulama DAİMA bununla bağlanır).
- Tenant, bağlantı başına `set_config('app.tenant_id', …)` ile taşınır (`TenantConnectionInterceptor`). RLS policy bunu `NULLIF(current_setting('app.tenant_id',true),'')::uuid` ile okur.
- `Tenants` ve `Users` **platform tablolarıdır** (RLS yok; owner yazar, app okur).

### Para & muhasebe
- `Money` = `readonly record struct (decimal Amount, string Currency, decimal Rate)`; `AmountInBase => Amount * Rate`.
- **Çift-taraflı defter**: `AccountLedgerEntry` (`AccountType`, `AccountRef`, `Direction` Debit/Credit, `Money`, `SourceType`, `SourceId`). `LedgerAccountType`: Cari/Kasa/Banka/Gelir/Kdv/Gider. `LedgerDirection` **`AccountLedgerEntry.cs` içinde** (Domain.Entities, Enums'ta DEĞİL).
- Her işlem **DENGELİ** küme yazar: `Σ Borç(base) == Σ Alacak(base)` — repo `PostAsync`/`LedgerPoster` bunu zorlar.
- **Cari bakiye** = `Σ SignedBase` (Debit:+, Credit:−), AccountRef=cariId. Pozitif = müşteri borçlu.
- Düzeltme **ters kayıtla** yapılır (silme/update yok). İdempotency: kısmi unique index + `UniqueViolation` yutma.
- **DB-immutability**: fatura/defter gibi tablolarda `rc_prevent_mutation()` BEFORE UPDATE OR DELETE trigger.
- **Boşluksuz sıra no** (per tenant): `SequenceAllocator.NextAsync(db, tenant, "XNo")` → `RZ-000001` vb. Insert ile AYNI transaction.

### Yetki
- `UserRole`: Admin/Yonetici/Operator/Muhasebe. `Permission`: ManageUsers/OperationsWrite/FinanceWrite/ViewReports. Matris: `RolePermissions`.
- Servis guard: `PermissionGuard.Require(user, Permission.X)` → `ValidationException`. Web ayrıca `RequirePermission`/`[Authorize(Roles=…)]` (çift savunma).
- **Şube kapsamı**: `BranchScope.Effective(user)` → Operatör yalnız `AssignedBranch`'ini görür; diğerleri tümünü.

## 5. Yeni tenant-owned tablo ekleme reçetesi
1. `Domain/Entities/X.cs` : `ITenantOwned, IAuditable`, `Id` Guid (ValueGeneratedNever), audit timestamp'leri.
2. `AppDbContext`: `DbSet<X>` + `OnModelCreating`'de config (kolon tipleri `numeric(19,4)`, `HasIndex (TenantId, doğal-anahtar) IsUnique`, `HasQueryFilter(x => x.TenantId == TenantId)`).
3. `dotnet ef migrations add AddX --project src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
4. **Migration'a RLS bloğunu ELLE ekle** (EF üretmez): `ENABLE`+`FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation … USING/WITH CHECK (NULLIF(current_setting('app.tenant_id',true),'')::uuid)` + `GRANT … TO racar_app`. (Mali belgeyse ayrıca immutability trigger; değilse tam CRUD grant.)
5. `IXRepository` (Application) + `XRepository` (Infrastructure, `IDbContextFactory<AppDbContext>`, `AsNoTracking`).
6. `XService` (Application) — doğrulama + guard + iş kuralı.
7. DI: `Application/DependencyInjection.cs` (`AddScoped<XService>`) + `Infrastructure/DependencyInjection.cs` (`AddScoped<IXRepository, XRepository>`).
8. Web: `Components/Pages/.../XList.razor` + `X/XEndpoints.cs` + `Program.cs` `MapXEndpoints()` + `_Imports.razor` using + `MainLayout.razor` nav.
9. Test: `tests/…/XTests.cs` — bağımsız oracle (CRUD + benzersizlik + **tenant izolasyon** `racar_app` ile + yetki).

**Tuzak:** opsiyonel `decimal?`/`int?`/`DateTimeOffset?` form alanları boş string ("") gelince `[FromForm]` 400 verir → web ucunda `string?` parametre alıp `FormParse.Dec/Int/Date/Id` ile çevir.

## 6. Mevcut durum (2026-06, PR #29 itibarıyla)
**Tamam (Faz 1–5, ~29 PR, 221 entegrasyon testi yeşil):**
- İskelet: çok-kiracılık (RLS+FORCE), audit log, boşluksuz no, 2-aşamalı login, rol/yetki matrisi, şube kapsamı.
- Araç, Cari, Rezervasyon→Kira→Teslim/Dönüş, çift-taraflı defter, Kasa/Banka (tahsilat/ödeme/virman/ters), Fatura (KDV), Gider.
- Sigorta/MTV/Muayene + vade panosu, Ceza + HGS yansıtma, Araç satış + BAF, Servis/bakım.
- Raporlar: kasa/banka defteri, gelir-gider, cari bakiye+yaşlandırma, filo doluluk, servis maliyeti.
- Müsaitlik arama, detay sayfaları, liste arama/filtre/sayfalama, sözleşme/fatura yazdırma, denetim görünümü.
- **Şube master** (PR #27, additive — metin alanları korundu, FK'ye çevrilmedi), **Teklif→rezervasyon** (PR #28), **Rezervasyon takvimi** (PR #29).

**Bilerek ertelendi (kullanıcı kararıyla):**
- **Fiyat motoru** — şu an fiyat manuel günlük ücret (`BookingMath`). Canlı TürevRent paritesi 403 ile engelli → birebir parite alınamıyor.
- **Gerçek entegrasyonlar** (e-Fatura/SMS/HGS/banka/GİB) — hepsi **stub**.
- **Şube FK migrasyonu** — `Vehicle.Sube`/`Expense.Sube`/`User.AtanmisSube` hâlâ serbest metin; FK'ye çevirmek flagged bir karar.

**Eksik (genişlik):** orijinal ~155 ekranın çoğu (tanım/master ekranları: tarife, ek hizmet, ceza türü, KDV oranları, araç grubu; toplu işlemler; dönem kapanışı; PDF/Excel export; bildirim; dashboard derinliği).

## 7. Yerelde çalıştırma (Mac)
Önkoşul: .NET 10 SDK + PostgreSQL (Homebrew `postgresql@15` çalışıyor).
```bash
# postgres çalışmıyorsa:
brew services start postgresql@15
# roller + db + grant (tek seferlik, idempotent) — bkz. scripts/db-init-roles.sql + bu reçete
# (racar_owner / racar_owner_pw, racar_app / racar_app_pw, db racar)
# migration + seed açılışta otomatik:
ASPNETCORE_URLS=http://localhost:5220 dotnet run --project src/RentACar.Web
```
**Bağlantı (appsettings.json):** runtime = `racar_app`, migrator = `racar_owner`, db `racar`, port 5432.
**Giriş (seed):** firma `yucerent` / kullanıcı `umit` / şifre `umit1376` (Admin); `operator`/`umit1376` (Operatör); firma `demo` / `umit` / `umit1376`.

## 8. Test
```bash
RACAR_TEST_PG_ADMIN="Host=localhost;Port=5432;Username=burak;Database=postgres" \
  dotnet test RentACar.slnx -c Debug
```
- `PostgresFixture` test başına gerçek DB + roller (RLS dahil) kurar; `[Collection("postgres")]`.
- `host.ScopeFor(tenant, userId, userName, role=Admin, assignedBranch)` ile servis çağır.
- İzolasyon testleri MUTLAKA `racar_app` ile bağlanır (fixture öyle ayarlı).
- `RACAR_TEST_PG_ADMIN` superuser admin bağlantısı (Mac'te kullanıcı adın superuser, ör. `burak`, trust auth).

## 9. Sıradaki iş (öneri sırası)
1. **Tanım/master ekranları** (tarife, ek hizmet, ceza türü, KDV oranı, araç grubu) — engelsiz genişlik; tarife master'ı fiyat motorunun zeminini hazırlar.
2. **Fiyat motoru v1** (kural-bazlı; parite olmadan makul kurallarla).
3. **Şube FK migrasyonu** (flagged item'i kapat).
4. Export (PDF/Excel), bildirim, dashboard derinliği.

Ertelenenler parite/kimlik bilgisi gerektirir (fiyat motoru canlı 403, entegrasyonlar GİB/SMS kimlikleri) — açmadan önce kullanıcıya sor.
