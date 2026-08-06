# FAZ-27 — Karşılaştırmalı Durum Analizi (Yeni Pivot Rapor)

| | |
|---|---|
| **Desen** | D4 (kataloğun 1g tabanının üstünde — sapma nedeni: çok-boyutlu pivot) |
| **Efor** | 2 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `karsilastirmali_durum_analizi.aspx` |
| **Risk** | düşük — **P&L değil**, hacim/adet karşılaştırması; D4'ün "yalnız defterden" kısıtı bu
  fazda uygulanmaz (tutar üretmiyor, ledger'a dokunmuyor) |

## Amaç
Kira/Rezervasyon hacmini periyot × kaynak-kırılımı bazında aylık kolonlu bir pivot grid'de gösteren
yeni bir rapor sayfası ekleyerek kullanıcı `/raporlar/karsilastirmali-analiz` üzerinden dönemsel
adet/gün karşılaştırması yapabilir hale gelir.

## Neden (kanıt)
`src/RentACar.Infrastructure/Persistence/OrtakSorgular.cs` içinde bu pivotu üreten bir sorgu yok;
`src/RentACar.Application/Reporting/ReportService.cs`'te mevcut ~25 rapor metodu arasında periyot×
tablo×veri-türü×kaynak-kırılımı birleşimini tek grid'de veren bir metod yok (grep ile doğrulandı —
en yakın olan `GetRezervasyonKaynakAsync` tek boyutlu, pivot değil).

## Yapılacaklar
1. `src/RentACar.Infrastructure/Persistence/OrtakSorgular.cs`'e yeni bir statik sorgu ekle
   (ör. `KarsilastirmaliAnalizAsync(AppDbContext db, Guid tenantId, int ayBaslangic, int ayBitis,
   string tablo, string veriTuru, string kirilimBoyutu, CancellationToken ct)`): Periyot (1-12 ay)
   × Tablo seçici (`Kira`=RentalContract veya `Rezervasyon`=Reservation) × VeriTürü (`Adet`/`Gün`)
   × Kaynak-kırılımı (kullanıcı seçimli TEK boyut: `RezKaynagi`|`AracGrubu`|`CikisNoktasi`) + İşlem
   Şube filtresi. Canlının serbest pivot-ızgarası yerine sabit 3 seçenekli dropdown ile sadeleştirilir.
2. `src/RentACar.Application/Reporting/ReportService.cs`'e `GetKarsilastirmaliAnalizAsync(...)`
   ekle — `OrtakSorgular`'daki yeni sorguyu çağırıp aylık kolonlu bir DTO'ya (`KarsilastirmaliAnalizDto`,
   satır=kırılım değeri, kolon=ay, hücre=adet/gün toplamı) dönüştürür.
3. Yeni `src/RentACar.Web/Components/Pages/Reports/KarsilastirmaliAnaliz.razor` — filtre formu
   (Periyot/Tablo/VeriTürü/Kırılım/İşlem Şube) + aylık kolonlu grid + `MainLayout.razor` nav girişi
   (Raporlar altına, finans-rol kapılı değil — hacim raporu, `ViewReports` yetkisi yeterli).
4. Export: mevcut `ExportTable`/`ListExportCatalog` desenine (export-paritesi kararı) bu raporu ekle
   (`KarsilastirmaliAnalizExport` ya da mevcut export kataloğuna kayıt).

## Dokunulacak dosyalar
- `src/RentACar.Infrastructure/Persistence/OrtakSorgular.cs` — yeni sorgu
- `src/RentACar.Application/Reporting/ReportService.cs` — yeni metod + DTO
- (yeni) `src/RentACar.Web/Components/Pages/Reports/KarsilastirmaliAnaliz.razor` + nav + export

## Migration
Yok — mevcut `RentalContract`/`Reservation` tablolarından okuyor, yeni tablo/kolon yok.

## Test
- Yeni `tests/RentACar.IntegrationTests/KarsilastirmaliAnalizTests.cs`: 6 kira (3 farklı araç grubu,
  2 farklı ay, elle kurulan tarih/adet) oluşturulur; Tablo=Kira, VeriTürü=Adet, Kırılım=AracGrubu
  seçilince beklenen pivot **elle hesaplanan** sabit değerlerle karşılaştırılır (bağımsız oracle —
  "3 grup × 2 ay = 6 hücre, her hücrede kurduğum sayı" — `ReportService`'ten türetilmez).
  Gün veri-türü testi: aynı senaryoda `Gün` seçilince kira gün sayıları toplamı (elle sayılan) döner.
  İşlem Şube filtresi testi: 2 şubeden kira karışık kurulur, filtre yalnız 1 şubeninkini döndürür.

## Exit
- [ ] `/raporlar/karsilastirmali-analiz` 4 filtre + aylık kolonlu grid gösteriyor
- [ ] Kira ve Rezervasyon tablo seçimleri ayrı ayrı doğru veri döndürüyor
- [ ] Export çalışıyor
- [ ] Tam suite yeşil

## Notlar
Bu bir hacim/adet raporu — **tutar üretmiyor**, dolayısıyla D4'ün "yalnız defterden" kuralı burada
uygulanmaz (kaynak-varlık miktarları RentalContract/Reservation'dan doğrudan sayılır, çift-sayım
riski parasal değil).
