# FAZ-66 — Müşteri Taksit Takibi (yeni dikey) + AracKredi Wire-in

| | |
|---|---|
| **Desen** | D7 (yeni dikey) + D1 mikro (wire-in) |
| **Efor** | 4,25 gün (4 gün yeni dikey CLAUDE.md §5 tam reçete + 0,25 gün `AracKredi.VehicleId`
  wire-in) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `kredi_takip_listesi.aspx` |
| **Risk** | orta — yeni entity/tablo (CLAUDE.md §5 tam reçete, RLS dahil), ama para YAZMIYOR (takip
  amaçlı, mevcut defter akışına dokunmuyor) |

## Amaç
Kullanıcı müşteriye taksitli satış/kredi ile araç verme sürecini (Cari Bilgi + Plaka + Vade + Ödeme
Durumu ekseninde) takip edebilir hale gelir — bu, şirketin banka kredisiyle araç ALIMINI takip eden
mevcut `AracKredi`'den TAMAMEN FARKLI bir iş akışıdır.

## Neden (kanıt)
Canlı "Kredi Takip" ekranı **müşteriye taksitli satış/kredi ile araç verme** takibi yapıyor (Cari
Bilgi + Plaka + Vade + Ödeme Durumu ekseni). Bizim `AracKredi` (`src/RentACar.Domain/Entities/
AracKredi.cs`) **şirketin banka kredisiyle araç ALIMI** takibi yapıyor — tamamen farklı iş;
`AracKredi`'yi genişletmek KAVRAMSAL KARIŞTIRMA olurdu (iki farklı para akışı, iki farklı taraf: biri
şirket→banka borcu, diğeri müşteri→şirket alacağı).

Ayrıca `AracKredi.VehicleId` (`src/RentACar.Domain/Entities/AracKredi.cs` L20) şemada VAR ama ne
formda ne listede kullanılıyor (grep doğrulandı: `AracKrediList.razor`'da `VehicleId` referansı YOK)
— bu AYRI, küçük bir wire-in eksikliği, aynı PR'a eklenebilir boyutta.

## Yapılacaklar
1. CLAUDE.md §5 tam reçete — yeni tenant-owned tablo:
   - Yeni `src/RentACar.Domain/Entities/MusteriTaksit.cs`: `ITenantOwned, IAuditable`, `Id` (Guid,
     ValueGeneratedNever), `CariId` (Guid, müşteri), `VehicleId` (Guid?, opsiyonel — hangi araç),
     `VehicleSaleId` (Guid?, opsiyonel — `VehicleSale.Id` bağlantısı, taksitli satışsa), `Vade`
     (DateTimeOffset), `TaksitTutari` (decimal), `Currency`/`Kur`, `OdemeDurumu` (enum: Bekliyor/
     Odendi/Gecikti), audit timestamp'leri.
   - `AppDbContext`: `DbSet<MusteriTaksit>` + `MusteriTaksitConfig : IEntityTypeConfiguration
     <MusteriTaksit>` (kolon tipleri `numeric(19,4)`, `HasIndex(TenantId, CariId)`). **`HasQueryFilter`
     YAZILMAZ** — merkezi döngü otomatik uygular.
   - Migration: `dotnet ef migrations add AddMusteriTaksit --project src/RentACar.Infrastructure
     --startup-project src/RentACar.Infrastructure`. **RLS bloğu ELLE EKLENİR**: `ENABLE`+`FORCE ROW
     LEVEL SECURITY` + `CREATE POLICY tenant_isolation ... USING/WITH CHECK
     (NULLIF(current_setting('app.tenant_id',true),'')::uuid)` + `GRANT` CRUD `racar_app`'e (mali
     belge DEĞİL, immutability trigger gerekmez — TAM CRUD grant).
   - `IMusteriTaksitRepository` (Application) + `MusteriTaksitRepository` (Infrastructure,
     `IDbContextFactory<AppDbContext>`, `AsNoTracking`).
   - `MusteriTaksitService` (Application) — doğrulama + `PermissionGuard.Require(Permission.
     FinanceWrite)` + `BranchScope` (varsa) iş kuralı.
   - DI: `Application/DependencyInjection.cs` (`AddScoped<MusteriTaksitService>`) +
     `Infrastructure/DependencyInjection.cs` (`AddScoped<IMusteriTaksitRepository,
     MusteriTaksitRepository>`).
   - Web: `Components/Pages/MusteriTaksitleri/MusteriTaksitList.razor` + endpoint
     (`MusteriTaksitEndpoints.cs`) + `Program.cs` `MapMusteriTaksitEndpoints()` + `_Imports.razor`
     using + `MainLayout.razor` nav.
2. `AracKredi.VehicleId` wire-in (0,25 gün, ayrı iş ama aynı PR'a eklenir):
   `src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor`'a Araç (Plaka) seçim
   input'u eklenir (mevcut `VehicleId` kolonu yalnız forma/listeye bağlanır, migration gerekmez).

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Domain/Entities/MusteriTaksit.cs`
- (yeni) migration `AddMusteriTaksit` (RLS bloğu ELLE)
- (yeni) `IMusteriTaksitRepository` + `MusteriTaksitRepository`
- (yeni) `MusteriTaksitService`
- (yeni) `Components/Pages/MusteriTaksitleri/MusteriTaksitList.razor` + endpoint + nav
- `src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor` (VehicleId wire-in)

## Migration
Var — yeni `MusteriTaksitler` tablosu. **RLS bloğu ELLE EKLENİR** (CLAUDE.md §5, yukarıda detaylı).

## Test
- (yeni) `MusteriTaksitTests.cs` — CLAUDE.md §5 madde 9 reçetesi: CRUD + **tenant izolasyonu**
  (`racar_app` ile, iki tenant'ta birer `MusteriTaksit` oluşturulur, çapraz-tenant sorgu 0 satır
  döner mi) + yetki (`FinanceWrite` olmayan rol reddedilir mi — bağımsız oracle: `ValidationException`
  bekleniyor).
- Round-trip: elle bir `MusteriTaksit` (Vade, TaksitTutari sabit) oluşturulur → kaydet→oku, beklenen
  değerler test içinde sabit.
- `AracKrediGiderTests.cs`'e (veya yeni bir teste) ek senaryo: `VehicleId` dolu bir `AracKredi`
  oluşturulur, listede Plaka görünüyor mu (round-trip, bağımsız oracle).

## Exit
- [ ] `MusteriTaksit` CRUD çalışıyor
- [ ] RLS+FORCE aktif, tenant izolasyon testi yeşil
- [ ] Yetki guard'ı (FinanceWrite) çalışıyor
- [ ] `AracKredi.VehicleId` listede/formda görünüyor
- [ ] Tam suite yeşil

## Notlar
Bu faz PARA YAZMIYOR (defter postlaması yok, salt takip) — bu yüzden D5 zorunlu-adversarial kuralı
UYGULANMAZ (yeni dikey ama para-hareketsiz). İleride "taksit ödendi" aksiyonu deftere gerçek bir
tahsilat yazacak şekilde genişletilmek istenirse (`CashService.CollectAsync` ile bağlanma), o AYRI
bir D5 fazı olarak açılmalı.
