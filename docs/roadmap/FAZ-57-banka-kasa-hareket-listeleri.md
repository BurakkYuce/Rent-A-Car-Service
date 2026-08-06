# FAZ-57 — Kasa/Banka Hareket Listeleri: Hesap-bazlı Filtre Derinliği

| | |
|---|---|
| **Desen** | D3 |
| **Efor** | 2,5 gün (1 gün banka_hesap_hareketleri + 0,5 gün banka_nakit_listesi + 0,5 gün
  banka_para_listesi + 0,5 gün kasa_dagilimi) |
| **Bağımlılık** | FAZ-50 (TEMEL PR-A — hesap-bazlı `AccountRef` altyapısı olmadan bu filtreler
  anlamsız/boş kalır) |
| **Kapsanan canlı ekran** | `banka_hesap_hareketleri.aspx`, `banka_nakit_listesi.aspx`,
  `banka_para_listesi.aspx`, `kasa_dagilimi.aspx` |
| **Risk** | düşük — salt-okunur filtre/kolon genişletmesi, `KasaBankaDefteri.razor` üzerinde tek PR |

## Amaç
Kullanıcı `/raporlar/kasa-banka` sayfasında artık spesifik hesap (IBAN/Hesap No), Cari Bilgi, Döviz,
İşlem Türü, Şube kolonlarıyla filtreleyip görebilir hale gelir — dört canlı ekran (farklı ön-filtre
varyantları) TEK sayfa + filtre kombinasyonuyla karşılanır.

## Neden (kanıt)
`/raporlar/kasa-banka` bugün nav'a eklendi (`MainLayout.razor`, commit `d08810c`) ve erişilebilir —
ama Hesap_No (spesifik IBAN), Cari Bilgi, Döviz, İşlem Türü, Şube kolonları FAZ-50'nin ürünüdür
(`AccountRef` FAZ-50'den önce her zaman `null`, dolayısıyla bu kolonlar boş/anlamsız kalır). Canlıda
dört ayrı aspx (`banka_hesap_hareketleri`, `banka_nakit_listesi`, `banka_para_listesi`,
`kasa_dagilimi`) aynı temel veriyi (banka/kasa hareketleri) farklı ön-filtreyle gösteriyor; bizde
TEK sayfa + filtre kombinasyonu yeterli (canlı 4 ekranın davranışsal birleşimi).

## Yapılacaklar
1. `Reports/KasaBankaDefteri.razor`'a FAZ-50'nin ürettiği hesap-bazlı `AccountRef` verisini
   kullanan yeni kolonlar eklenir: Hesap_No (spesifik IBAN — `FinancialAccount.Iban`/`HesapNo`),
   Cari Bilgi, Döviz, İşlem Türü, Şube.
2. `Devir_Goster` (açılış bakiyesi gösterme anahtarı — checkbox) eklenir.
3. Kasa_Kodu (spesifik hesap) filtresi eklenir — `banka_nakit_listesi`/`banka_para_listesi`/
   `kasa_dagilimi`'nin istediği filtre kombinasyonu bununla karşılanır (aynı sayfa, aynı filtre
   altyapısı, ek kolon/sorgu değişikliği minimal).
4. `ReportService.GetKasaBankaSummaryAsync`/`GetAccountLedgerAsync` bu yeni filtre parametrelerini
   (hesapId, döviz, işlemTürü, şube) alacak şekilde genişler (FAZ-50'de zaten hesap-bazlı temel
   genişleme yapılmıştı — bu faz üstüne kolon/filtre ekler).

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Reports/KasaBankaDefteri.razor`
- `src/RentACar.Application/Reporting/ReportService.cs`

## Migration
Yok — FAZ-50'nin ürettiği `CashTransaction.HesapId`/`AccountLedgerEntry.AccountRef` verisi okunuyor.

## Test
- `ReportingTests.cs`'e ek senaryo: elle 4 işlem (2 farklı hesap, 2 farklı döviz) oluşturulur; her
  filtre kombinasyonu (Hesap_No, Döviz, İşlem Türü, Şube) beklenen alt-kümeyi döndürüyor mu
  (bağımsız oracle: beklenen satır sayısı/toplamı test içinde sabit).
- `Devir_Goster` açık/kapalı iki modda açılış bakiyesi satırının görünüp/kaybolduğu doğrulanır.

## Exit
- [ ] Hesap_No/Cari Bilgi/Döviz/İşlem Türü/Şube kolonları çalışıyor
- [ ] `Devir_Goster` anahtarı çalışıyor
- [ ] Kasa_Kodu filtresi 4 canlı ekranın işlevini tek sayfada karşılıyor
- [ ] Tam suite yeşil

## Notlar
Bu faz FAZ-50 (TEMEL PR-A) TAMAMLANMADAN başlatılmamalı — `AccountRef` FAZ-50'den önce hep `null`
olduğu için bu filtreler test edilemez/anlamsız kalır.
