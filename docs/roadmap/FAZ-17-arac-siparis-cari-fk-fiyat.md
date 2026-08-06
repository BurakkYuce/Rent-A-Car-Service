# FAZ-17 — Araç Sipariş: Cari-FK + Çok-Katmanlı Fiyat + Filtre

| | |
|---|---|
| **Desen** | D2 (Cari-FK + fiyat katmanları) + D3 (filtre) — hangi fiyat katmanı "resmi" **PARA — Opus'a devredilir** |
| **Efor** | 2,5 gün |
| **Bağımlılık** | FAZ-13 (soft — `KrediNo` alanı arac_kredi'nin `No` sıra numarasına bağlanınca anlamlı; FAZ-13 gitmeden de derlenir, sadece işlevsiz kalır) |
| **Kapsanan canlı ekran** | `arac_siparis.aspx`, `arac_siparis_detay_listesi.aspx`, `arac_siparis_listesi.aspx` |
| **Risk** | orta — çok-katmanlı fiyat alanı ekleniyor ama "resmi tutar" DEĞİŞMİYOR (regresyon garantili) |

## Amaç
Kullanıcı sipariş tedarikçisini bir **Cari** olarak arayıp seçebilir, sipariş detayına
Dosya No/İmza Tarihi/Temsilci/Versiyon-Opsiyon-Renk/çok-katmanlı fiyat (Piyasa/Ops/Filo)
girebilir ve krediyle (`KrediNo`) ilişkilendirebilir; `/arac-siparis` listesini Cari/Ad-Soyad/
Tarih/Plaka ile filtreleyebilir.

## Neden (kanıt)
- `arac_siparis.aspx`: K2=%29 (8/28), K3=%23 (5/22). Tedarikçi bir **Cari** kaydı
  (arama+seç); bizde özgür-metin. `DosyaNo`, `ImzaTarih`, `SatisTemsilci`/`OzelTemsilci`,
  `Versiyon`/`Opsiyon`/`Renk`/`IcRenk`, `KaynakTip`, `SatisTipi`, `PiyasaFiyat`/`OpsFiyat`/
  `FiloFiyat` (bugün tek `BirimFiyat`), `TsbKayitNo`, `KrediNo` yok.
- `arac_siparis_detay_listesi.aspx`: K2=%0 (0/6 — filtre formu yok), K3=%19 (4/21). Cari
  arama/Tarih aralığı/Plaka filtreleri + çok-katmanlı fiyat kolonları yok.
- `arac_siparis_listesi.aspx`: K2=%0 (0/6), K3=%50 (2/4). En dar canlı varyant (4 kolon);
  detay-listesi'nin filtre PR'ıyla birlikte karşılanır, ayrı kod gerekmez.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/AracSiparis.cs` — `TedarikciCariId` (`Guid?`, additive —
   eski `Tedarikci` text alanı KALIR) + `DosyaNo`, `ImzaTarih`, `SatisTemsilci`,
   `OzelTemsilci`, `Versiyon`, `Opsiyon`, `Renk`, `IcRenk`, `KaynakTip`, `SatisTipi`,
   `PiyasaFiyat`, `OpsFiyat`, `FiloFiyat` (bugün tek `BirimFiyat` — bu 3 alan EKLENİR,
   `BirimFiyat` KALIR), `TsbKayitNo`, `KrediNo` (`Guid?`, `AracKredi`'ye composite tenant-FK).
2. `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs`
   (`AracSiparisConfig`) — yeni alanlar + `KrediNo` FK.
3. Migration: `dotnet ef migrations add AddAracSiparisCariFkFiyat --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
4. `src/RentACar.Application/AracSiparisleri/AracSiparisInput.cs` — yeni alanları ekle.
5. `src/RentACar.Application/AracSiparisleri/AracSiparisService.cs` — `CreateAsync`/
   `UpdateAsync` map genişlet. **PARA — Opus not:** hangi fiyat katmanı "resmi" sipariş
   tutarı sayılacak — bu fazda `BirimFiyat` "resmi" kalır **DEĞİŞMEZ**, yeni 3 katman
   (`PiyasaFiyat`/`OpsFiyat`/`FiloFiyat`) SALT-BİLGİ.
6. `src/RentACar.Web/Components/Pages/AracSiparisleri/AracSiparisList.razor` — yeni alan
   input'ları + Cari arama/seç (`TedarikciCariId`).
7. `src/RentACar.Web/AracSiparisleri/AracSiparisEndpoints.cs` — POST handler genişlet.
8. (yeni) `src/RentACar.Application/AracSiparisleri/AracSiparisFilter.cs` — Cari/Ad-Soyad
   arama, Tarih aralığı, Plaka/Araç.
9. `src/RentACar.Application/AracSiparisleri/IAracSiparisRepository.cs` (mevcut, MODİFİYE) +
   `src/RentACar.Infrastructure/Persistence/Repositories/AracSiparisRepository.cs` (mevcut,
   MODİFİYE) — `SearchAsync` eklenir.
10. `AracSiparisService.cs` — `SearchAsync`.
11. `AracSiparisList.razor` — filtre formu + çok-katmanlı fiyat kolonları (aynı sayfa, en
    dar canlı varyant `arac_siparis_listesi` bu görünümün alt-kümesidir, ayrı kod gerekmez).

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/AracSiparis.cs`
- `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs`
  (AracSiparisConfig)
- (yeni) migration dosyası (`AddAracSiparisCariFkFiyat`)
- `src/RentACar.Application/AracSiparisleri/AracSiparisInput.cs`, `AracSiparisService.cs`
- (yeni) `src/RentACar.Application/AracSiparisleri/AracSiparisFilter.cs`
- `src/RentACar.Application/AracSiparisleri/IAracSiparisRepository.cs` (mevcut)
- `src/RentACar.Infrastructure/Persistence/Repositories/AracSiparisRepository.cs` (mevcut)
- `src/RentACar.Web/Components/Pages/AracSiparisleri/AracSiparisList.razor`
- `src/RentACar.Web/AracSiparisleri/AracSiparisEndpoints.cs`

## Migration
Var — mevcut `AracSiparisler` tablosuna additive kolonlar (`KrediNo` composite-FK dahil). RLS
zaten aktif → yeni RLS bloğu gerekmez.

## Test
- `AracSiparisServiceTests` — yeni alanların round-trip testi.
- `KrediNo` FK testi: var olan bir `AracKredi.Id` ile bağlanabildiği (oracle: sabit
  `krediId` seed edilir, sipariş o id ile oluşturulur, geri okunduğunda eşleşir); olmayan bir
  id ile `ValidationException` (negatif senaryo).
- `SearchAsync` filtre testi: 3 sipariş seed edilir (1'i `TedarikciCariId=X`), filtre ile
  **1** sonuç bekleniyor (sabit).

## Exit
- [ ] `TedarikciCariId`/`KrediNo`/çok-katmanlı fiyat alanları migration'la eklendi
- [ ] `AracSiparisList` Cari arama + yeni alanları gösteriyor
- [ ] Cari/Ad-Soyad/Tarih/Plaka filtre formu sonucu daraltıyor
- [ ] `BirimFiyat` "resmi tutar" davranışı DEĞİŞMEDİ (regresyon testiyle doğrulanır)
- [ ] Tam suite yeşil

## Notlar
Fiyat onayı üreten ekran (Liste/Piyasa/Ops/Filo/Onay fiyat katmanları) — çok katmanlı fiyat
modelinin bizde tek alana sıkıştırılmış olması PARA-Opus kararı; bu faz yalnız alan/altyapıyı
kurar, "resmi tutar"ı DEĞİŞTİRMEZ. `KrediNo` bağlamasının defter/AracKredi ilişkisine etkisi de
Opus kararı — bu faz `AracSiparis` "DEFTER POSTLAMAZ" ilkesini bozmaz (mevcut entity yorumu).
