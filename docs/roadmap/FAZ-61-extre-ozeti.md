# FAZ-61 — Extre Özeti (yeni ekran, satır seviyesi)

| | |
|---|---|
| **Desen** | D4 |
| **Efor** | 1,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `extre_ozeti.aspx` |
| **Risk** | düşük — salt-okunur yeni rapor, para yazmıyor |

## Amaç
Kullanıcı `/raporlar/extre-ozeti` sayfasında müşteri+plaka+vade bazlı açık-tutar satırlarını
görebilir hale gelir — mevcut `/raporlar/cari-bakiye` (toplu bakiye) ile KARIŞTIRILMAZ, bu SATIR
seviyesi bir görünümdür.

## Neden (kanıt)
Canlı `extre_ozeti.aspx` müşteri+plaka+vade bazlı açık-tutar (Invoice.VadeTarihi + Rental.VehicleId
JOIN) satırları gösteriyor; bizde bu JOIN'i yapan bir sorgu/sayfa yok (mevcut `CariBakiye.razor`
toplu-bakiye seviyesinde, satır seviyesinde değil).

## Yapılacaklar
1. `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`'e yeni
   `GetExtreOzetiRowsAsync(from, to, ct)` metodu: `Invoice` (vade geçmemiş/açık) × `Rental`
   (`VehicleId` üzerinden plaka) JOIN'i, müşteri+plaka+vade satırları.
2. `src/RentACar.Application/Reporting/ReportService.cs`'e `GetExtreOzetiAsync` metodu.
3. Yeni `src/RentACar.Web/Components/Pages/Reports/ExtreOzeti.razor` (rota:
   `/raporlar/extre-ozeti`) + filtre (Ofis, Tarih).
4. `MainLayout.razor`'a nav eklenir.

## Dokunulacak dosyalar
- `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs` (yeni
  `GetExtreOzetiRowsAsync`)
- `src/RentACar.Application/Reporting/ReportService.cs` (yeni `GetExtreOzetiAsync`)
- (yeni) `src/RentACar.Web/Components/Pages/Reports/ExtreOzeti.razor`
- `src/RentACar.Web/Components/Layout/MainLayout.razor` (nav)

## Migration
Yok — mevcut `Invoice`/`Rental` tabloları okunuyor, yeni tablo yok.

## Test
- (yeni) `ExtreOzetiTests.cs`: elle 3 fatura (farklı vade, farklı cari/plaka) oluşturulur;
  `GetExtreOzetiAsync` her satırın müşteri/plaka/vade/açık-tutar kombinasyonunu doğru döndürüyor mu
  (bağımsız oracle: beklenen açık-tutar test içinde elle hesaplanır, servis kodundan türetilmez).
  `/raporlar/cari-bakiye` (toplu) ile KARIŞTIRILMADIĞI (aynı toplamı vermediği, satır seviyesinde
  kaldığı) ayrıca doğrulanır.

## Exit
- [ ] `/raporlar/extre-ozeti` sayfası çalışıyor
- [ ] Ofis/Tarih filtresi çalışıyor
- [ ] Satır seviyesi görünüm `/raporlar/cari-bakiye` ile karıştırılmıyor (ayrı doğrulama testiyle)
- [ ] Tam suite yeşil

## Notlar
Yok.
