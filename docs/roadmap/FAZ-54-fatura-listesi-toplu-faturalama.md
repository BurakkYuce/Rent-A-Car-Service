# FAZ-54 — Fatura Listesi: Filtre/Kolon + Toplu Faturalama

| | |
|---|---|
| **Desen** | D3 (filtre/kolon) + D5 (toplu faturalama — PARA) |
| **Efor** | 3 gün (1 gün filtre/kolon + 2 gün toplu faturalama/Opus) |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `fatura_islem_listesi.aspx` ("XML'e Aktar" hariç — bkz.
  `_toplama-05-finans.md` BLOKE) |
| **Risk** | orta — (a) risksiz; (b) YÜKSEK (atomik çoklu-fatura kesimi, KDV/kur çözümü kararı) |

**Zorunlu:** adversarial inceleme (bu fazın (b) toplu faturalama kısmı için) — Critical/High/Medium
bulgu kalmadan commit yok.

## Amaç
Kullanıcı fatura listesinde canlı paritesine yakın filtre (Cari, Fatura No aralığı, Plaka Ara,
Fatura Durumu, Tarih aralığı, Ofis) + kolonlarla arama yapabilir; ayrıca birden çok kira/kaydı
seçip TEK istekle toplu fatura kesebilir hale gelir.

## Neden (kanıt)
`InvoiceList.razor`'da şu an filtre paneli yok — liste tüm faturaları filtresiz döküyor. Canlıda
ID, Cari Kod, Vergi Dairesi/No, İptal, Ofis, Müş Özel Kod, Açıklama, Müşteri Ülke, Genel Toplam Dvz,
Özel Kod kolonları var, bizde yok. Toplu faturalama ("Seçili Olanları Faturala") hiç yok —
`InvoiceService`'te `CashService.BatchCollectAsync` (`CashService.cs` L91-153) desenine benzer bir
`BatchCreateAsync` YOK (grep doğrulandı).

## Yapılacaklar
1. **(a) Filtre + kolon (D3):** `InvoiceList.razor`'a filtre paneli (Cari, Fatura No aralığı,
   Plaka Ara, `Fatura_Durumu` Hepsi/Faturalanmayanlar/Faturalananlar, Tarih aralığı, Ofis) + kolon
   (ID, Cari Kod, Vergi Dairesi/No, İptal, Ofis, Müş Özel Kod, Açıklama, Müşteri Ülke, Genel Toplam
   Dvz, Özel Kod) eklenir.
2. **(b) "Seçili Olanları Faturala" toplu aksiyon (D5, Opus kararı gerekli):**
   `InvoiceService`'e `CashService.BatchCollectAsync` desenine benzer `BatchCreateAsync` eklenir:
   ATOMİK (hep-ya-hiç, `CashRepository.PostBatchAsync` desenine benzer bir `PostBatchAsync`),
   satır-bazlı dengeli (her fatura kendi Borç Cari/Alacak Gelir+KDV çiftini yazar), idempotency
   anahtarı (`CashService.RowKey(batch, index)` desenine benzer, batch⊕index deterministik).
   **KARAR GEREKLİ — Opus/kullanıcı kararı (bu faz kararı VERMEZ):**
   - Hangi kiralar "faturalanabilir" sayılacak (yalnız `Durum=TamamlandiTeslim` mı, yoksa açık
     kiralar da faturalanabilir mi — açık kirada `GenelToplam` henüz kesinleşmemiş olabilir).
   - Toplu kesimde KDV oranı/kur nasıl çözülecek: her kiranın KENDİ kayıtlı KDV oranı/kur snapshot'ı
     mı kullanılır (güvenli, mevcut `KdvOranSnapshot` deseniyle tutarlı), yoksa toplu işlem başına
     TEK bir kur/tarih mi uygulanır (basit ama döviz riski taşır).
   Bu faz **yapısal iskeleti** kurar (form + servis imzası + atomik/idempotent altyapı); yukarıdaki
   iki karar netleşmeden gerçek `BatchCreateAsync` gövdesi YAZILMAZ — Opus incelemesi bu kararla
   birlikte gelir.

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Finance/InvoiceList.razor`
- `src/RentACar.Application/Finance/InvoiceService.cs` (yeni `BatchCreateAsync`)
- `src/RentACar.Web/Finance/FinanceEndpoints.cs`
- `src/RentACar.Infrastructure/Persistence/Repositories/InvoiceRepository.cs` (yeni
  `PostBatchAsync`, varsa — dosya adı teyit edilir)

## Migration
Yok — mevcut `Invoice`/`InvoiceLine` şeması kullanılır (idempotency anahtarı zaten `Invoice.Id`
üzerinden kısmi-unique deseniyle çözülür, `CashTransaction.IslemAnahtari` deseniyle aynı).

## Test
- (a) `InvoiceTests.cs`'e ek senaryo: 5 fatura (farklı cari/plaka/durum) elle oluşturulur; her
  filtre kombinasyonu beklenen alt-kümeyi döndürüyor mu (bağımsız oracle: beklenen sayı test içinde
  sabit).
- (b) (yeni) `ToplulFaturalamaTests.cs`:
  - **Defter dengesi:** N faturanın toplamı `Σ Borç(base) == Σ Alacak(base)` (her fatura kendi
    dengeli çiftini yazdığı için toplamda da dengeli olmalı — testte N=3 farklı döviz/kur ile).
  - **Atomiklik:** N faturadan biri geçersizse (ör. `CariId=Guid.Empty`) HİÇBİRİ yazılmamalı (batch
    tamamen geri alınır — DB'de fatura sayısı öncesi/sonrası AYNI).
  - **İdempotency:** aynı batch anahtarıyla ikinci çağrı ikinci kez fatura YAZMAZ (kısmi unique
    index yutar) — çift-submit senaryosu.

## Exit
- [ ] Filtre paneli + yeni kolonlar çalışıyor
- [ ] Toplu faturalama kararları (faturalanabilirlik kriteri + KDV/kur çözümü) Opus/kullanıcı
  onayıyla netleşti VE uygulandı
- [ ] Atomiklik + idempotency + defter dengesi testleri yeşil
- [ ] Adversarial inceleme: Critical/High/Medium bulgu YOK
- [ ] Tam suite yeşil

## Notlar
"XML'e Aktar" (UBL-TR e-Fatura XML şeması) BU FAZA DAHİL DEĞİL — entegratör kimliği gerekir (bkz.
`_toplama-05-finans.md` BLOKE).
