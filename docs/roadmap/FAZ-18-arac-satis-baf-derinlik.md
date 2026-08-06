# FAZ-18 — Araç Satış + Baf: Alan/Filtre Zenginleştirmesi

| | |
|---|---|
| **Desen** | D1 (alan ekleme) + D3 (filtre/arama) — satış hedef-fiyat/KDV ayrımı **PARA — Opus'a devredilir** |
| **Efor** | 3 gün (1,5 + 1,5) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `arac_satis.aspx`, `arac_satis_ara.aspx`, `baf_ara.aspx`, `baf_islemleri.aspx` |
| **Risk** | orta — `VehicleSale` DB-immutable mali belge; yeni alanlar açıkça "bilgilendirme; deftere yansımaz" (kod yorumu) olarak eklenir |

**Not:** Bu faz iki bağımsız küçük PR'ı (Araç Satış, Baf) slot-bütçesi nedeniyle tek dosyada
belgeler — Bölüm A ve Bölüm B birbirinden dosya-bağımsızdır, istenirse 2 ayrı PR olarak
gönderilebilir.

## Amaç
Kullanıcı "Yeni Satış" formunda kiraya-verme/ilan-km/kampanya/ihale/devir-durumu bilgilerini
girebilir ve `/satislar` listesini tarih/durum/plaka ile filtreleyebilir; "Yeni BAF"/"Teslim Al"
formlarında kullanım amacı/onay/saat granülü bilgilerini girebilir ve `/baf` listesini
personel/plaka/durum/lokasyon ile arayabilir.

**Not — wire-in DIŞARIDA:** `VehicleSale.HedefFiyat/SatisKm/SatisKanali/Devir` ve
`Baf.DonusTarihi/DonusYakit` zaten entity+input+servis katmanında var; form alanlarının
eklenmesi `docs/roadmap/FAZ-00-bedava-kazanclar.md` kapsamındadır — **bu fazda o adımlar YOK**.

## Neden (kanıt)
- `arac_satis.aspx`: K2=%26 (6/23). `Kiraya_Verme`, `Liste_Fiyati+Liste_Doviz`, `Ilan_Km`
  (`Satis_Km`'den ayrı — ilan anındaki km), `Satis_Kanali`/`Satis_Noktasi`/
  `Uygulanan_Kampanya`, İhale bloğu (`Ihale_Firmasi`/`Tarihi`/`Sayisi`), `Satisi_Verildi`
  (devir durumu — `Devir` text alanından ayrı), `Yevmiye_Numarasi`, `Aciklama2` yok.
- `arac_satis_ara.aspx`: K2=%0 (0/8 — hiç filtre formu yok), K3=%17 (6/36). Tarih aralığı,
  `Satisi_Verildi`/`Hedef_Fiyat_Turu`/`Durumx`/`Plaka`/`Ofis` filtreleri yok.
- `baf_ara.aspx`: K2=%0 (0/9), K3=%12 (2/17). `/baf`'ta CRUD var ama ARAMA/FİLTRE yok —
  Kullanıcı(personel)/Plaka/Durum/Lokasyon/Kullanım Amacı/Tarih aralığı/Ofis araması yok.
- `baf_islemleri.aspx`: K2=%44 (7/16). `Kullanim_Amaci` (11 seçenek), `Onaylayan`,
  `Kiraya_Ver`, `DropDownListDurum`, `Cikis_Saat`/`Donus_Saat`, `Donus_Ofisi` (`DonusSube`)
  yok.

## Yapılacaklar

### Bölüm A — arac_satis + arac_satis_ara (`VehicleSale`)
1. `src/RentACar.Domain/Entities/VehicleSale.cs` — ekle: `KirayaVerme` (`bool`), `IlanKm`
   (`int?`, `SatisKm`'den AYRI), `UygulananKampanya` (`string?`), `IhaleFirmasi`/`IhaleTarihi`/
   `IhaleSayisi` (İhale bloğu), `SatisiVerildi` (`bool`, devir durumu — `Devir` text alanından
   AYRI), `YevmiyeNumarasi` (`string?`), `Aciklama2` (`string?`), `ListeDoviz` (`string?`,
   `HedefFiyat`'ın döviz karşılığı).
2. `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs`
   (`VehicleSaleConfig`) — yeni alanlar.
3. Migration: `dotnet ef migrations add AddVehicleSaleAlanlari --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
4. `src/RentACar.Application/VehicleSales/VehicleSaleInput.cs` — 8 alanı ekle.
5. `src/RentACar.Application/VehicleSales/VehicleSaleService.cs` — map genişlet.
   **PARA — Opus not:** `HedefFiyat` (Liste Fiyatı) vs `SatisNet` (satış fiyatı) ayrımının
   KDV/kur hesabına etkisi — bu fazda `SatisNet` KDV'lenen tutar olarak KALIR **DEĞİŞMEZ**;
   `SatisiVerildi` yalnız bir durum bayrağıdır, `VehicleSale`'in DB-immutable kaydını
   ETKİLEMEZ.
6. `src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor` — "Yeni Satış"
   formuna 8 yeni alan.
7. `src/RentACar.Web/VehicleSales/VehicleSaleEndpoints.cs` — POST handler genişlet.
8. (yeni) `src/RentACar.Application/VehicleSales/VehicleSaleFilter.cs` — Tarih aralığı,
   `SatisiVerildi`, Durum, Plaka, Ofis.
9. `src/RentACar.Application/VehicleSales/IVehicleSaleRepository.cs` (mevcut, MODİFİYE) +
   `src/RentACar.Infrastructure/Persistence/Repositories/VehicleSaleRepository.cs` (mevcut,
   MODİFİYE) — `SearchAsync`.
10. `VehicleSaleService.cs` — `SearchAsync`.
11. `VehicleSaleList.razor` — filtre formu + Kasko/Trafik poliçe özeti (`/regulasyon` join),
    İhale bilgisi, Kredi Firma kolonu (FAZ-13/17'nin alanlarına bağlı — o alanlar yoksa
    kolon boş kalır, hata vermez), Geçen Süre (`Tarih` − bugün, hesap, yeni alan gerektirmez).

### Bölüm B — baf_ara + baf_islemleri (`Baf`)
12. Yeni `src/RentACar.Domain/Enums/BafKullanimAmaci.cs` — 11 seçenek (Araç Ayırma/Dönüşü/
    Teslimatı/Yıkama vb.).
13. `src/RentACar.Domain/Entities/Baf.cs` — `KullanimAmaci` (`BafKullanimAmaci?`), `Onaylayan`
    (`Guid?`, personelId), `KirayaVer` (`bool`), `DonusSube` (`string?`, çıkış şubesinden
    AYRI), `CikisSaat`/`DonusSaat` (`TimeOnly?`).
14. `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs` (`BafConfig`)
    — yeni alanlar.
15. Migration: `dotnet ef migrations add AddBafAlanlari --project
    src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure` (Bölüm A'nın
    migration'ından AYRI dosya, aynı PR).
16. `src/RentACar.Application/Baflar/BafInput.cs` — `KullanimAmaci`, `Onaylayan`, `KirayaVer`,
    `CikisSaat` ekle (çıkış anında girilenler).
17. `src/RentACar.Application/Baflar/BafService.cs` — `CreateAsync` map genişlet;
    `TeslimAlAsync` imzasına `donusSube`/`donusSaat` parametreleri eklenir (mevcut
    `donusKm`/`donusYakit`/`donusTarihi` parametreleriyle birlikte — `donusYakit`/
    `donusTarihi` ZATEN vardı, bu fazda yalnız `donusSube`/`donusSaat` EKLENİR).
18. `src/RentACar.Web/Components/Pages/Baflar/BafList.razor` — "Yeni BAF" formuna
    `KullanimAmaci` select, `Onaylayan` personel seç, `KirayaVer` checkbox, `CikisSaat` input;
    "Teslim Al" formuna `DonusSube`/`DonusSaat` input (`DonusTarihi`/`DonusYakit` inputları
    FAZ-00'da eklenir, BU FAZ onları içermez).
19. `src/RentACar.Web/Baflar/BafEndpoints.cs` — POST handler genişlet.
20. (yeni) `src/RentACar.Application/Baflar/BafFilter.cs` — Personel/Plaka/Durum/Lokasyon
    (Aynı Ofis-Farklı Ofis — `CikisSube` vs `DonusSube` karşılaştırması)/`KullanimAmaci`/
    Tarih aralığı/Ofis.
21. `src/RentACar.Application/Baflar/IBafRepository.cs` (mevcut, MODİFİYE) +
    `src/RentACar.Infrastructure/Persistence/Repositories/BafRepository.cs` (mevcut,
    MODİFİYE) — `SearchAsync`.
22. `BafService.cs` — `SearchAsync`.
23. `BafList.razor` — filtre formu.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/VehicleSale.cs`, `Baf.cs`; (yeni) `Enums/BafKullanimAmaci.cs`
- `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs`
  (VehicleSaleConfig, BafConfig)
- (yeni) 2 migration dosyası (`AddVehicleSaleAlanlari`, `AddBafAlanlari`)
- `src/RentACar.Application/VehicleSales/VehicleSaleInput.cs`, `VehicleSaleService.cs`
- (yeni) `src/RentACar.Application/VehicleSales/VehicleSaleFilter.cs`
- `src/RentACar.Application/VehicleSales/IVehicleSaleRepository.cs` (mevcut)
- `src/RentACar.Infrastructure/Persistence/Repositories/VehicleSaleRepository.cs` (mevcut)
- `src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor`,
  `src/RentACar.Web/VehicleSales/VehicleSaleEndpoints.cs`
- `src/RentACar.Application/Baflar/BafInput.cs`, `BafService.cs`
- (yeni) `src/RentACar.Application/Baflar/BafFilter.cs`
- `src/RentACar.Application/Baflar/IBafRepository.cs` (mevcut)
- `src/RentACar.Infrastructure/Persistence/Repositories/BafRepository.cs` (mevcut)
- `src/RentACar.Web/Components/Pages/Baflar/BafList.razor`,
  `src/RentACar.Web/Baflar/BafEndpoints.cs`

## Migration
Var — `VehicleSale`/`Baf`'a additive kolonlar (2 ayrı migration, aynı PR). RLS zaten aktif →
yeni RLS bloğu gerekmez.

## Test
- `VehicleSaleServiceTests` — yeni 8 alanın round-trip testi; `SearchAsync` filtre testi
  (oracle: 3 satış seed, `SatisiVerildi=true` filtresiyle **1** sonuç); Geçen Süre hesap
  testi (sabit tarih farkı, örn. `Tarih=bugün-10gün` → `10`).
- `BafServiceTests` — yeni alanların round-trip testi; `SearchAsync` Lokasyon filtre testi
  (oracle: `CikisSube="A",DonusSube="A"` → "Aynı Ofis" sonuç kümesinde; `CikisSube="A",
  DonusSube="B"` → "Farklı Ofis" sonuç kümesinde — sabit senaryo).

## Exit
- [ ] `VehicleSale`'e 8, `Baf`'a 5 yeni alan formda görünüyor, kaydediliyor
- [ ] `/satislar` ve `/baf` filtre formları sonucu daraltıyor
- [ ] `VehicleSale`'in DB-immutable/deftere-yansımaz davranışı DEĞİŞMEDİ (regresyon testiyle
  doğrulanır)
- [ ] Tam suite yeşil

## Notlar
`HedefFiyat/SatisKm/SatisKanali/Devir` (VehicleSale) ve `DonusTarihi/DonusYakit` (Baf) form-
wiring'i **bu fazda YOK** (`FAZ-00-bedava-kazanclar.md`). Hedef-fiyat vs satış-fiyatı
ayrımının KDV/kur hesabına etkisi ve `Satisi_Verildi`'nin defter kaydını etkileyip
etkilemeyeceği **PARA — Opus**, ayrı bir takip fazı gerektirebilir.
