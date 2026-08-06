# FAZ-50 — Hesap-bazlı (FinancialAccount) Kasa/Banka Defteri [TEMEL PR-A]

| | |
|---|---|
| **Desen** | D5 — para/model temeli (defter hesap-ayrımı) |
| **Efor** | 6 gün (5 gün TEMEL PR-A yapısal + 0,5 gün `kasa_virman.aspx` kalan alanlar + 0,5 gün
  02-tanim-master transferi: `hesap_para_islem.aspx`+`genel_kasa.aspx` kalan ince UI — bkz. Notlar) |
| **Bağımlılık** | yok (öncelikli — FAZ-56/57/58/59/64 bu fazdaki hesap-bazlı `AccountRef` altyapısına dayanır) |
| **Kapsanan canlı ekran** | `hesap_para_islem.aspx` ve `genel_kasa.aspx` (02-tanim-master-plan.md'den transfer — bkz. Notlar), `banka_virman.aspx`, `kasa_virman.aspx` (kısmi) |
| **Risk** | yüksek — mevcut Kasa/Banka defter satırlarının `AccountRef` alanı ilk kez anlamlı dolduruluyor; geriye dönük veri kararı (backfill mi/null-toleranslı mı) bakiye sürekliliğini etkiler |

**Zorunlu:** adversarial inceleme — Critical/High/Medium bulgu kalmadan commit yok.

## Amaç
Kullanıcı artık tahsilat/ödeme/virman/gider/sigorta-MTV-muayene ödemesi/araç kredisi taksiti/depozito
işlemlerinde **hangi spesifik Kasa/Banka hesabından** (IBAN/hesap no bazında) işlem yapıldığını
seçebilir; iki farklı banka hesabı arasında virman yapabilir (şu an sistem ikisini de "Banka" görüp
engelliyor); ve kasa/banka defteri raporunu hesap bazında filtreleyip görebilir.

## Neden (kanıt)
`LedgerAccountType` (`src/RentACar.Domain/Enums/LedgerAccountType.cs`) yalnız `Kasa=1`/`Banka=2` İKİ
değeri tutuyor — IBAN/hesap bazlı ayrım yok. `AccountLedgerEntry.AccountRef` (nullable Guid; Cari/
Depozito/Gider için zaten "hangi varlık" ayrımını taşıyor — bkz. `CashService.TransferBetweenCariAsync`
L234-239, `ExpenseService.BuildEntries` L139/146) Kasa/Banka satırlarında HER ZAMAN `null` yazılıyor —
grep ile doğrulandı, 12 satır:
- `CashService.cs` L188, L190 (`TransferAsync`), L288 (`Natural`)
- `ExpenseService.cs` L146 (`karsiRef = karsiHesap == Cari ? e.CariId : null` — Kasa/Banka'da null)
- `AracKrediService.cs` L102 (`TaksitOdeAsync`)
- `RegulationService.cs` L100/L133/L167 (`MtvOdeAsync`/`MuayeneOdeAsync`/`SigortaOdeAsync`)
- `DepozitoService.cs` L34/L40 (`AlAsync`/`IadeAsync` — `borcRef`/`alacakRef` sabit `null`)

`FinancialAccount` (`src/RentACar.Domain/Entities/FinancialAccount.cs`, `/hesaplar`) tablosu zaten
`Iban`/`HesapNo`/`Banka`/`Sube` alanlarını taşıyor (roadmap K1'de eklenmiş) ama **hiçbir yazma-yolu
bunu okumuyor** — tanım ile işlem tarafı bağlı değil. `CashService.TransferAsync`'teki
`if (kaynak == hedef) throw` kontrolü (L177) **enum seviyesinde** — iki farklı banka hesabı (ikisi de
`Tur=Banka`) sistemin gözünde AYNI "hedef" olduğundan virman engelleniyor (`banka_virman.aspx`'in
yapısal kısıtı — canlı fazlası, bizde YOK).

`02-tanim-master-plan.md`'nin `hesap_para_islem.aspx` (efor tahmini: 1 gün, `KasaHub.razor`'a
IBAN/Hesap No seçimi) ve `genel_kasa.aspx` (efor tahmini: 1 gün, `KasaBankaDefteri.razor`'a çoklu-kasa
checkbox) maddeleri bu kök nedeni görmeden yalnız UI katmanını planlamış — forma bir `<select>`
eklemek kaynağı çözmez, veri (`AccountRef`) hiç yazılmıyor. Bu iki ekranın gerçek maliyeti BU FAZ'ın
kendisidir; bkz. Notlar.

## Yapılacaklar
1. `src/RentACar.Domain/Entities/CashTransaction.cs`: `KarsiHesap` enum alanının YANINA nullable
   `HesapId` (`Guid?`, `FinancialAccount` FK) eklenir. Enum SİLİNMEZ (geriye dönük satırlar + Kasa/
   Banka türü hâlâ enum'dan okunur — `HesapId` yalnız hangi SPESİFİK hesap olduğunu ekler).
2. `src/RentACar.Application/Finance/CashInput.cs`, `src/RentACar.Application/Expenses/ExpenseInput.cs`:
   `Hesap`/`KasaBankaHesap` enum alanının yanına opsiyonel `HesapId` (`Guid?`) eklenir (boş → eski
   davranış, legacy `AccountRef=null`).
3. Ledger üretimi — aşağıdaki 8 satırın hepsinde: `HesapId` verilmişse Kasa/Banka bacağının
   `AccountRef`'i ONU alır, verilmemişse `null` (legacy davranış korunur):
   - `CashService.cs` `Natural()` (L288) — `tx.HesapId` eklenir.
   - `CashService.cs` `TransferAsync()` (L188/L190) — imza `Guid? kaynakHesapId = null, Guid?
     hedefHesapId = null` parametreleriyle genişler.
   - `ExpenseService.cs` `BuildEntries()`/`BuildPosting()` (L98-146) — `input.HesapId`.
   - `AracKrediService.cs` `TaksitOdeAsync()` (L59-102) — parametre eklenir.
   - `RegulationService.cs` `MtvOdeAsync`/`MuayeneOdeAsync`/`SigortaOdeAsync` (L78/109/143) —
     üçünün de `hesap` parametresi yanına `hesapId` eklenir.
   - `DepozitoService.cs` `AlAsync`/`IadeAsync` (L31-40) — `hesapId` parametresi eklenir.
4. `CashService.TransferAsync`: `if (kaynak == hedef) throw` kontrolü
   `if (kaynak == hedef && kaynakHesapId == hedefHesapId) throw` şekline genişler — `Guid?`
   karşılaştırması null-safe (ikisi de null ise eski davranış: aynı enum = engel; en az biri
   doluysa ve farklıysa artık FARKLI sayılır). Aynı `LedgerAccountType.Banka` iki FARKLI hesap
   arası virman artık MÜMKÜN.
5. `src/RentACar.Application/Reporting/ReportService.cs` `GetKasaBankaSummaryAsync`/
   `GetAccountLedgerAsync` (~L388-420): hesap-bazlı filtre/gruplama parametresi (`Guid? hesapId`)
   alır. `AccountRef` doluysa o hesaba, `null` ise **"(tanımsız/legacy hesap)"** ayrı satıra toplanır
   — İKİ KOVA karışık toplanmaz (adversarial risk: legacy null'ları rastgele bir hesaba yedirmek
   bakiyeyi bozar).
6. Form katmanı:
   - `Finance/KasaHub.razor`: virman formunda Kaynak/Hedef select'leri
     `FinancialAccountService.ListActiveAsync()`'ten beslenir (tip'e göre filtrelenmiş); Banka
     Dövizi/Banka Kuru alanları eklenir; **Makbuz Numarası + İşlem Şube** alanları eklenir
     (`kasa_virman.aspx`'in kalan iki bilgi alanı — "İşlemi Yapan" EKLENMEZ, oturumdan geliyor,
     audit zaten taşıyor).
   - `Reports/KasaBankaDefteri.razor`: Hesap select'i tip'ten spesifik hesaba genişler; **Araç
     Sahibi** dropdown (`VehicleOwnerService`'ten), **çoklu-kasa checkbox listesi**
     (`FinancialAccountService.ListAsync`), **İşlem Tipi** (Depozito Hariç/Sadece Depozito —
     `SourceType` filtresi), **Döviz** filtresi eklenir; grid'e Cari Bilgi, Kasa Kodu, Döviz, Şube,
     Evrak No, Araç Sahibi, Plaka kolonları eklenir (`genel_kasa.aspx`'in kalan işi).
   - `Finance/FinanceEndpoints.cs` (`/finans/virman`, `/finans/tahsilat`, `/finans/odeme`): `HesapId`
     form alanı okunur ve servise geçilir.
   - `Expenses/ExpenseList.razor` + endpoint, `AracKredileri/AracKrediEndpoints.cs`,
     `Regulation/RegulationEndpoints.cs`, `Finance/DepozitoEndpoints.cs`: hepsi `hesap` string
     parametresini şimdi enum'a çeviriyor — yanına `hesapId` eklenir.
7. Migration: yalnız `CashTransactions.HesapId` (nullable) eklenir. `AccountLedgerEntries` tarafında
   YENİ kolon YOK (mevcut nullable `AccountRef` kullanılıyor) — ADDITIVE, geriye dönük veri bozulmaz.
8. **KARAR GEREKLİ — Opus/kullanıcı kararı (bu faz kararı VERMEZ):** geriye dönük satırlar
   (`AccountRef=null`) nasıl ele alınacak —
   **(a) null-toleranslı okuma** (bu fazın yapısal iskeleti BUNU baz alır: legacy satırlar kalıcı
   "(tanımsız/legacy hesap)" kovasında kalır, hiçbir veri taşınmaz, risksiz) — veya
   **(b) backfill** (bir "Varsayılan Kasa"/"Varsayılan Banka" `FinancialAccount` kaydı oluşturulup
   tüm `AccountRef=null` satırları ona atanır — bakiye sürekliliğini etkiler: (b) seçilirse o
   varsayılan hesabın raporlanan bakiyesi ANİDEN yükselir, ama tek-kova temizlenir).
   Bu FAZ (a)'yı uygular (risk yok, additive); (b) istenirse AYRI bir backfill migration'ı olarak
   kullanıcı onayı sonrası eklenir.
9. (02-tanim-master transferi) `genel_kasa.aspx`'in "Araç Sahibi"/çoklu-kasa/İşlem Tipi filtreleri
   madde 6'da yukarıda listelendi — burada tekrar sayılmadı, sadece atıf.
10. (02-tanim-master transferi) `hesap_para_islem.aspx`'in Makbuz Numarası/İşlem Şube alanları
    madde 6'da yukarıda listelendi — burada tekrar sayılmadı, sadece atıf.

## Dokunulacak dosyalar
- `src/RentACar.Domain/Entities/CashTransaction.cs` — `HesapId`
- `src/RentACar.Application/Finance/CashInput.cs`, `src/RentACar.Application/Expenses/ExpenseInput.cs`
- `src/RentACar.Application/Finance/CashService.cs` (`Natural`, `TransferAsync`)
- `src/RentACar.Application/Expenses/ExpenseService.cs` (`BuildPosting`/`BuildEntries`)
- `src/RentACar.Application/AracKredileri/AracKrediService.cs` (`TaksitOdeAsync`)
- `src/RentACar.Application/Regulation/RegulationService.cs` (3 metod)
- `src/RentACar.Application/Finance/DepozitoService.cs` (`AlAsync`/`IadeAsync`/`PostAsync`)
- `src/RentACar.Application/Reporting/ReportService.cs` (`GetKasaBankaSummaryAsync`,
  `GetAccountLedgerAsync`)
- `src/RentACar.Infrastructure/Persistence/Repositories/CashRepository.cs` (`PostAsync`/
  `PostBatchAsync` imzası değişmez, `HesapId` alanı SaveChanges'e girer)
- `src/RentACar.Web/Components/Pages/Finance/KasaHub.razor`
- `src/RentACar.Web/Components/Pages/Reports/KasaBankaDefteri.razor`
- `src/RentACar.Web/Finance/FinanceEndpoints.cs`, `src/RentACar.Web/Finance/DepozitoEndpoints.cs`
- `src/RentACar.Web/AracKredileri/AracKrediEndpoints.cs`, `src/RentACar.Web/Regulation/RegulationEndpoints.cs`
- `src/RentACar.Web/Components/Pages/Expenses/ExpenseList.razor`
- (yeni) migration `AddCashTransactionHesapId`

## Migration
Var — `CashTransactions` tablosuna nullable `HesapId` (Guid, FK→`FinancialAccounts.Id`, `ON DELETE
SET NULL` önerilir) eklenir. RLS bloğu **ELLE EKLENMEZ** (yeni tablo yok, mevcut tabloya additive
nullable kolon; RLS policy tabloda zaten aktif ve kolon-bağımsız çalışır).

## Test
- (yeni) `HesapBazliDefterTests.cs`:
  - **Bağımsız oracle:** elle 2 `FinancialAccount` oluştur (Banka-A id sabit, Banka-B id sabit,
    ikisi de `Tur=Banka`). `TransferAsync(kaynak: Banka, hedef: Banka, kaynakHesapId: A, hedefHesapId:
    B, tutar: 1000)` çağrısı ARTIK `ValidationException` FIRLATMAMALI (önceden fırlıyordu — regresyon
    kilidi). Postlanan iki `AccountLedgerEntry`'nin `AccountRef` alanları sırasıyla B (Debit) / A
    (Credit) — beklenen değer test içinde SABİT guid, koddan türetilmez.
  - **Defter dengesi:** `Σ Borç(base) == Σ Alacak(base) == 1000` (aynı money/kur → base aynı).
  - **`kaynak == hedef` hâlâ engellenir:** `kaynakHesapId == hedefHesapId` (ikisi de A) verilirse
    ValidationException.
  - **Legacy okuma regresyonu:** `HesapId` verilmeden yapılan eski-tarz Tahsilat/Ödeme/Virman
    `AccountRef=null` yazmayı sürdürür — mevcut `FinanceTests.cs`/`KasaBankaTests.cs`/
    `AdversarialCashTests.cs`/`CariVirmanTests.cs`/`TahsilatIdempotencyTests.cs` tam suite YEŞİL
    kalmalı (tek başına regresyon kanıtı).
  - **İdempotency:** `HesapId` eklenmesi mevcut `IslemAnahtari`/`SourceId` dedup mekanizmasını
    KIRMAZ — aynı token'la iki kez `TransferAsync(..., kaynakHesapId: A, hedefHesapId: B, ...)`
    çağrılırsa ikinci çağrı yutulur (kısmi unique index).
  - **Tenant izolasyonu:** Tenant-1'in `FinancialAccount`'ı Tenant-2 bağlamında `hesapId` olarak
    verilirse (`racar_app` ile) `FinancialAccountService` tenant-filtreli bulamaz → ValidationException
    (cross-tenant hesap sızıntısı YOK).
- `ReportingTests.cs`'e ek senaryo: 3 işlem (2 farklı `FinancialAccount`, 1 legacy `HesapId=null`)
  sonrası `GetKasaBankaSummaryAsync` üç ayrı satır döndürür (2 hesap + 1 "(tanımsız/legacy hesap)")
  — beklenen toplamlar elle hesaplanır, servis kodundan türetilmez.

## Exit
- [ ] `CashTransaction.HesapId` eklendi + migration uygulandı, tam suite yeşil
- [ ] `TransferAsync` iki farklı Banka hesabı arasında ARTIK çalışıyor (aynı `LedgerAccountType`,
  farklı `HesapId`) — regresyon testiyle kanıtlı
- [ ] 8 çağrı noktasının hepsi `HesapId`→`AccountRef` zincirini kullanıyor (grep: eski sabit
  `AccountRef = null` satırları artık koşullu)
- [ ] `ReportService` hesap-bazlı gruplama + "(tanımsız/legacy hesap)" kovası AYRI satırda (karışık
  toplanmıyor)
- [ ] `genel_kasa.aspx` + `hesap_para_islem.aspx` kalan alanları eklendi (02-tanim-master transferi)
- [ ] `kasa_virman.aspx` Makbuz No/İşlem Şube alanları eklendi
- [ ] Adversarial inceleme: Critical/High/Medium bulgu YOK

## Notlar
**Backfill vs null-toleranslı okuma kararı Opus/kullanıcıya aittir** (bkz. Yapılacaklar madde 8) —
bu faz yalnız null-toleranslı (a) seçeneğini uygular, backfill (b) ayrı bir sonraki-adım kararı olarak
AÇIK bırakılır.

Bu faz `02-tanim-master-plan.md`'nin `hesap_para_islem.aspx` ve `genel_kasa.aspx` maddelerini TAMAMEN
çözer — o planı yürüten kişi/ajan bu iki ekranı **"FAZ-50 ile kapsanan"** olarak işaretlemeli, tekrar
iş açmamalı (bkz. `_toplama-05-finans.md`). Aksi halde aynı `KasaHub.razor`/`CashService.cs`/
`KasaBankaDefteri.razor` dosyasında iki bağımsız PR çakışır.

Bu faz TAMAMLANMADAN FAZ-56/57/58/59/64 (TEMEL PR-A bağımlılığı taşıyanlar) başlatılamaz — spesifik
hesap seçimi olmadan raporları/formları genişletmek anlamsız kalır.
