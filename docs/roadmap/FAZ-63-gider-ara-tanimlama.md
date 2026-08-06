# FAZ-63 — Gider Arama Filtresi + Gider Tanımlama Mikro Düzeltme

| | |
|---|---|
| **Desen** | D3 (gider_ara) + D1 mikro (gider_tanimlama) |
| **Efor** | 1 gün (+10 dakika mikro-ek) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `gider_ara.aspx`, `gider_tanimlama.aspx` |
| **Risk** | düşük — filtre + salt-okunur kolon eklemesi |

## Amaç
Kullanıcı gider listesinde canlı paritesine yakın filtre (Cari Ara, Tarih aralığı, Ofis,
`Gider_Iade`, Plaka Ara, Gider Adı) ile arama yapabilir; ayrıca gider kategorisi liste ekranında
`Tür` kolonunu görebilir hale gelir (10 dakikalık mikro düzeltme — aynı "gider" ailesine ait, tek
başına faz açmaya değmeyecek kadar küçük olduğu için buraya eklendi).

## Neden (kanıt)
`ExpenseList.razor`'da filtre paneli yok (grep doğrulandı). Servis/hasar-özel kolonlar (İşlem KM,
Dönüş KM, Hasar Dosya No, Yansıtma/Garanti Tutarı) `ServiceRecord`/`Regulation` (hasar) tablolarından
JOIN gerektirir — bu VERİ zaten var ama farklı entity'lerde.

`ExpenseCategory.cs` (`src/RentACar.Domain/Entities/ExpenseCategory.cs` L18) `Tur` alanını ZATEN
taşıyor VE edit formunda ZATEN var — `ExpenseCategoryList.razor`'ın salt-okunur liste tablosunda
`<thead>`/`<td>` eksik, sadece görüntülenmiyor (kod/migration DEĞİŞMEZ, iki satır ekleme).

## Yapılacaklar
1. `ExpenseList.razor`'a filtre paneli (Cari Ara, Tarih aralığı, Ofis, `Gider_Iade`, Plaka Ara,
   Gider Adı) eklenir.
2. Servis/hasar-özel kolonlar için `ExpenseService`'e `ServiceRecord`/`Regulation` (hasar) tablosuyla
   JOIN eklenir (İşlem KM, Dönüş KM, Hasar Dosya No, Yansıtma/Garanti Tutarı) — multi-entity JOIN.
3. `ExpenseCategoryList.razor`'daki liste tablosunun `<thead>` satırına `<th>Tür</th>`, her satıra
   `<td>@c.Tur</td>` eklenir (tek dosya, iki satır).

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Expenses/ExpenseList.razor`
- `src/RentACar.Application/Expenses/ExpenseService.cs`
- `src/RentACar.Web/Components/Pages/ExpenseCategories/ExpenseCategoryList.razor`

## Migration
Yok — mevcut `Expense`/`ServiceRecord`/`Regulation`/`ExpenseCategory` tabloları okunuyor.

## Test
- `ExpenseTests.cs`'e ek senaryo: elle 3 gider (farklı cari/plaka/tarih) oluşturulur; filtre
  kombinasyonları beklenen alt-kümeyi döndürüyor mu (bağımsız oracle, sabit sayı).
- `ExpenseCategoryTests.cs`'e ek senaryo: `Tur` dolu bir kategori oluşturulur, liste sayfasında
  görünüyor mu (round-trip, bağımsız oracle: elle girilen `Tur` değeri).

## Exit
- [ ] Gider filtre paneli çalışıyor
- [ ] Servis/hasar-özel kolonlar (JOIN) doğru veri gösteriyor
- [ ] `ExpenseCategoryList.razor`'da `Tür` kolonu görünüyor
- [ ] Tam suite yeşil

## Notlar
Yok.
