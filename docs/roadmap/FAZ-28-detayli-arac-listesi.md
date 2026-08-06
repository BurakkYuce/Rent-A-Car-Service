# FAZ-28 — Detaylı Araç Listesi (Konsolide Grid)

| | |
|---|---|
| **Desen** | D3 (konsolidasyon — mevcut tablolardan join) + D1 (gerçekten eksik ~20 alan) |
| **Efor** | 2 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `detayli_arac_listesi.aspx` |
| **Risk** | düşük-orta — çoğu kolon mevcut tablolardan SALT-OKUNUR join; yeni yazılan alanlar
  (Vehicle'a 23 kolon) additive nullable, mevcut form akışlarını bozmuyor |

## Amaç
Araç listesine canlıdaki 52 detay kolonunun tamamını (30'u mevcut tablolardan join, 20'si gerçekten
yeni alan) kazandırarak kullanıcı tek grid'de aracın alım/kredi/muayene/sigorta/kira geçmişi
bilgilerini görebilir hale gelir.

## Neden (kanıt)
- Mevcut ve JOIN edilecek (D3, migration gerekmez): `Vehicle.AlimYapilanFirma/AlimTarihi/AlimBedeli`
  (satır 84-85, 116 doğrulandı), `VehicleSale.HedefFiyat` (satır 36 doğrulandı), `AracKredi.BankaAdi`
  (satır 19 doğrulandı), `InspectionRecord.Bitis` (satır 14 doğrulandı, araç başına en yakın),
  `InsurancePolicy.Bitis`+`Tip` (satır 16/20 doğrulandı, Tip'e göre filtrelenir), `Vehicle.
  FiloGirisTarih/FiloCikisTarih` (satır 100-101), `Vehicle.OzelKod1-5` (satır 104-108).
- Gerçekten YENİ (D1, `Vehicle.cs`'de yok — grep doğrulandı): `BelgeNo`, `RuhsatSahibi`, `SozNo`,
  `AraciAlan`, `SonTeslimKm`, `SonTeslimTarihi`, `Kiralayan`, `KiraGun`, `KiraFiyat`, `KiraBitTar`,
  `KiraBekTar`, `AssistanFirma`, `DisKmLimit`, `KiraMusteriId`, `TsbKodu`, `TsbKaskoDegeri`,
  `OdemeSekli`, `PasifSebep`, `SonDurum`, `AlisEuro`, `AlisEuroFiyat`, `SatisEuroFiyat`, `HgsFirma`.
- `VehicleSale.cs`'de yok: `IhaleTarihi`, `IhaleFirmasi`, `NoterSatisTarihi`.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/Vehicle.cs` — 22 nullable alan ekle: `BelgeNo` (string?),
   `RuhsatSahibi` (string?), `SozNo` (string?), `AraciAlan` (string?), `SonTeslimKm` (int?),
   `SonTeslimTarihi` (DateTimeOffset?), `Kiralayan` (string?), `KiraGun` (int?), `KiraFiyat`
   (decimal?), `KiraBitTar` (DateTimeOffset?), `KiraBekTar` (DateTimeOffset?), `AssistanFirma`
   (string?), `DisKmLimit` (int?), `KiraMusteriId` (Guid?, Customer FK — additive), `TsbKodu`
   (string?), `TsbKaskoDegeri` (decimal?), `OdemeSekli` (string?), `PasifSebep` (string?),
   `SonDurum` (string?), `AlisEuro` (bool?), `AlisEuroFiyat` (decimal?), `SatisEuroFiyat` (decimal?),
   `HgsFirma` (string?).
2. `src/RentACar.Domain/Entities/VehicleSale.cs` — 3 alan ekle: `IhaleTarihi` (DateTimeOffset?),
   `IhaleFirmasi` (string?), `NoterSatisTarihi` (DateTimeOffset?).
3. `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs` — ilgili `VehicleConfig`
   ve `VehicleSaleConfig` sınıflarına yeni kolonları ekle (`numeric(19,4)` para alanları için).
4. Migration: `dotnet ef migrations add AddVehicleDetayAlanlari --project src/RentACar.Infrastructure
   --startup-project src/RentACar.Infrastructure`.
5. `src/RentACar.Application/Vehicles/VehicleService.cs` — "detaylı görünüm" için join sorgusu
   ekle: `Vehicle` + son `AracKredi.BankaAdi` + araç başına en yakın `InspectionRecord.Bitis` + Tip'e
   göre `InsurancePolicy.Bitis` + `VehicleSale.HedefFiyat/IhaleTarihi/IhaleFirmasi/
   NoterSatisTarihi`. "Rez. Müşteri" alanı **DEPOLANMAZ** — aktif rezervasyon/kira üzerinden CANLI
   çözülür (D3: `ReservationRepository`/`RentalRepository`'den aracın aktif kaydı sorgulanır).
6. `src/RentACar.Web/Components/Pages/Vehicles/VehicleList.razor` — yeni bir "Detaylı Görünüm"
   sekmesi/görünümü ekle (mevcut grid'i BOZMADAN, ek sekme); Ofis (Şube) filtresi ekle.
7. Yeni alanları düzenleme formuna ekle (mevcut `VehicleList.razor` create/edit formuna, ya da
   ayrı bir "detay" alt-formu — proje konvansiyonuna göre `<details>` ile katlanır, form şişkinliğini
   yönetmek için).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/Vehicle.cs` — 22 alan
- `src/RentACar.Domain/Entities/VehicleSale.cs` — 3 alan
- `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs`
- (yeni) migration dosyası
- `src/RentACar.Web/Components/Pages/Vehicles/VehicleList.razor`
- `src/RentACar.Application/Vehicles/VehicleService.cs` — join sorgusu + "detaylı görünüm" DTO

## Migration
Var — `Vehicles` tablosuna 22, `VehicleSales` tablosuna 3 nullable kolon. RLS zaten aktif tablolar,
yeni tablo yok → RLS bloğu gerekmez.

## Test
- `VehicleTests`: 22 yeni alan round-trip (bağımsız oracle — elle kurulan araç nesnesi).
- `VehicleSaleTests`: 3 yeni alan round-trip.
- Detaylı görünüm join testi (bağımsız oracle): 1 araç + 1 kredi (BankaAdi="X Bankası") + 1 muayene
  kaydı (2 farklı tarih, en yakını elle bilinir) + 1 sigorta poliçesi (Tip=Kasko) elle kurulur;
  detaylı satırda Kredi Firma="X Bankası", Muayene Tar=elle bilinen en yakın tarih, Sig./Kasko Bit.
  Tar=doğru poliçenin bitiş tarihi olduğu doğrulanır.
- "Rez. Müşteri" canlı-çözme testi: aracın aktif bir kirası varken detaylı satırda o kiranın
  müşterisi görünüyor; kira bitince (ya da hiç kira yoksa) alan boş/"—" dönüyor (depolanmadığı
  regresyonla doğrulanır — DB'de böyle bir kolon YOK).
- Ofis (Şube) filtresi testi: 2 şubeden araç kurulur, filtre doğru alt-kümeyi döndürür.

## Exit
- [ ] Detaylı görünümde 52 kolonun tamamı (30 join + 22 Vehicle + 3 VehicleSale) görünüyor
- [ ] "Rez. Müşteri" depolanmıyor, canlı çözülüyor
- [ ] Ofis filtresi çalışıyor
- [ ] Tam suite yeşil

## Notlar
22+3=25 yeni alan sayısı plandaki "~20" tahminine yakın (plan "gerçekten YENİ ~20'si" diyordu,
gerçek sayım 25 çıktı — efor tahmini aynı kalır, sapma küçük).
