# FAZ-78 — Ek Hizmet Raporu Derinliği (Satır-Bazlı Detay + Personel Atfı)

| | |
|---|---|
| **Desen** | D3 |
| **Efor** | 1,5 gün |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `extralar_raporu.aspx` |
| **Risk** | düşük — additive kolon + yeni ham-satır sorgusu; mevcut özet API korunur |

## Amaç
`/raporlar/ek-hizmet` (bugün Ad'a göre TOPLU özet) satır-bazlı detay listesine genişler: her ek
hizmet satırı kendi Kayıt No/tarih/plaka/müşteri/kaynak/ofis/ilk-tahsilat bilgisiyle görünür + kim
sattığı (personel) izlenir.

## Neden (kanıt)
- `ReportService.GetEkHizmetRaporuAsync(from?, to?, ct)` (doğrulandı) → `_repository.
  GetEkHizmetSalesRowsAsync(from, to, ct)` çağırır, sonucu `r.Ad`'a **GroupBy** yapar (doğrulandı) →
  `EkHizmetRaporDto(Satirlar: EkHizmetRaporRowDto[Ad, ToplamMiktar, Net, Kdv, Brut, KiraAdet], ...)`
  — TOPLU özet, satır-bazlı DEĞİL.
- Ham sorgu `GetEkHizmetSalesRowsAsync` (`ReportRepository.cs:345-360`, doğrulandı):
  `db.RentalAddOns.Where(RentalId ∈ aktifKiraIds).Where(CreatedAtUtc aralığı)` →
  `{Ad, Miktar, NetTutar, KdvTutar, Toplam, RentalId}` — **VehicleId/Plaka JOIN YOK**, Kayıt No/
  Müşteri/Rez.Kaynağı/Ç.Ofisi/İlk Tahsilat YOK.
- `RentalAddOn.cs` tüm alanları doğrulandı: `Id, TenantId, RentalId, EkHizmetTanimId, Ad, Miktar,
  BirimNetFiyat, KdvOrani, NetTutar, KdvTutar, Toplam, CreatedAtUtc, UpdatedAtUtc` — **`PersonelId`
  alanı YOK** (grep doğrulandı, sıfır eşleşme) — "Kiraya Veren/Teslim Eden/Ek Hizmet Satan" bugün
  hiç izlenmiyor.
- "Döviz/TL Fiyat" notu: `RentalAddOn` tutarları zaten TRY-baz saklanıyor (Kur alanı yok, tasarım
  kararı) — canlının döviz gösterimini birebir taklit etmek bu PR kapsamında DEĞİL, "TL Fiyat"
  zaten karşılanıyor (`Toplam` alanı).

## Yapılacaklar
1. `src/RentACar.Domain/Entities/RentalAddOn.cs` — additive `PersonelId (Guid?)` kolonu ("satan
   personel" — kiraya veren/teslim eden/ek hizmet satan senaryolarının hangisi olduğu ayrı bir alan
   DEĞİL, tek "satan personel" referansı; canlının 3 ayrı rolü tek personel alanına sadeleştirilir,
   not olarak belgelenmiş şekilde).
2. `src/RentACar.Application/Reporting/ReportDtos.cs`'e yeni `EkHizmetDetayRow(Guid RentalId,
   string KayitNo, DateTimeOffset Bas, DateTimeOffset? Bit, string Plaka, string Musteri, string?
   RezKaynagi, string? CikisOfisi, decimal IlkTahsilat, string Ad, decimal Miktar, decimal Net,
   decimal Kdv, decimal Brut, string? SatanPersonel)` DTO.
3. `ReportRepository.cs`'e **YENİ** gruplamasız-ham-satır metodu `GetEkHizmetDetayRowsAsync(from?,
   to?, rezKaynagi?, sube?, filtreTuru? [Kira/Rezervasyon], ct)` — `RentalAddOn` → `Rental`
   (`SozlesmeNo`, `MusteriId`→Customer, `CikisOfisi`) → `Vehicle` (Plaka) JOIN'i + Personel JOIN
   (adım 1'deki `PersonelId`) + İlk Tahsilat (`CashTransaction`/`Invoice` üzerinden en-erken
   ödeme — Araç Karnesi'ndeki "ilk tahsilat" atıf desenine benzer). **Mevcut
   `GetEkHizmetSalesRowsAsync` (özet) API'si KORUNUR**, değiştirilmez.
4. `ReportService.cs`'e `GetEkHizmetDetayAsync(...)` — repo metodunu sarar.
5. `EkHizmetRaporu.razor` — mevcut Ad-bazlı özet tablo ÜSTTE kalır; ALTTA/sekme değişiminde
   satır-bazlı detay listesi (yukarıdaki kolonlar) + Rapor_Türü (Kira/Rezervasyon)/İçerik/Kime_Ait/
   İşlem_Şube filtre formu eklenir.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/RentalAddOn.cs` — additive `PersonelId`
- `src/RentACar.Application/Reporting/ReportDtos.cs`
- `src/RentACar.Application/Reporting/ReportService.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
- `src/RentACar.Web/Components/Pages/Reports/EkHizmetRaporu.razor`

## Migration
`RentalAddOn`'a additive `PersonelId (uuid NULL)` kolonu (FK → `Users.Id` veya `Personel.Id`,
repodaki personel referans desenine göre) — tablo zaten RLS'li, RLS bloğu gerektirmez.

## Test
- `ReportRepositoryTests`: elle kurulan 3 `RentalAddOn` (farklı kira/plaka/personel) →
  `GetEkHizmetDetayRowsAsync` her satırı doğru JOIN'lenmiş alanlarla (Plaka/KayıtNo/SatanPersonel)
  döndürür — bağımsız oracle, sabit test verisi.
- Regresyon: `GetEkHizmetRaporuAsync` (mevcut özet) AYNI test verisiyle bu fazdan ÖNCE/SONRA aynı
  `ToplamNet`/`ToplamKdv`/`ToplamBrut` değerlerini döndürür (API değişmedi kanıtı).
- Filtre testi: Rapor_Türü=Rezervasyon seçilince yalnız rezervasyon-kaynaklı satırlar (varsa ayrı
  kaynak) döner; İşlem_Şube filtresi doğru alt-kümeyi verir.

## Exit
- [ ] Satır-bazlı detay listesi çalışıyor, mevcut özet API bozulmadı
- [ ] `PersonelId` CRUD'da (kira ek-hizmet ekleme formunda) doldurulabiliyor
- [ ] Tam suite yeşil

## Notlar
"Kiraya Veren/Teslim Eden/Ek Hizmet Satan" canlıda 3 ayrı rol olabilir; bu faz TEK `PersonelId`
alanına sadeleştirir (repo genelinde personel-rol ayrımı yapan başka bir yapı yok) — ileride 3 ayrı
alan gerekirse additive genişleme kolaydır, bu fazda YAPILMAZ.
