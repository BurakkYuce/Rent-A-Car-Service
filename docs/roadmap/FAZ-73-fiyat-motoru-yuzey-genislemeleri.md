# FAZ-73 — Fiyat Motoru Yüzey Genişletmeleri (Broker Müsaitlik + Doluluk Toplu-Giriş + Kampanya Arama)

| | |
|---|---|
| **Desen** | D3 (`broker_musaitlik_listesi`) + D2 (`doluluk_algoritma`, `kampanya_ara`) |
| **Efor** | 4 gün (`broker_musaitlik_listesi.aspx` 1g + `doluluk_algoritma.aspx` 1g +
  `kampanya_ara.aspx` 2g) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `broker_musaitlik_listesi.aspx`, `doluluk_algoritma.aspx`,
  `kampanya_ara.aspx` |
| **Risk** | orta — `kampanya_ara.aspx` bölümü `RentalRule.Aktif`→`KampanyaDurum` göçü + TÜM tüketim
  noktalarının taşınması gerektiriyor (wire-in tamlığı riski); diğer iki ekran düşük risk |

## Amaç
(1) `/musaitlik` broker/kanal-yöneticisi görünümüne genişler (SIPP/Km Limiti/Yaş/Ehliyet/Provizyon/
Drop Bedeli/Şube kolonları + Kaynak/Döviz filtresi). (2) `/doluluk-fiyat` sayfasına aynı grup için
10 kademeyi tek formda girme yardımcısı + "Sadece Kendi Şubeleri" bayrağı eklenir. (3) `/kira-
kurallari`'na arama formu + kampanyaların 5-durumlu (Planlandı/Aktif/Pasif/Taslak/İptal) yaşam
döngüsü eklenir.

## Neden (kanıt)
- `src/RentACar.Application/Availability/AvailabilityService.cs:18-19` — `FindAvailableAsync(from,
  to, grup?, sube?, ct)` bugün Kaynak/Döviz parametresi ALMIYOR; `MusaitlikArama.razor` (route
  `/musaitlik`) filtreleri sadece Başlangıç/Bitiş/Grup/Şube (4 filtre), kolonlar Plaka/Marka/Grup/
  Şube/Günlük/Toplam/Döviz (SIPP/Km Limiti/Yaş/Ehliyet/Provizyon/Drop Bedeli YOK). Bu alanların
  KAYNAĞI zaten mevcut: `VehicleGroup.Sipp` (satır ~28), `GunlukKmLimiti`/`AylikMaxKm` (64-66),
  `SurucuMinYas`/`GencSurucuYas` (49-50), `EhliyetMinYil`/`GencEhliyetMinYil` (56-58), `Provizyon`
  (60) — hepsi doğrulandı, sadece JOIN eksik.
- `src/RentACar.Domain/Entities/DolulukFiyatKural.cs` alanları doğrulandı: `Kod, Ad, AracGrupKod,
  EsikYuzde, CarpanYuzde, GecerlilikBas/Bit, Aktif` — `Sube`/`SadeceKendiSubeleri` alanı YOK, sınıf
  `IBranchScoped` implement ETMİYOR. `RentalQuoteEngine.cs` satır 76-95 (doluluk çarpanı bloğu) hiç
  şube eşleşme kontrolü yapmıyor (doğrulandı — `req.Sube`/`Sube` DolulukFiyatKural filtresinde hiç
  geçmiyor).
- `RentalRule.cs` alanları doğrulandı: `Kod, Ad, Aciklama, Kanal, Sube, SubeId, AracGrupKod, MinGun,
  MaxGun, Iskonto, HaftaSonuFarkOran, SonraOdeOran, HediyeGun, KampanyaMi, KampanyaKodu,
  MusteriSegment, GecerlilikBas/Bit, SartMetni, Aktif` (bool) — `Aktif` OKUNAN TEK nokta:
  `RentalRuleRepository.ListActiveAsync` → `.Where(r => r.Aktif)` (`RentalRuleRepository.cs:26`).
  `RentalRuleService.ListAsync()` (filtresiz — UI listesi) ile `ListActiveAsync()` (fiyat motoru,
  `RentalQuoteEngine.cs:155`) AYRI yollar — bugün wire-in eksiği YOK, ama 5-durum göçünde
  `.Where(r => r.Aktif)` yerine `.Where(r => r.KampanyaDurum == KampanyaDurum.Aktif)` olacak; bu TEK
  satır repository'de olduğundan risk düşük ama migration'daki backfill (`Aktif=true→Aktif`,
  `Aktif=false→Pasif`) ve iki kolonun senkron bırakılması dikkat gerektirir.
- Arama formu: `RentalRuleList.razor` (route `/kira-kurallari`) bugün arama/filtre formu taşımıyor
  (yalnız liste+create/edit).

## Yapılacaklar
### A) broker_musaitlik_listesi.aspx (1 gün)
1. `src/RentACar.Application/Availability/IAvailabilityRepository.cs` +
   `AvailabilityService.cs` — `FindAvailableAsync`/`GetAvailableAsync` imzasına `string? kaynak`,
   `string? doviz` opsiyonel parametre eklenir (geriye uyumlu, default `null`).
2. `MusaitlikArama.razor` — arama formuna Rez_Kaynağı dropdown (`ReservationSourceService.
   ListActiveAsync`) + Döviz seçici eklenir; sonuç tablosuna `VehicleGroup` JOIN'inden SIPP/Km
   Limiti/Yaş/Ehliyet/Provizyon kolonları + çıkış≠dönüş şubeyse `DropTanim.Ucret` (Drop Bedeli) +
   `Vehicle.SubeId` (Şube Id) kolonları eklenir.
3. Yeni additive `bool Analiz` alanı — hangi entity'ye ekleneceği (muhtemelen arama sonucu satırına
   geçici bayrak, PERSIST edilen bir kolon değil — DTO-only) PR başında netleştirilir; persist
   gerekiyorsa `VehicleGroup`'a küçük bool kolon.
4. Not: canlı ekran GRUP/SIPP bazlı TOPLU satır gösteriyor, bizimki araç-bazlı kalır — bu PR
   grup-bazlı özet moduna DOKUNMAZ.

### B) doluluk_algoritma.aspx (1 gün)
5. `src/RentACar.Web/Components/Pages/DolulukFiyat/DolulukFiyatList.razor` — "aynı grup için 10
   kademeyi tek formda gir" toplu-giriş yardımcı formu (10 satırlık `EsikYuzde`/`CarpanYuzde` grid,
   submit'te 10 ayrı `DolulukFiyatKuralService.CreateAsync` çağrısı) — entity/motor DEĞİŞMEZ.
6. `src/RentACar.Domain/Entities/DolulukFiyatKural.cs` — additive `SadeceKendiSubeleri bool`
   (default `false`) + `IBranchScoped` implementasyonu (mevcut `RentalRule`/`RateMatrix` deseniyle
   aynı `SubeAdi`/`SubeFk` property haritası).
7. `RentalQuoteEngine.cs` satır 76-95 (surge bloğu) — `SadeceKendiSubeleri` true olan kurallar için
   `req.Sube == kural.Sube` (ya da `SubeId` eşleşmesi) kontrolü eklenir; false/null ise mevcut
   davranış (şube-agnostik) korunur.

### C) kampanya_ara.aspx (2 gün)
8. `RentalRule.cs` — additive `TarihTipi` enum (Talep/Rezervasyon) + `KampanyaDurum` enum
   (Planlandı/Aktif/Pasif/Taslak/İptal); `Aktif bool` kolonu bir süre SENKRON bırakılır (geriye
   uyum).
9. Migration: backfill `Aktif=true → KampanyaDurum.Aktif`, `Aktif=false → KampanyaDurum.Pasif`.
10. **Wire-in completeness (zorunlu grep taraması):** `.Aktif` okuyan TÜM noktalar bulunup
    `KampanyaDurum==Aktif`'e taşınır. Bugün tespit edilen tam liste: `RentalRuleRepository.cs:26`
    (`.Where(r => r.Aktif)` → fiyat motoru besleyen `ListActiveAsync`), `RentalRuleService.cs:114,139`
    (create/update — `KampanyaDurum` de aynı noktalarda set edilecek), `RentalRuleEndpoints.cs:49`
    (form-parse). Her ikisinin de (Aktif VE KampanyaDurum) senkron güncellendiği create/update
    yolunda garanti edilir (tek yazma noktasından, iki alan birlikte set edilir — asenkron sürüklenme
    riski yok).
11. `RentalRuleList.razor` — arama formu: Kural Adı (metin), Tarih Tipi (dropdown), tarih aralığı
    (`GecerlilikBas/Bit` filtre) + `KampanyaDurum` filtresi (dropdown, 5 değer).
12. `RentalRuleEndpoints.cs` — arama query-param'ları (`FormParse` ile opsiyonel) eklenir.

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Availability/MusaitlikArama.razor`
- `src/RentACar.Application/Availability/AvailabilityService.cs`,
  `IAvailabilityRepository.cs`
- `src/RentACar.Domain/Entities/DolulukFiyatKural.cs` — additive `SadeceKendiSubeleri`
- `src/RentACar.Application/DolulukFiyat/DolulukFiyatFiles.cs`
- `src/RentACar.Web/Components/Pages/DolulukFiyat/DolulukFiyatList.razor`
- `src/RentACar.Application/Pricing/RentalQuoteEngine.cs` — şube eşleşme kontrolü
- `src/RentACar.Domain/Entities/RentalRule.cs` — additive `TarihTipi`, `KampanyaDurum`
- `src/RentACar.Application/RentalRules/RentalRuleService.cs`, `RentalRuleInput.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/RentalRuleRepository.cs`
- `src/RentACar.Web/Components/Pages/RentalRules/RentalRuleList.razor`
- `src/RentACar.Web/RentalRules/RentalRuleEndpoints.cs`

## Migration
- Bölüm A/B: `DolulukFiyatKural`'a additive `SadeceKendiSubeleri bool` — tablo zaten RLS'li, yeni
  kolon RLS bloğu gerektirmez.
- Bölüm C: `RentalRule`'a additive `TarihTipi` (enum/int), `KampanyaDurum` (enum/int) + backfill
  script (data migration, `Aktif` değerine göre). Tablo zaten RLS'li, yeni kolon RLS bloğu
  gerektirmez; backfill idempotent yazılmalı (migration `Up()` içinde tek seferlik UPDATE).

## Test
- `AvailabilityServiceTests`: Kaynak/Döviz parametresiyle arama — elle kurulan 2 araç (biri eşleşen
  kaynak, biri değil) → sonuçta yalnız eşleşenin döndüğü bağımsız oracle ile doğrulanır.
- `DolulukFiyatKuralTests`: `SadeceKendiSubeleri=true` kuralı + farklı şubeden istek → surge
  UYGULANMAZ (elle hesaplanan günlük ücret sabit kalır); aynı şubeden istek → surge uygulanır (elle
  hesaplanan `%carpan` ile çarpılmış değer).
- `RentalRuleTests`: 5-durum migration — `Aktif=true` olan 3 elle-kurulmuş satır backfill sonrası
  `KampanyaDurum.Aktif` olduğu; `Aktif=false` olan 2 satır `Pasif` olduğu doğrulanır (sabit sayılar,
  migration kodundan değil test kurulumundan). **Wire-in regresyon testi:** `KampanyaDurum=
  Planlandı` olan bir kural `RentalQuoteEngine`'de OTOMATİK SEÇİLMEZ (bugünkü `Aktif=true` ile aynı
  testin yeni-enum karşılığı) — bu test madde 10'daki riskin gerçekten kapandığını kanıtlar.
- Tenant izolasyon: `racar_app` ile `RentalRule`/`DolulukFiyatKural` başka tenant'ta görünmüyor.

## Exit
- [ ] Müsaitlik araması Kaynak/Döviz filtreli, SIPP/Km/Yaş/Ehliyet/Provizyon/Drop kolonlu
- [ ] Doluluk toplu-giriş formu 10 kademeyi tek submit'te kaydediyor
- [ ] `SadeceKendiSubeleri` surge'ü doğru kısıtlıyor (testli)
- [ ] `KampanyaDurum` 5-durumu backfill'li, TEK tüketim noktası (`ListActiveAsync`) yeni enum'u
      okuyor, wire-in regresyon testi yeşil
- [ ] Tam suite yeşil

## Notlar
Doluluk motorunun **%50 tavan formülü DEĞİŞMİYOR** (`Math.Min(surge.CarpanYuzde, 50m)`,
`RentalQuoteEngine.cs:90`) — PARA-Opus kararı GEREKMİYOR, sadece şube-kapsamı eklenir. Kampanya
5-durum göçünde `Aktif` kolonu bu fazda SİLİNMEZ (geriye uyum için senkron bırakılır) — kaldırma
kararı ayrı bir temizlik fazı.
