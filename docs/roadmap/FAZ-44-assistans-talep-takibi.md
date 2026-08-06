# FAZ-44 — Assistans Talep Takibi (Yol Yardım Mesajları, Yeni Dikey)

| | |
|---|---|
| **Desen** | D7 — yeni dikey |
| **Efor** | 3,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `musterigelenmesajlar.aspx` |
| **Risk** | orta — tamamen yeni entity/tablo, CLAUDE.md §5 tam reçetesi (RLS dahil) uçtan uca
  uygulanıyor; para/defter dokunmuyor ama yanlış RLS kurulumu klasik tenant-sızıntı riski taşır |

## Amaç
Kirada olan bir araçtan/müşteriden gelen yol-yardım tipi talep-mesajları (yedek lastik, araç hareket
edememe, vb.) kaydedilip `/assistans`'ta plaka+tarih ile aranabilir hale gelir — sistemde bugün hiç
izi olmayan bir işlev canlı paritesine kavuşur.

## Neden (kanıt)
Repo genelinde `AssistansTalep`/`musterigelenmesajlar` için **grep sıfır isabet** (doğrulandı — ne
entity, ne servis, ne razor sayfası, ne endpoint var). Canlı ekranın işlevi: kiradaki araçtan/
müşteriden gelen yol-yardım mesajlarının kaydı (yedek lastik gerekiyor mu, araç hareket edebiliyor
mu, mesaj/sebep metni) — bugün karşılığı hiç yok, tamamen yeni bir dikey.

## Yapılacaklar
1. Yeni `src/RentACar.Domain/Entities/AssistansTalep.cs` — `ITenantOwned, IAuditable`: `Id`,
   `TenantId`, `RentalId` (Guid?, `RentalContract` FK), `Plaka` (string, snapshot — kayıt anında
   `RentalContract.VehicleId`→`Vehicle.Plaka`'dan kopyalanır, sözleşme sonradan değişirse geçmiş
   talep metni sabit kalsın), `AdSoyad`/`CepTel` (string?, `Customer`'dan snapshot, aynı gerekçe),
   `Zaman` (DateTimeOffset, varsayılan `UtcNow`), `Mesaj` (string), `Sebep` (string?),
   `YedekLastikMi` (bool, default false), `AracHareketMi` (bool, default false).
2. `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — yeni
   `AssistansTalepConfig : IEntityTypeConfiguration<AssistansTalep>` sınıfı ekle (`HasIndex(TenantId,
   Plaka)`, `HasIndex(TenantId, Zaman)` — plaka+tarih arama için).
3. Migration: `dotnet ef migrations add AddAssistansTalep --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
4. **YENİ TABLO — RLS bloğunu migration'a ELLE ekle** (EF üretmez, CLAUDE.md §5):
   `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation …
   USING/WITH CHECK (NULLIF(current_setting('app.tenant_id',true),'')::uuid)` +
   `GRANT SELECT, INSERT, UPDATE, DELETE ON … TO racar_app` (mali belge değil, tam CRUD).
5. Yeni `src/RentACar.Application/Crm/IAssistansTalepRepository.cs` (`SearchAsync(Plaka?, TarihMin?,
   TarihMax?, ct)`, `FindAsync`, `CreateAsync`, `UpdateAsync`) — `IDbContextFactory<AppDbContext>`,
   liste tarafı `AsNoTracking`.
6. Yeni `src/RentACar.Infrastructure/Persistence/Repositories/AssistansTalepRepository.cs` —
   arayüzün implementasyonu (mevcut `CrmRepositories.cs` deseniyle aynı dosyada YA DA ayrı dosya —
   yeni bir domain olduğu için ayrı dosya tercih edilir, mevcut Anket/Sikayet dosyasına eklenmez).
7. Yeni `src/RentACar.Application/Crm/AssistansTalepService.cs` — `PermissionGuard.Require(
   OperationsWrite)` guard'lı CRUD + `RentalId` verilince `Plaka`/`AdSoyad`/`CepTel` snapshot'ını
   otomatik doldurma mantığı (RentalContract + Customer join).
8. Yeni `src/RentACar.Web/Components/Pages/Crm/AssistansTalepList.razor` — `/assistans` sayfası:
   Plaka + Tarih aralığı filtresi, liste (Zaman/Plaka/Ad Soyad/Cep Tel/Mesaj/Sebep/Yedek Lastik/Araç
   Hareket kolonları), create formu (`RentalId` seçici → snapshot alanları otomatik doldurur,
   kullanıcı override edebilir).
9. Yeni `src/RentACar.Web/Crm/AssistansTalepEndpoints.cs` — `POST /assistans/create`,
   `POST /assistans/update` (mevcut `CrmEndpoints.cs` deseniyle, `AntiforgeryToken` + guard).
10. `src/RentACar.Web/Program.cs` — `app.MapAssistansTalepEndpoints();` ekle (satır 493 civarı,
    `MapCrmEndpoints()` yanına).
11. `src/RentACar.Web/Components/Layout/MainLayout.razor` — nav'a `<a href="/assistans">Assistans
    Talepleri</a>` ekle (satır 67 civarındaki CRM `nav-group` içine, Sikayetler/Anketler yanına).
12. DI: `src/RentACar.Application/DependencyInjection.cs` (`AddScoped<AssistansTalepService>`) +
    `src/RentACar.Infrastructure/DependencyInjection.cs`
    (`AddScoped<IAssistansTalepRepository, AssistansTalepRepository>`).

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Domain/Entities/AssistansTalep.cs`
- `src/RentACar.Infrastructure/Persistence/Configurations/CustomerConfigs.cs` — yeni
  `AssistansTalepConfig`
- (yeni) migration `AddAssistansTalep` (RLS bloğu dahil)
- (yeni) `src/RentACar.Application/Crm/IAssistansTalepRepository.cs`
- (yeni) `src/RentACar.Infrastructure/Persistence/Repositories/AssistansTalepRepository.cs`
- (yeni) `src/RentACar.Application/Crm/AssistansTalepService.cs`
- (yeni) `src/RentACar.Web/Components/Pages/Crm/AssistansTalepList.razor`
- (yeni) `src/RentACar.Web/Crm/AssistansTalepEndpoints.cs`
- `src/RentACar.Web/Program.cs` — `MapAssistansTalepEndpoints()`
- `src/RentACar.Web/Components/Layout/MainLayout.razor` — nav
- `src/RentACar.Application/DependencyInjection.cs`, `src/RentACar.Infrastructure/DependencyInjection.cs`

## Migration
Var — **yeni** `AssistansTalep` tablosu, RLS bloğu ELLE eklenir (madde 4).

## Test
- `AssistansTalepTests` (yeni): CRUD round-trip (bağımsız oracle — elle kurulan talep nesnesi).
  **Tenant izolasyonu `racar_app` ile** (CLAUDE.md §8) — 2 tenant'ta aynı plaka için talep elle
  kurulur, çapraz sorgu 0 satır dönmeli.
- Snapshot testi: 1 `RentalContract` (Plaka="34ABC12", Customer Ad="Ali Veli", CepTel="0555...")
  elle kurulur; `RentalId` verilerek talep oluşturulunca `Plaka=="34ABC12"`, `AdSoyad=="Ali Veli"`
  beklenir (sabit, servis kodundan değil elle girilen sözleşme verisinden).
- Filtre testi: 3 talep (2 farklı plaka, 2 farklı tarih) elle oluşturulur; Plaka filtresiyle arama
  yalnız o plakaya ait kaydı döndürüyor mu (beklenen sayı testte sabit).
- Yetki testi: create/update yalnız `OperationsWrite` iznine sahip roller (Admin/Yonetici/Operator)
  çalışıyor; izinsiz rol `ValidationException`/403.

## Exit
- [ ] `/assistans` sayfası açılıyor, create/update çalışıyor
- [ ] Plaka + Tarih filtresi çalışıyor
- [ ] Tenant izolasyonu doğrulandı (racar_app ile çapraz-tenant testi geçiyor)
- [ ] Nav'da görünüyor, yetkisiz rol engelleniyor
- [ ] Tam suite yeşil

## Notlar
`Plaka`/`AdSoyad`/`CepTel` bilinçli olarak **snapshot** (kopya), FK-üzerinden canlı okuma DEĞİL —
sözleşme/müşteri sonradan değişse (plaka takas, müşteri adı düzeltmesi) geçmiş talep kaydı o anki
gerçeği yansıtmalı (CLAUDE.md'deki diğer "bilgi amaçlı" snapshot desenleriyle tutarlı, ör. `Sikayet`
FAZ-43'teki CikisOfisi snapshot'ı).
