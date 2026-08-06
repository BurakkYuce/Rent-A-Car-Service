# FAZ-00 — Bedava Kazançlar (wire-in borcu)

| | |
|---|---|
| **Desen** | D3 — veri var, ekrana bağlanmamış |
| **Efor** | ~0,5 gün (saatler) |
| **Bağımlılık** | yok — her şeyden önce yapılabilir |
| **Kapsanan canlı ekran** | `arac_kredi.aspx`, `arac_satis.aspx`, `baf_islemleri.aspx` |
| **Risk** | düşük — migration yok, servis değişikliği yok |

## Amaç

Zaten var olan ama formların hiç sormadığı alanları bağlamak. Kod, entity, input modeli ve servis
tarafı **hazır**; eksik olan tek şey form alanı. Migration gerekmez.

## Neden (kanıt)

Parite taraması "wire-in eksikliği" kalıbını üç yerde ölçtü — üçü de kodda doğrulandı:

| Alan | Nerede var | Formda |
|---|---|---|
| `AracKredi.VehicleId` | `AracKrediModels.cs:7` (input), `AracKrediService.cs:39` (map) | **yok** |
| `VehicleSale.HedefFiyat / SatisKm / SatisKanali / Devir` | `VehicleSale.cs:36-39` | **dördü de yok** |
| `Baf.DonusTarihi / DonusYakit` | `Baf.cs:26,28` | yalnız `donusKm` soruluyor |

### Bunlardan biri kozmetik değil — para atfını bozuyor

`AracKrediService.cs:100` kredi taksitinin **gider bacağını** şöyle yazıyor:

```csharp
new AccountLedgerEntry { AccountType = LedgerAccountType.Gider, AccountRef = kredi.VehicleId, … }
```

`VehicleId` nullable ve form onu hiç doldurmadığı için **her kredi taksiti `AccountRef = null` ile
deftere düşüyor**. Sonuç: araç kredisi maliyeti Araç Karnesi (`/raporlar/arac-karne/{id}`) ve Filo
Analiz raporlarında **hiçbir araca yansımıyor** — rapor "(Atanmamış)" tarafında birikiyor.

> Bu, daha önce düzeltilmiş bir hata sınıfının (araç ön muhasebe atıf düzeltmesi) formdan sızan
> devamıdır: atıf zinciri kodda doğru kurulmuş ama girdi hiç alınmıyor.

## Yapılacaklar

1. **`src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor`** — "Yeni Kredi" formuna
   araç seçici ekle (`name="vehicleId"`, `<select>` ya da proje konvansiyonu gereği
   `<input list>` + `<datalist>` ComboBox). Alan **opsiyonel** kalmalı (filo geneli krediler için).
2. **`src/RentACar.Web/AracKredileri/AracKrediEndpoints.cs`** — `vehicleId` form alanını oku;
   boş string gelebileceği için `FormParse.Id(...)` ile çevir (CLAUDE.md §5 tuzağı).
3. **`src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor`** — satış formuna
   `hedefFiyat` (decimal), `satisKm` (int), `satisKanali` (metin/ComboBox), `devir` (metin) alanlarını
   ekle; uçta `FormParse.Dec/Int` ile çevir.
4. **`src/RentACar.Web/Components/Pages/Baflar/BafList.razor`** — "Teslim Al" formuna `donusTarihi`
   (datetime-local) ve `donusYakit` (int) ekle; uçta `FormParse.Date/Int`.
5. Her form için ilgili endpoint'in input'a alanı geçirdiğini doğrula (servis tarafı zaten hazır).

## Dokunulacak dosyalar

- `src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor` — araç seçici
- `src/RentACar.Web/AracKredileri/AracKrediEndpoints.cs` — `vehicleId` parse
- `src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor` — 4 alan
- `src/RentACar.Web/Components/Pages/Baflar/BafList.razor` — 2 alan
- İlgili `*Endpoints.cs` dosyaları — form alanı okuma

## Migration

**Yok.** Kolonların hepsi zaten mevcut.

## Test

- `AracKrediTests`: araçlı kredi oluştur → taksit postla → defterde gider bacağının
  `AccountRef == vehicleId` olduğunu doğrula. **Bağımsız oracle:** beklenen değer senaryodan
  ("krediyi şu araca bağladım, gideri o araçta görmeliyim"), servis kodundan değil.
- Aynı testin negatif hâli: araçsız kredide `AccountRef == null` (filo geneli kredi meşru).
- `VehicleSaleTests`: 4 alan kaydedilip geri okunuyor.
- `BafTests`: teslim alma sonrası `DonusTarihi`/`DonusYakit` dolu.

## Exit

- [ ] Üç formda da yeni alanlar görünüyor ve kaydediliyor
- [ ] Araçlı kredi taksitinin gideri **araç karnesinde görünüyor** (atıf çalışıyor)
- [ ] Boş bırakılan alanlar 400 vermiyor (opsiyonel alanlarda `FormParse` kullanıldı)
- [ ] Tam suite yeşil

## Notlar

Bu fazın asıl değeri 4. maddedeki atıf düzeltmesi. Diğer alanlar veri zenginliği; kredi-araç bağı
ise **rapor doğruluğu**. Aynı kalıbın başka yerlerde de olup olmadığı, bir alanı bağlarken onu yazan
TÜM formların gre'plenmesiyle kontrol edilmeli.
