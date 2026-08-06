# FAZ-64 — Gider İşlemleri: Bilgi Alanları + Kısmi Ödeme Takibi

| | |
|---|---|
| **Desen** | D3 (bilgi alanları) + D2/PARA-bitişik (kısmi ödeme) |
| **Efor** | 2,5 gün (1 gün bilgi alanları + 1,5 gün kısmi ödeme/Opus) |
| **Bağımlılık** | Spesifik kasa/banka hesabı seçimi FAZ-50'nin ürünü — ek iş bu fazda YOK, sadece
  atıf |
| **Kapsanan canlı ekran** | `gider_islemleri.aspx` |
| **Risk** | orta — kısmi ödemenin defter postlamasını NASIL böldüğü kararı gerektiriyor |

**Zorunlu:** adversarial inceleme (kısmi ödeme kısmı için) — Critical/High/Medium bulgu kalmadan
commit yok.

## Amaç
Kullanıcı gider formunda Ödeme Tarihi (ayrı)/Hazır Açıklama (şablon)/Sözleşme No (kiraya bağlama)/
İşlem Yapan bilgilerini girebilir; ayrıca bir giderin KISMİ ödendiğini kaydedip "Kalan" tutarını
takip edebilir hale gelir (şu an gider hep TAM ödenmiş varsayılıyor).

## Neden (kanıt)
`Expense.cs` (`src/RentACar.Domain/Entities/Expense.cs`) şu an `OdemeYontemi`/`KasaBankaHesap`
taşıyor ama ayrı `OdemeTarihi`, `HazirAciklama`, `RentalId` (kiraya bağlama), kısmi ödeme takibi için
`OdenenTutar`/`Kalan` alanı YOK (grep doğrulandı) — gider hep tam ödenmiş varsayımıyla kurulu.

## Yapılacaklar
1. **Bilgi alanları (D3):** `Expense.cs`'e `OdemeTarihi` (DateTimeOffset?, ayrı ödeme tarihi),
   `HazirAciklama` (string?, şablon açıklama), `RentalId` (Guid?, kiraya bağlama FK), `IslemYapan`
   (**EKLENMEZ** — oturumdan geliyor, audit zaten taşıyor, `banka_virman.aspx`/`kasa_virman.aspx`
   ile aynı kural) eklenir.
2. **"Kalan" kısmi ödeme takibi (D2/PARA-bitişik):** `Expense`'e `OdenenTutar` (decimal) +
   hesaplanan `Kalan` (`GenelToplam - OdenenTutar`) eklenir — gider hep tam ödenmiş varsayımı
   kırılır.
   **KARAR GEREKLİ — Opus/kullanıcı kararı (bu faz kararı VERMEZ):** kısmi ödeme kaydı defter
   postlamasını NASIL böler — **(i)** TAM tutar (Borç Gider(net)+Borç KDV / Alacak karşı-hesap(gross))
   İLK GİRİŞTE yazılır, `OdenenTutar` yalnız TAKİP alanı (defter DEĞİŞMEZ, mevcut `BuildEntries`
   aynen kalır — düşük risk) — veya **(ii)** İKİ AŞAMALI: ilk giriş yalnız Gider+KDV'yi Cari'ye
   borçlandırır (`Borç Gider+Kdv / Alacak Cari`, AçıkHesap gibi), her kısmi ödeme AYRI bir
   `CashService.PayAsync` çağrısı ile Cari'yi kapatır (gerçek nakit çıkışı ödeme anında yazılır,
   daha doğru ama `ExpenseService`/`CashService` arası yeni bir bağ kurulması gerekir). Bu faz
   yapısal alanı ekler; hangi model uygulanacağı Opus incelemesiyle netleşir — VARSAYILAN (i)
   uygulanır (risk yok, mevcut defter davranışı korunur).
3. Migration: `Expense`'e yeni alanlar.

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Expenses/ExpenseList.razor`
- `src/RentACar.Domain/Entities/Expense.cs`
- (yeni) migration `AddExpenseOdemeVeKalan`
- `src/RentACar.Application/Expenses/ExpenseService.cs`

## Migration
Var — `Expenses` tablosuna `OdemeTarihi`/`HazirAciklama`/`RentalId`/`OdenenTutar` nullable/default
kolonları eklenir. RLS bloğu **ELLE EKLENMEZ** (mevcut tenant-owned tabloya additive kolon).

## Test
- (a) `ExpenseTests.cs`'e ek senaryo: bilgi alanları round-trip (bağımsız oracle: elle girilen
  değerler).
- (b) kısmi ödeme: elle 1000 TL gidere 400 TL `OdenenTutar` girilir → `Kalan=600` (bağımsız oracle:
  600 test içinde sabit); **defter dengesi** REGRESYONSUZ — mevcut `BuildEntries` testleri
  (Σ Borç(base)=Σ Alacak(base)) tam suite yeşil kalır ((i) kararı doğrulaması).

## Exit
- [ ] Bilgi alanları formda + kaydediliyor
- [ ] `OdenenTutar`/`Kalan` takip alanı çalışıyor
- [ ] Defter postlama modeli (i/ii) kararı Opus/kullanıcı onayıyla netleşti VE uygulandı
- [ ] Adversarial inceleme: Critical/High/Medium bulgu YOK
- [ ] Tam suite yeşil

## Notlar
Spesifik kasa/banka hesabı seçimi (Kasa_Kodu/Hesap_No) FAZ-50'nin ürünüdür — bu fazda EK İŞ YOK.

**Defter postlama modeli (madde 2) Opus/kullanıcı kararıdır** — plan dosyasında açıkça
"yanlış 'Kalan' hesap-şeması para kaybına yol açabilir" notu var; bu yüzden zorunlu adversarial
inceleme şart.
