# FAZ-12 — Araç Durum/Günlük Durum/Gelir-Gider Raporları Derinliği

| | |
|---|---|
| **Desen** | D4 — rapor (agrega) |
| **Efor** | 3,5 gün (1 + 1 + 1,5) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `arac_durum_takip.aspx`, `arac_gunluk_durum.aspx`, `arac_gelir_gider_tablosu.aspx` |
| **Risk** | orta — Bölüm A/B'de yapısal eylem net; Bölüm C'nin GERÇEK pivot değişikliği Opus kararına kadar kod yazılmaz (bkz. Notlar) |

## Amaç
`/raporlar/arac-durum-takip` ARAÇ bazlı (Plaka/SIPP satır) görünüme geçebilir ve Baf-Gün kovasını
sayar; yeni `/raporlar/arac-gunluk-durum` sayfası o günkü araç-bazlı günlük gelir kesitini
gösterir; Karlılık/FiloAnaliz/EkHizmetRaporu üçlüsünün canlının 5-modlu export'una göre hangi
yapısal seçenekle (A/B) birleştirileceği kararı hazırlanır.

## Neden (kanıt)
- `arac_durum_takip.aspx`: K2=%18 (2/11), K3=%43 (3/7). Grain farklı — canlı satır=ARAÇ
  (seçilen aralıkta toplam Dolu-Gün/Bakım-Gün/Baf-Gün/Boş-Gün/Potansiyel); bizde satır=GÜN.
  **Baf Gün kovası bizde hiç yok** (Dolu/Bakım/Boş üçlü; Baf hiç sayılmıyor, sessizce kayboluyor).
- `arac_gunluk_durum.aspx`: K2=%0 (0/6 — canlıda tarih aralığı YOK, o gün/o an içindir), K3=%17
  (2/12). Canlı ARAÇ × GÜN bazında Günlük Kira/Günlük Hizmet/Günlük Toplam gösteriyor; bizdeki
  hiçbir rapor bu per-araç-per-gün gelir kesitini üretmiyor (Karlılık dönem TOPLAMI verir).
- `arac_gelir_gider_tablosu.aspx`: K2=%25 (2/8), K3=%14 (9/64). Canlı 5 farklı Excel-export
  modu (Şube/Grup/Araç/Detay/Hizmet); bizde 3 ayrı rapora bölünmüş (Karlılık boyut=sube/grup/
  araç, FiloAnaliz, EkHizmetRaporu), hiçbiri ~25 ek-hizmet-kolonunu (CDW, LCF, Genç Sürücü…)
  tek satırda araç bazında göstermiyor.

## Yapılacaklar

### Bölüm A — arac_durum_takip
1. `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs` — yeni
   `GetAracDurumTakipAracBazliRowsAsync`: `kiralar`/`servisler` sorgusuna `Baflar` dahil et,
   `VehicleId`'ye göre grupla, seçilen aralıktaki Dolu-Gün/Bakım-Gün/Baf-Gün/Boş-Gün hesapla.
2. `src/RentACar.Application/Reporting/ReportDtos.cs` — yeni `AracDurumTakipAracRow` record.
3. `src/RentACar.Application/Reporting/IReportRepository.cs`,
   `src/RentACar.Application/Reporting/ReportService.cs` — imzaları genişlet.
4. `src/RentACar.Web/Components/Pages/Reports/AracDurumTakip.razor` — "Gün" ↔ "Araç" görünüm
   anahtarı (arac_listesi'ndeki `gorunum=grup` desenindeki gibi) + Ofis/AracSahibi/Grup
   (SIPP↔Grup toggle)/Plaka filtreleri.
5. **Bu fazın kapsamı DIŞINDA:** "Potansiyel" (boş-gün × günlük ücret) kolonu — hangi günlük
   ücretin (grup ortalaması mı, o aracın son kirası mı) kullanılacağı **PARA — Opus** kararı;
   bu fazda EKLENMEZ.

### Bölüm B — arac_gunluk_durum (yeni sayfa)
6. `src/RentACar.Application/Reporting/ReportDtos.cs` — yeni `AracGunlukDurumRow` record.
7. `IReportRepository.cs`/`ReportService.cs` — yeni `GetAracGunlukDurumAsync`.
8. `ReportRepository.cs` — o gün aktif (`BasTar<=gün<=Bit`) her kiranın `GenelToplam`'ını kira
   süresine (BasTar..Bit arası gün sayısı) düz bölen, `VehicleId` bazında gruplayan sorgu.
   **PARA — Opus not:** bölüştürme yöntemi (düz bölüm mü, ek hizmet günlükleri dahil mi, kısmi
   gün nasıl sayılır) formül kararıdır — bu fazda EN BASİT yöntem (toplam gün sayısına düz
   bölüm) GEÇİCİ olarak uygulanır; Opus onayı sonrası formül değişebilir, mevcut kayıtlı rapor
   sonucu tekrar hesaplanan bir projeksiyondur (kalıcı postlama YOK).
9. Yeni `src/RentACar.Web/Components/Pages/Reports/AracGunlukDurum.razor`
   (`/raporlar/arac-gunluk-durum`): Plaka/Araç Grubu filtreleri + `MainLayout.razor` nav.

### Bölüm C — arac_gelir_gider_tablosu (hazırlık — kod değişikliği Opus kararına kadar YOK)
10. Karar gerekiyor (kod adımı değil): Seçenek A (üçünü TEK sayfada sekmeli export moduna
    indir — mevcut `KarlilikSatirDto` + `EkHizmetTanim` pivotunu ARAÇ eksenine çevirerek
    birleştir) ya da Seçenek B (`EkHizmetRaporu`'na araç-bazlı pivot modu ekle — bugün
    hizmet-adı bazlı pivotluyor). İkisi de kod değişikliği gerektirir; hangisinin canlının
    GERÇEK dengi sayılacağı ve ~25 ek-hizmet kolonunun (CDW/LCF/Genç Sürücü/Bebek Koltuğu/
    Wifi/Kış Lastiği/Paket1-6/SCDW…) tek satırda nasıl gösterileceği **muhasebe/para
    uzmanlığı gerektiriyor** → Opus'a devredilir.
11. `src/RentACar.Web/Components/Pages/Reports/Karlilik.razor`,
    `FiloAnaliz.razor`, `EkHizmetRaporu.razor` — karar netleşene kadar kod DEĞİŞMEZ; bu fazda
    yalnız yukarıdaki karar notu bir teknik-borç işareti olarak (kod yorumu değil, bu belge
    üzerinden) takip edilir.

## Dokunulacak dosyalar
- `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
- `src/RentACar.Application/Reporting/ReportDtos.cs`, `IReportRepository.cs`, `ReportService.cs`
- `src/RentACar.Web/Components/Pages/Reports/AracDurumTakip.razor`
- (yeni) `src/RentACar.Web/Components/Pages/Reports/AracGunlukDurum.razor`
- `src/RentACar.Web/Components/MainLayout.razor` (nav)
- Bölüm C: **dokunulmaz** bu fazda (bkz. Yapılacaklar #10-11)

## Migration
Yok — rapor sorguları, yeni tablo yok.

## Test
- `ReportServiceTests` — `GetAracDurumTakipAracBazliRowsAsync`: bağımsız oracle — 1 araç için
  elle 3 gün kira + 1 gün baf + 2 gün boş seed edilir; beklenen `DoluGun=3`/`BafGun=1`/
  `BosGun=2` SABİTLERİ teste yazılır (koddan türetilmez).
- `GetAracGunlukDurumAsync`: 1 kira (3 gün, `GenelToplam=300`) seed edilir; beklenen günlük pay
  `100` (`300/3`, elle hesaplanmış sabit) teste yazılır.
- Bölüm C için test YOK (kod değişikliği yok).

## Exit
- [ ] `/raporlar/arac-durum-takip` Araç görünümünde Baf-Gün kovası doğru sayıyor
- [ ] `/raporlar/arac-gunluk-durum` sayfası erişilebilir, nav'da, doğru günlük pay hesaplıyor
- [ ] Karlılık/FiloAnaliz/EkHizmetRaporu davranışı bu fazda DEĞİŞMEDİ (regresyon testiyle
  doğrulanır)
- [ ] Tam suite yeşil

## Notlar
Bölüm C'nin yapısal seçeneği (A/B) netleşmeden kod yazılmaz — bu, plan dosyasının kendi notuyla
tutarlı ("hangisi canlının GERÇEK dengi sayılacağı muhasebe uzmanlığı gerektiriyor"). Opus kararı
geldiğinde bu faz tamamlanmış sayılır ama Bölüm C ayrı bir takip fazı gerektirir (bu döküman
kapsamında numaralandırılmamıştır — 10-19 aralığı tükendiği için ayrı bir faz numarası isteyecek).
"Potansiyel" kolonu da aynı şekilde ayrı bir küçük takip gerektirir.
