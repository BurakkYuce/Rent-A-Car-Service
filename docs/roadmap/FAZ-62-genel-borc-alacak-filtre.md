# FAZ-62 — Genel Borç/Alacak: Filtre + Ayrı Kolon Derinliği

| | |
|---|---|
| **Desen** | D3 |
| **Efor** | 1 gün (mutabakat hariç) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `genel_borc_alacak.aspx` (Mutabakat Gönder/Mutabakat akışı HARİÇ —
  bkz. Notlar) |
| **Risk** | düşük — salt-okunur filtre/kolon genişletmesi |

## Amaç
Kullanıcı `/raporlar/cari-bakiye` sayfasında canlı paritesine yakın filtre (Cari Ara, Özel Kod,
Firma Seç, Sözleşme Durumu, Ofis, Borç Türü, Döviz, Min Tutar) + Telefon/Mail Adresi/Banka
kolonlarıyla arama yapabilir; Borç ve Alacak'ı AYRI kolonlarda (şu an nette birleşik `Bakiye`)
görebilir hale gelir.

## Neden (kanıt)
`CariBakiye.razor`'da şu an filtre paneli yok; grid `Bakiye` (net, Debit-Credit birleşik) tek
kolon gösteriyor — canlı ayrı Borç/Alacak kolonu taşıyor (grep doğrulandı).

## Yapılacaklar
1. `CariBakiye.razor`'a filtre paneli (Cari Ara, Özel Kod, Firma Seç, Sözleşme Durumu, Ofis, Borç
   Türü, Döviz, Min Tutar) eklenir.
2. Telefon/Mail Adresi/Banka kolonları eklenir (mevcut `Customer`/`FinancialAccount` alanlarından).
3. `ReportService.GetCariBalancesAsync`'e ayrı Borç/Alacak toplamları döndürecek şekilde genişletme
   (mevcut net `Bakiye` hesaplaması DEĞİŞMEZ — `Σ SignedBase` aynen kalır, yalnız ayrıca `ToplamBorc`
   =`Σ Debit(base)`, `ToplamAlacak`=`Σ Credit(base)` iki ek alan döner).

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Reports/CariBakiye.razor`
- `src/RentACar.Application/Reporting/ReportService.cs` (`GetCariBalancesAsync`)

## Migration
Yok — mevcut `AccountLedgerEntry`/`Customer`/`FinancialAccount` tabloları okunuyor.

## Test
- `CariReportTests.cs`'e ek senaryo: elle bir cari için 3 hareket (2 Debit=100+50, 1 Credit=30)
  oluşturulur → `ToplamBorc=150`, `ToplamAlacak=30`, net `Bakiye=120` (bağımsız oracle: üç sabit
  değer test içinde elle hesaplanır); mevcut net `Bakiye` hesaplaması REGRESYONSUZ.
- Filtre kombinasyonları (bağımsız oracle: beklenen alt-küme sabit).

## Exit
- [ ] Filtre paneli çalışıyor
- [ ] Telefon/Mail Adresi/Banka kolonları görünüyor
- [ ] Ayrı Borç/Alacak kolonları doğru + net Bakiye REGRESYONSUZ
- [ ] Tam suite yeşil

## Notlar
**Mutabakat Gönder/Mutabakat** (e-mutabakat akışı) BU PR'A DAHİL DEĞİL — ayrı bir dikey (D7, e-posta
gönderim altyapısı + onay iş akışı gerektirir), ayrı bir sonraki faz olarak değerlendirilmeli.
