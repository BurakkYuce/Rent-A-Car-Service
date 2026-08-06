# FAZ-53 — Fatura Raporları Derinliği (Dönem + KDV)

| | |
|---|---|
| **Desen** | D4 (rapor genişletme) |
| **Efor** | 3,5 gün (1,5 gün fatura dönem raporu + 2 gün KDV raporu) |
| **Bağımlılık** | KDV raporunun Alış-KDV kısmı FAZ-55'e (`gelen_e_fatura_listesi.aspx`-a, KDV oran
  kırılımı) bağımlı — bkz. Yapılacaklar madde 2. Fatura dönem raporu kısmı bağımsız, önce yapılabilir. |
| **Kapsanan canlı ekran** | `fatura_donem_raporu.aspx`, `kdv_raporu.aspx` |
| **Risk** | düşük — salt-okunur rapor genişletmesi, para yazmıyor |

## Amaç
Kullanıcı fatura dönem raporunda "faturalanmamış kira" sekmesini görüp hangi kiraların henüz
faturalanmadığını listeleyebilir; KDV raporunda canlıdaki SATIR=fatura/sütun=oran geniş formatı
(mevcut özet/pivot formatın yanında yeni bir görünüm olarak) + Alış (gelen e-Fatura) KDV'sini de
dahil edebilir hale gelir.

## Neden (kanıt)
`ReportRepository.GetFaturaDonemRowsAsync` (`src/RentACar.Infrastructure/Persistence/Repositories/
ReportRepository.cs` L197-219) şu an yalnız kesilmiş `Invoice`'ları tarih aralığına göre listeliyor
— "faturalanmamış kira" (Rental LEFT JOIN Invoice, eşleşme yoksa) modu yok.

`ReportRepository.GetKdvLineRowsAsync` (L320-343) yalnız SATIŞ (`Invoice`/`InvoiceLine`) tablosunu
tarıyor — ALIŞ (`GelenEFatura`) hiç dahil değil (grep doğrulandı: metod içinde `db.GelenEFaturalar`
referansı yok). Canlı `kdv_raporu.aspx` SATIR=fatura/sütun=oran (geniş format) + Satış/Alış
(`Fatura_Turu`) ayrımı taşıyor; bizim mevcut özet/pivot format (`ReportService.GetKdvListesiAsync`,
L539) canlıda yok ama işe yarıyor — SİLİNMEZ, yeni bir sekme/mod eklenir.

## Yapılacaklar
1. `ReportRepository.GetFaturaDonemRowsAsync`'e "faturalanmamış kira" modu eklenir: `Rental` tablosu
   `LEFT JOIN Invoice` (`RentalId` eşleşmeyen + `KaynakKiraId` fark faturası da yoksa) filtresiyle
   "Fatura_Durum=Faturalanmamış" sekmesi. Kolon: `Kayit No` (`Rental.SozlesmeNo`), `Faturalanan`
   (bool), `Plaka`, Baş/Bit Tarih. Filtre: Cari Ara, `Fatura_Durum` (Faturalanmamış/Faturalanmış),
   İşlem Şube.
2. `ReportRepository.GetKdvLineRowsAsync`'e ALIŞ (`GelenEFatura`) KDV'si dahil edilir — **ANCAK**
   `GelenEFatura`'da şu an oran kırılımı yok (bkz. FAZ-55'in (a) kısmı: `Kdv20`/`Kdv10`/`Kdv1`/`Kdv0`
   alanları henüz eklenmedi). **FAZ-55 (a) BİTMEDEN bu madde uygulanamaz** — sıra: önce FAZ-55,
   sonra bu maddenin Alış-kısmı.
3. Yeni bir satır-bazlı geniş format görünüm eklenir (her fatura bir satır, oran sütunlarına
   dağıtılmış Tutar/KDV) — MEVCUT özet/pivot format SİLİNMEZ, yeni bir sekme/mod olur.
4. `ReportService.GetFaturaDonemAsync` (L708), `ReportService.GetKdvListesiAsync` (L539) yeni
   parametreleri (`fatura_durum` filtresi, `dahilAlis: bool`) alacak şekilde genişler.
5. `Components/Pages/Reports/FaturaDonem.razor`, `Components/Pages/Reports/KdvListesi.razor` filtre
   paneli + yeni sekme/mod UI'sı eklenir.

## Dokunulacak dosyalar
- `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
  (`GetFaturaDonemRowsAsync` ~L197, `GetKdvLineRowsAsync` ~L320)
- `src/RentACar.Application/Reporting/ReportService.cs` (`GetFaturaDonemAsync` ~L708,
  `GetKdvListesiAsync` ~L539)
- `src/RentACar.Web/Components/Pages/Reports/FaturaDonem.razor`
- `src/RentACar.Web/Components/Pages/Reports/KdvListesi.razor`

## Migration
Yok — mevcut `Invoice`/`InvoiceLine`/`Rental`/`GelenEFatura` tabloları okunuyor (Alış-KDV kısmı için
`GelenEFatura`'nın oran-kırılım kolonları FAZ-55'te eklenecek, o fazın migration'ına dahil).

## Test
- `fatura_donem_raporu` için (yeni senaryo, mevcut rapor test dosyasına ek): elle 3 kira (1 faturalı,
  1 fark-faturalı `KaynakKiraId`, 1 faturasız) oluşturulur; "Faturalanmamış" filtresi yalnız 3.
  kirayı döndürüyor mu (bağımsız oracle: beklenen sayı=1, test içinde sabit).
- `kdv_raporu` geniş format: elle 2 fatura (farklı KDV oranı, %20 ve %10) oluşturulur; geniş format
  satırında her faturanın doğru oran sütununa dağıldığı doğrulanır (elle hesaplanan tutar).
- Alış-KDV dahil etme (FAZ-55 bitince): elle 1 `GelenEFatura` (oran kırılımlı) + 1 satış faturası
  oluşturulur; toplam KDV = satış KDV + alış KDV (indirilecek) mi (elle toplam, koddan türetilmez).

## Exit
- [ ] Fatura dönem raporunda "Faturalanmamış kira" sekmesi çalışıyor
- [ ] KDV raporunda yeni geniş-format görünüm MEVCUT pivot format YANINDA duruyor (regresyon yok)
- [ ] Alış-KDV (GelenEFatura) dahil edilmesi FAZ-55 sonrası tamamlanmış
- [ ] Tam suite yeşil

## Notlar
KDV raporunun Alış-kısmı bu faz İÇİNDE başlatılabilir ama TAMAMLANAMAZ — FAZ-55'in oran-kırılım
alanları bitmeden Alış-KDV dahil etme kodu derlenip test edilemez. Önerilen sıra: bu fazın (1)+(3)
maddesi önce, (2) maddesi FAZ-55'ten sonra ayrı bir küçük takip-commit'i olarak.
