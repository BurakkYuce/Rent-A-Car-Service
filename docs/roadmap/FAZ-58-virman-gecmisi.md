# FAZ-58 — Virman Geçmişi Listelenebilirliği (Kasa/Banka Virman)

| | |
|---|---|
| **Desen** | D4 |
| **Efor** | 1 gün |
| **Bağımlılık** | FAZ-50 (hesap adının gösterilmesi için — PR-A olmadan da SourceId-bazlı liste
  çalışır ama "Kaynak/Hedef Hesap No" kolonu boş kalır) |
| **Kapsanan canlı ekran** | `banka_virman_islem_ara.aspx` |
| **Risk** | düşük — yeni sorgu, mevcut şema DEĞİŞMEZ, okuma-yolu |

## Amaç
Kullanıcı `/raporlar/virman-gecmisi` sayfasında geçmiş Kasa↔Banka virman işlemlerini (tarih,
kaynak/hedef hesap, tutar, döviz) listeleyip filtreleyebilir hale gelir — şu an virman işlemleri
`/kasa` sayfasının "Son İşlemler" tablosunda HİÇ görünmüyor.

## Neden (kanıt — Yapısal Bulgu #2)
`CashService.TransferAsync` (Kasa↔Banka, `src/RentACar.Application/Finance/CashService.cs` L169-193)
**`CashTransaction` yazmıyor**, yalnız iki `AccountLedgerEntry` postluyor (`SourceType="Virman"`,
ortak `SourceId`). `/kasa` sayfasının "Son İşlemler" tablosu `CashService.ListAsync()` →
`CashTransactions` tablosunu okuyor → virman hiç görünmüyor (grep doğrulandı: `TransferAsync`
içinde `_repository.PostAsync(tx, ...)` çağrısı YOK, `_ledger.PostAsync` var).

**Seçilen yaklaşım (Seçenek A — bu planın efor sayımı bunu baz alır):** YENİ SORGU, mevcut şema hiç
değişmez. `AccountLedgerEntry` tablosu `WHERE SourceType='Virman'` filtrelenip `SourceId`'ye göre
çift (Borç/Alacak bacağı) gruplanır → bir rapor/liste DTO'su üretir. Kayıt numarası yoksa `SourceId`
kısaltması veya tarih+sıra gösterilir (canlının "Kayit No" tam sıra-numarası PARİTESİ olmaz ama
işlevsel liste olur). **Risk yok** — okuma-yolu, şema değişmez.

(**Seçenek B**, tam parite: `CashTransactionType`'a `Virman` eklenir, `CashTransaction.CariId`
NULLABLE'a çevrilir, gerçek `VR-000001` sıra no'su `SequenceAllocator`'dan tahsis edilir — blast-
radius büyük (`KasaHub.razor` `_adlar` sözlük araması, `ListExportCatalog.NakitIslemler`,
`GetRentalIslemSayilariAsync` etkilenir), D5 zorunlu adversarial gerekir. Kullanıcı gerçek sıra-no
isterse AYRI bir eskalasyon olarak değerlendirilmeli — bu faz Seçenek A'yı uygular.)

## Yapılacaklar
1. `src/RentACar.Application/Reporting/ReportService.cs`'e yeni `GetVirmanGecmisiAsync(from, to, ct)`
   metodu: `AccountLedgerEntry WHERE SourceType='Virman'` sorgusu, `SourceId`'ye göre gruplanır
   (Debit bacağı=Hedef, Credit bacağı=Kaynak), DTO: Tarih, Kaynak Hesap (FAZ-50 sonrası `AccountRef`
   üzerinden `FinancialAccount.Ad`, FAZ-50 öncesi/legacy null ise `LedgerAccountType` adı), Hedef
   Hesap, Tutar, Döviz.
2. Yeni `src/RentACar.Web/Components/Pages/Reports/VirmanGecmisi.razor` (rota:
   `/raporlar/virman-gecmisi`) + filtre (Tarih aralığı).
3. `src/RentACar.Web/Components/Layout/MainLayout.razor`'a nav eklenir.

## Dokunulacak dosyalar
- `src/RentACar.Application/Reporting/ReportService.cs` (yeni `GetVirmanGecmisiAsync`)
- (yeni) `src/RentACar.Web/Components/Pages/Reports/VirmanGecmisi.razor`
- `src/RentACar.Web/Components/Layout/MainLayout.razor` (nav)

## Migration
Yok — mevcut `AccountLedgerEntry` tablosu okunuyor, yeni tablo/kolon yok.

## Test
- (yeni) `VirmanGecmisiTests.cs`: elle 2 virman (`TransferAsync` ile, FAZ-50 sonrası farklı
  `HesapId`'lerle) oluşturulur; `GetVirmanGecmisiAsync` 2 satır döndürüyor mu, her satırın Kaynak/
  Hedef Hesap adı doğru mu (bağımsız oracle: elle sabitlenen hesap adları, koddan türetilmez).
  FAZ-50 öncesi (legacy, `HesapId=null`) bir virman için Kaynak/Hedef alanı `LedgerAccountType` adını
  (ör. "Kasa") gösteriyor mu (regresyon-güvenli fallback).

## Exit
- [ ] `/raporlar/virman-gecmisi` sayfası çalışıyor
- [ ] Tarih aralığı filtresi çalışıyor
- [ ] FAZ-50 sonrası hesap adı, öncesi/legacy'de tür adı gösteriliyor (regresyon yok)
- [ ] Tam suite yeşil

## Notlar
Seçenek B (gerçek `VR-000001` sıra no'su) kullanıcı isterse AYRI bir sonraki faz olarak
değerlendirilmeli — bu fazın kapsamı DEĞİL.
