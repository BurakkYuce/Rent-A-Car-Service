# FAZ-19 — Filo Plan Yönetimi (Yeni Dikey) + Boş Araç Listesi Zenginleştirmesi

| | |
|---|---|
| **Desen** | D7 (yeni dikey) + D3 (liste zenginleştirme) |
| **Efor** | 4 gün (3 + 1) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `arac_plan_yonetim.aspx`, `bos_arac_listesi.aspx` |
| **Risk** | düşük — para yok, yeni tablo RLS'li ama defter postlamıyor |

**Not:** Bu faz iki bağımsız küçük PR'ı (Filo Plan, Boş Araç Listesi) slot-bütçesi nedeniyle
tek dosyada belgeler — Bölüm A ve Bölüm B birbirinden dosya-bağımsızdır, istenirse 2 ayrı PR
olarak gönderilebilir.

## Amaç
Kullanıcı SIPP/Araç Grubu bazında HEDEF filo adedi tanımlayıp gerçekleşen adetle (mevcut
`Vehicle.Grup`/`Sipp` sayımından) karşılaştırabilir; `/musaitlik` sonuç listesinde canlı kadar
zengin araç kolonlarını, "Boştaki Süresi"ni ve doğrudan "Kirala" linkini görür.

## Neden (kanıt)
- `arac_plan_yonetim.aspx`: K2=%0 (0/20), aday route yok. Kod tabanında "hedef adet/filo
  planı" kavramı **hiç yok** (`grep` boş sonuç). SIPP/Araç Grubu bazında hedef filo adedi +
  gerçekleşen adet karşılaştırması + Artır/Azalt aksiyonu (kapasite planlama ekranı); en yakın
  olabilecek `/arac-siparis` (gerçek satın alma siparişi) FARKLI bir iş.
- `bos_arac_listesi.aspx`: K2=%40 (2/5), K3=%17 (4/24). "Boştaki Süresi", Tipi/Yılı/Yakıt
  Türü/Vites/Grup Özel Kod/Renk/SIPP/Lokasyon/Son Km/Kar Lastiği/Temizlik/Müşteri(son kullanan)
  kolonları yok; doğrudan Kirala/Rezerve aksiyonu yok (bizde sadece genel `/rezervasyonlar`
  linki).

## Yapılacaklar

### Bölüm A — arac_plan_yonetim (yeni dikey, CLAUDE.md §5 tam reçete)
1. Yeni `src/RentACar.Domain/Entities/FiloPlanHedefi.cs` — `ITenantOwned, IAuditable`:
   `Id` (Guid, `ValueGeneratedNever`), `TenantId`, `AracGrupAdi` (`string?`) veya `Sipp`
   (`string?`) — ikisinden biri zorunlu, `HedefAdet` (`int`), `Donem` (`string?`, nullable —
   açık uçlu plan).
2. Yeni `src/RentACar.Application/FiloPlan/IFiloPlanRepository.cs`, `FiloPlanInput.cs`,
   `FiloPlanService.cs` — CRUD + "gerçekleşen adet" hesaplaması (mevcut `Vehicle` sayımından,
   `AracGrupAdi`/`Sipp` eşleşmesiyle).
3. Yeni `src/RentACar.Infrastructure/Persistence/Configurations/FiloPlanConfigs.cs` —
   `IEntityTypeConfiguration<FiloPlanHedefi>` (kolon tipleri, `HasIndex(TenantId,
   AracGrupAdi)` gibi doğal-anahtar benzersizliği).
4. Yeni `src/RentACar.Infrastructure/Persistence/Repositories/FiloPlanRepository.cs`.
5. Migration: `dotnet ef migrations add AddFiloPlanHedefi --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`. **YENİ
   TABLO → RLS bloğu ELLE eklenir** (CLAUDE.md §5 adım 4): `ENABLE`+`FORCE ROW LEVEL
   SECURITY` + `CREATE POLICY tenant_isolation … USING/WITH CHECK
   (NULLIF(current_setting('app.tenant_id',true),'')::uuid)` + `GRANT SELECT, INSERT, UPDATE,
   DELETE ON "FiloPlanHedefleri" TO racar_app`.
6. DI: `src/RentACar.Application/DependencyInjection.cs` (`AddScoped<FiloPlanService>`) +
   `src/RentACar.Infrastructure/DependencyInjection.cs`
   (`AddScoped<IFiloPlanRepository, FiloPlanRepository>`).
7. Yeni `src/RentACar.Web/Components/Pages/FiloPlan/FiloPlanList.razor` — HEDEF filo adedi +
   gerçekleşen adet karşılaştırması + Artır/Azalt aksiyon-formu (ana tablonun dışında,
   `form=` attribute'lü mini-form).
8. Yeni `src/RentACar.Web/FiloPlan/FiloPlanEndpoints.cs`.
9. `src/RentACar.Web/Program.cs` — `MapFiloPlanEndpoints()`.
10. `src/RentACar.Web/Components/MainLayout.razor` — nav linki.

### Bölüm B — bos_arac_listesi (`MusaitlikArama`)
11. `src/RentACar.Application/Availability/AvailabilityService.cs` /
    `IAvailabilityRepository.cs` / `src/RentACar.Infrastructure/Persistence/Repositories/
    AvailabilityRepository.cs` — "Boştaki Süresi" (son tamamlanan kira/servisin dönüş
    tarihinden bugüne gün sayısı) ve "Müşteri (son kullanan)" için `Rentals` üzerinde
    `VehicleId` bazında son `GercekDonusTar` join'i ekle — yeni tablo gerekmez.
12. `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor` — Tipi/Yılı/
    Yakıt Türü/Vites/Renk/SIPP/Kar Lastiği/Temizlik kolonları (Vehicle'da zaten var, sadece
    tabloya eklenir) + Plaka arama kutusu + Boştaki Süresi/Müşteri kolonları + doğrudan
    "Kirala" linki (`/kiralar/yeni?vehicleId=X&from=&to=` prefill — mega-form zaten
    querystring prefill destekliyor, kontrol edilip yoksa küçük ek).

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Domain/Entities/FiloPlanHedefi.cs`
- (yeni) `src/RentACar.Application/FiloPlan/IFiloPlanRepository.cs`, `FiloPlanInput.cs`,
  `FiloPlanService.cs`
- (yeni) `src/RentACar.Infrastructure/Persistence/Configurations/FiloPlanConfigs.cs`,
  `src/RentACar.Infrastructure/Persistence/Repositories/FiloPlanRepository.cs`
- (yeni) migration dosyası (`AddFiloPlanHedefi`) — **RLS bloğu elle**
- `src/RentACar.Application/DependencyInjection.cs`,
  `src/RentACar.Infrastructure/DependencyInjection.cs`
- (yeni) `src/RentACar.Web/Components/Pages/FiloPlan/FiloPlanList.razor`,
  `src/RentACar.Web/FiloPlan/FiloPlanEndpoints.cs`
- `src/RentACar.Web/Program.cs`, `src/RentACar.Web/Components/MainLayout.razor`
- `src/RentACar.Application/Availability/AvailabilityService.cs`, `IAvailabilityRepository.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/AvailabilityRepository.cs`
- `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`

## Migration
Var — **yeni tablo** `FiloPlanHedefleri` → RLS bloğu ELLE eklenir (Bölüm A adım 5). Bölüm B
migration gerektirmez (mevcut veriden türetilen join).

## Test
- (yeni) `tests/.../FiloPlanTests.cs` — CRUD + benzersizlik + **tenant izolasyon**
  (`racar_app` ile) + yetki (CLAUDE.md §5 adım 9 şablonu). **Bağımsız oracle:** `HedefAdet=10`
  seed edilir, `Grup="X"` olan **7** araç elle seed edilir → beklenen Fark `=-3` (`7-10`, elle
  hesaplanmış sabit) teste yazılır.
- `AvailabilityServiceTests` — Boştaki Süresi hesap testi: bağımsız oracle —
  `GercekDonusTar=bugün-5gün` seed edilir, bugün çağrılır, beklenen `BoştakiSure=5` (sabit).

## Exit
- [ ] `/filo-plan` nav'da erişilebilir, Artır/Azalt aksiyonu çalışıyor
- [ ] Yeni tabloda RLS FORCE aktif, `racar_app` ile tenant-izolasyon testi yeşil
- [ ] `/musaitlik` sonuç listesinde yeni kolonlar + Plaka arama + doğrudan "Kirala" linki
  çalışıyor
- [ ] Tam suite yeşil

## Notlar
Bölüm A ve B birbirinden bağımsız, farklı entity/dosya kümesi — slot bütçesi nedeniyle tek
fazda belgelenmiştir. `/musaitlik`'in tarih-aralığı ZORUNLU arama parametresi (canlı ise anlık
"şu an boşta olanlar" mantığıyla çalışıyor) bilinçli UX farkı olarak **KALIR** — bu fazın
kapsamı DIŞINDA (plan dosyasının kendi notu: "YAPILMAZ değil, sadece önceliksiz").
