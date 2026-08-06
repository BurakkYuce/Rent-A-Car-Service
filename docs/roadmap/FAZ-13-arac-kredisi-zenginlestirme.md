# FAZ-13 — Araç Kredisi Zenginleştirme

| | |
|---|---|
| **Desen** | D2 (kredi özet alanları) + D3 (filtre/arama) — cari-bağlama modeli/faiz formülü **PARA — Opus'a devredilir** |
| **Efor** | 2 gün (1,5 + 0,5) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `arac_kredi.aspx`, `arac_kredi_listesi.aspx` |
| **Risk** | orta — `AracKredi.TaksitOdeAsync` ZATEN gerçek gider+defter kaydı postluyor (`AracKrediRepository.cs`); bu fazda `Hesapla()`'ya yeni ÖZET alanlar eklenir ama mevcut faiz formülü DEĞİŞMEZ |

## Amaç
Kullanıcı krediyi bir **Cari**ye bağlayabilir, özet kartlarını (Toplam Faiz/Son Vade Günü/Son
Taksit Tutarı/Bu Ay Toplam Taksit/Toplam Kredi Borcu) görür, taksitleri toplu iptal edebilir ve
`/arac-kredi` listesini Cari/Plaka/Tarih aralığıyla filtreleyebilir.

**Not — wire-in DIŞARIDA:** `AracKredi.VehicleId` zaten `AracKrediModels.cs`/`AracKrediService.cs`
içinde var; "Yeni Kredi" formuna araç seçici eklemek `docs/roadmap/FAZ-00-bedava-kazanclar.md`
kapsamındadır — **bu fazda o adım YOK**.

## Neden (kanıt)
- `arac_kredi.aspx`: K2=%20 (3/15). Kredi bir **Cari**ye bağlı (Musteri_No/Ad); bizde sadece
  özgür-metin `BankaAdi`. `Faiz_Toplam`, `Son_Vade_Gunu`, `Son_Taksit_Tutari`,
  `Bu_Ay_Toplam_Taksit`, `Toplam_Kredi_Borcu` özet kartları yok. "Taksitleri İptal Et" toplu
  aksiyonu yok.
- `arac_kredi_listesi.aspx`: K2=%17 (1/6), K3=%40 (2/5). `AracKrediList.razor`'da hiç filtre
  formu yok (`OnInitializedAsync` düz `ListAsync()`); Cari/Plaka/Tarih aralığı araması ve
  Dosya No kolonu yok.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/AracKredi.cs` — `CariId` (`Guid?`, additive — eski `BankaAdi`
   serbest-metin alanı KALIR) ekle.
2. `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs` (`AracKredi` config, bu dosyada tanımlı) —
   `CariId` için composite tenant-FK (Cari tablosuna, şube deseniyle aynı) + index.
3. Migration: `dotnet ef migrations add AddAracKrediCariId --project
   src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
4. `src/RentACar.Application/AracKredileri/AracKrediModels.cs` — `AracKrediInput.CariId` ekle;
   `AracKrediOzet` record'a `ToplamFaiz`, `SonVadeGunu`, `SonTaksitTutari`,
   `BuAyToplamTaksit`, `ToplamKrediBorcu` alanları ekle.
5. `src/RentACar.Application/AracKredileri/AracKrediService.cs` — `Hesapla()` metodunu bu 5
   özet alanı üretecek şekilde genişlet (mevcut yıllık basit faiz/eşit taksit varsayımı
   **DEĞİŞMEZ**, sadece özet alanlar türetilir).
6. `src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor` — Cari arama/seç
   input'u ("Yeni Kredi" formuna); 5 özet kart; "Taksitleri İptal Et" toplu aksiyonu (ana
   formun DIŞINDA, `form=` attribute'lü mini-form).
7. `src/RentACar.Web/AracKredileri/AracKrediEndpoints.cs` — taksit toplu iptal için POST ucu.
8. (yeni) `src/RentACar.Application/AracKredileri/AracKrediFilter.cs` — Cari/Plaka/Tarih
   aralığı alanları.
9. `src/RentACar.Application/AracKredileri/IAracKrediRepository.cs` (mevcut, MODİFİYE) +
   `src/RentACar.Infrastructure/Persistence/Repositories/AracKrediRepository.cs` (mevcut,
   MODİFİYE) — `SearchAsync` eklenir (repository dosyaları ZATEN var, yeni dosya DEĞİL).
10. `AracKrediService.cs` — `SearchAsync` metodu.
11. `AracKrediList.razor` — filtre formu + Dosya No kolonu.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/AracKredi.cs` — `CariId`
- `src/RentACar.Infrastructure/Persistence/Configurations/VehicleConfigs.cs` (AracKredi config)
- (yeni) migration dosyası (`AddAracKrediCariId`)
- `src/RentACar.Application/AracKredileri/AracKrediModels.cs`, `AracKrediService.cs`
- (yeni) `src/RentACar.Application/AracKredileri/AracKrediFilter.cs`
- `src/RentACar.Application/AracKredileri/IAracKrediRepository.cs` (mevcut)
- `src/RentACar.Infrastructure/Persistence/Repositories/AracKrediRepository.cs` (mevcut)
- `src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor`
- `src/RentACar.Web/AracKredileri/AracKrediEndpoints.cs`

## Migration
Var — mevcut `AracKrediler` tablosuna 1 nullable kolon (`CariId`). RLS zaten aktif → yeni RLS
bloğu gerekmez.

## Test
- `AracKrediServiceTests` — `Hesapla()` özet alanları: bağımsız oracle (12.000 TL kredi, %10
  yıllık faiz, 12 taksit → `ToplamFaiz=1200` elle hesaplanıp SABİT olarak teste yazılır, koddan
  türetilmez).
- `SearchAsync` filtre testi: 3 kredi seed (1'i `CariId=X`), `CariId=X` filtresiyle **1** sonuç
  bekleniyor.
- Tenant izolasyon: `racar_app` ile 2. tenant'ın kredisi görünmemeli (mevcut desen).

## Exit
- [ ] `CariId` migration'la eklendi, `AracKrediList` Cari arama gösteriyor
- [ ] 5 özet kart oracle-doğrulanmış formülle doğru değer gösteriyor
- [ ] "Taksitleri İptal Et" toplu aksiyonu çalışıyor
- [ ] Cari/Plaka/Tarih filtre formu sonucu daraltıyor
- [ ] Tam suite yeşil

## Notlar
`VehicleId` araç-seçici wire-in'i **bu fazda YOK** (`FAZ-00-bedava-kazanclar.md`). Cari-bağlama
modelinin (kredi VEREN cari mi, ilişkili cari mi) ve faiz/vade formülünün canlıyla uyuşup
uyuşmadığı **PARA — Opus**; bu faz yalnız altyapıyı (CariId kolonu + özet alan türetme +
filtre) kurar, formülün doğruluğunu garanti ETMEZ. `TaksitOdeAsync` zaten gerçek gider+defter
postluyor — `Hesapla()`'daki formül değişikliği YENİ taksit hesaplarını etkiler, geçmiş
postlanmış kayıtları ETKİLEMEZ (yalnızca ileriye dönük).
