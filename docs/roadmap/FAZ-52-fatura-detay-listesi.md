# FAZ-52 — Fatura Detay Listesi (yeni ekran)

| | |
|---|---|
| **Desen** | D3 (veri var, görünmüyor — normalin üstü efor, çok-tablo JOIN) |
| **Efor** | 1,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `fatura_detay_listesi.aspx` |
| **Risk** | düşük — salt-okunur rapor, yeni tablo yok, para yazmıyor |

## Amaç
Kullanıcı `/faturalar/detay-listesi` sayfasında fatura SATIRI seviyesinde (her satır bir grid
kaydı) Cari/Plaka/Kira/Rezervasyon bilgisiyle birleştirilmiş görünümü görüp filtreleyebilir/export
edebilir hale gelir.

## Neden (kanıt)
`InvoiceLine` (`src/RentACar.Domain/Entities/Invoice.cs` L73-87) zaten `Aciklama`/`Miktar`/
`BirimNetFiyat`/`KdvOrani`/`SatirNet`/`SatirKdv`/`SatirToplam` tutuyor ama bunları satır bazında
Customer/Rental/Reservation ile JOIN'leyip gösteren bir ekran yok — canlının 46 kolonlu
`fatura_detay_listesi.aspx`'i (Cari Ünvan/Adres/Şehir/Mail, Evrak No/Tarih, Plaka, Kira Çıkış
Ofisi/Detayı/RA No, Rezervasyon Kaynağı, Satır Net/KDV/Toplam, İptal, Vade Tarihi) karşılığı yok.

## Yapılacaklar
1. `src/RentACar.Application/Finance/InvoiceService.cs`'e `ListLinesAsync` benzeri filtreli metod
   eklenir: `InvoiceLine` × `Invoice` × `Customer` × `Rental`/`Reservation` JOIN'i (Invoice.RentalId
   üzerinden Rental'a, Rental.ReservationId üzerinden Reservation'a).
2. Yeni `src/RentACar.Web/Components/Pages/Finance/InvoiceLineList.razor` (rota:
   `/faturalar/detay-listesi`) — satır-bazlı grid. Filtre: Cari, Fatura No aralığı, Plaka, Tarih,
   Ofis.
3. `src/RentACar.Web/Reports/ListExportCatalog.cs`'e yeni `FaturaDetaylari` metodu (mevcut
   `ExportTable` deseniyle, test-edilebilir).
4. `src/RentACar.Web/Components/Layout/MainLayout.razor`'a nav eklenir.

## Dokunulacak dosyalar
- (yeni) `src/RentACar.Web/Components/Pages/Finance/InvoiceLineList.razor`
- `src/RentACar.Application/Finance/InvoiceService.cs` (yeni `ListLinesAsync` metodu)
- `src/RentACar.Web/Reports/ListExportCatalog.cs` (yeni `FaturaDetaylari`)
- `src/RentACar.Web/Components/Layout/MainLayout.razor` (nav)

## Migration
Yok — mevcut `InvoiceLine`/`Invoice`/`Customer`/`Rental` tabloları okunuyor, yeni tablo yok.

## Test
- (yeni) `InvoiceLineListTests.cs`: elle 2 fatura (farklı cari, farklı plaka, 3 satır) oluşturulur;
  `ListLinesAsync` filtresiz çağrıldığında 3 satır dönüyor mu (bağımsız oracle: satır sayısı test
  içinde sabit, servis kodundan türetilmez); Cari filtresiyle yalnız o cariye ait satırlar dönüyor mu.
- `FaturaDetaylari` export: `ListExportCatalog`'un mevcut export-test desenine (ör.
  `ExportTable` round-trip) uyan bir senaryo — kolon başlıkları + satır sayısı sabit beklenen değerle
  karşılaştırılır.

## Exit
- [ ] `/faturalar/detay-listesi` sayfası satır-bazlı grid gösteriyor
- [ ] Cari/Fatura No/Plaka/Tarih/Ofis filtresi çalışıyor
- [ ] Export ucu çalışıyor
- [ ] Tam suite yeşil

## Notlar
Yok.
