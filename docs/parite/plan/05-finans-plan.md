# Ekleme Planı — 05a-finans-fatura + 05b-finans-kasa

**Ekran sayısı (eksikler.json, bu iki modül):** 33 (05a: 6, 05b: 27 — 05b'nin 3 `🩹 CANLI BOZUK`
ekranı `bakiye_islem_ara.aspx`/`banka_para_islem.aspx`/`dis_banka_entegrasyon_listesi.aspx`
eksikler.json'da yok, kıyas yapılamadığından planlanmadı; modül dosyasındaki 30 rakamı bunları içerir.)

**Desen dağılımı (33 ekran, birincil etiket — karma olanlar alt maddelerde ayrıştırılır):**
D1=2 · D2=3 · D3=13 · D4=4 · D5=6 (2'si TEMEL PR, 4'ü ekran-bazlı) · D7=1 · D8=4 (tam bloke;
+2 ekranda kısmi bloke alt-madde)

**Toplam efor (yapısal, bloke hariç, 2 TEMEL PR dahil):** ~45,5 gün
- 05a-finans-fatura: ~15 gün
- 05b-finans-kasa: ~30,5 gün (bunun 5 günü TEMEL PR-A — model temeli)

**Bloke (efor dışı):** 4 ekran tam bloke (`banka_islem_ara`, `banka_islemleri`, `ceza_gecis_listesi`,
`hgs_gecis_listesi`) + 2 ekranda kısmi-bloke alt-madde (`fatura.aspx` e-Fatura/GİB alanları,
`fatura_islem_listesi.aspx` XML export) — hepsi e-Fatura/GİB, POS/sanal-pos veya e-Devlet/HGS
kimliği gerektiriyor (CLAUDE.md §9).

**PARA — Opus'a devir (yapısal iskelet bu planda kurulur, tutar/formül/idempotency doğruluğu Opus
incelemesine kalır):** `bakiye_islem`, `banka_virman`/`kasa_virman` (TEMEL PR-A içinde), `cezalar`
(tutar/sebep), `ceza_listesi` (kısmi ödeme alt-maddesi), `gider_islemleri` ("Kalan" alt-maddesi),
`fatura.aspx`-b, `fatura_islem_listesi.aspx`-b, `gelen_e_fatura_listesi.aspx`-b.

---

## ÇAPRAZ-MODÜL UYARISI — 02-tanim-master-plan.md ile çakışma

`02-tanim-master-plan.md` (aynı koşunun kardeş planı) iki ekranda **aynı koda** dokunuyor ve
onları gerçek maliyetinden **çok daha ucuz** tahmin ediyor, çünkü aşağıdaki TEMEL PR-A'nın kök
nedenini (ledger'da hesap-bazlı ayrım yok) görmeden yalnız UI katmanını planlamış:

- **`hesap_para_islem.aspx`** (o plan, "efor: 1 gün yapısal") → `Finance/KasaHub.razor` +
  `CashService.cs` üzerinde IBAN/Hesap No seçimi ekliyor. Ama `CashService.TransferAsync`'teki
  `if (kaynak == hedef) throw` kontrolü **enum seviyesinde** (`LedgerAccountType.Kasa/Banka`) —
  forma bir `<select>` eklemek kaynağı çözmez; `AccountLedgerEntry.AccountRef` Kasa/Banka için HER
  ZAMAN `null` yazılıyor (bkz. TEMEL PR-A). Bu ekranın gerçek maliyeti aşağıdaki TEMEL PR-A'nın
  KENDİSİ (~5 gün) — 1 günlük ayrı bir iş değil.
- **`genel_kasa.aspx`** (o plan, "efor: 1 gün yapısal") → `Reports/KasaBankaDefteri.razor` +
  `ReportService.cs`'e "çoklu-kasa checkbox listesi" + "Kasa Kodu kolonu" ekliyor. Aynı kök neden:
  ledger'da hangi spesifik hesaba ait olduğu hiç kaydedilmiyor — bu bir filtre/kolon eksikliği
  değil, VERİ eksikliği. TEMEL PR-A tamamlanmadan bu iş yapılamaz.

**Öneri:** Bu iki 02-tanim-master ekranını TEMEL PR-A'nın **ardından**, onun üzerine ince bir UI
katmanı olarak yeniden kapsamla (gerçek ek maliyetleri o zaman ~0,5 gün'e düşer — asıl iş TEMEL
PR-A'da yapılmış olur). 02-tanim-master planını yürüten kişi bu notu görmeli; aksi halde aynı
`CashService.cs`/`KasaBankaDefteri.razor` dosyasında iki bağımsız PR çakışır.

Ayrıca **`kasa_tanimi.aspx`** (bu modül, aşağıda) ile 02-tanim-master'ın **`hesap_no_tanimlama.aspx`**
AYNI canlı boşluğu (Kasa/Banka hesabı için "Uyarı Mail Listesi") işaret ediyor ve o plan zaten
`FinancialAccount.UyariMailListesi` alanını PR-A'sına almış — bkz. aşağıda, efor orada sayılı,
burada tekrar sayılmadı.

---

## TEMEL PR-A: Hesap-bazlı (FinancialAccount) kasa/banka defteri — Yapısal Bulgu #1

### Kök neden (kod kanıtı)
`LedgerAccountType` (`src/RentACar.Domain/Enums/LedgerAccountType.cs`) yalnız `Kasa=1`/`Banka=2`
İKİ değeri tutuyor — IBAN/hesap bazlı ayrım yok. `AccountLedgerEntry.AccountRef` (nullable Guid,
Cari/Depozito/Gider için zaten "hangi varlık" ayrımını taşıyor) Kasa/Banka satırlarında HER ZAMAN
`null` yazılıyor — grep ile doğrulandı (`CashService.cs` 3 yer, `ExpenseService.cs`,
`AracKrediService.cs`, `RegulationService.cs` 3 yer, `DepozitoEndpoints.cs`, hepsi
`AccountRef = null` sabit). `FinancialAccount` (`/hesaplar`) tablosu IBAN/HesapNo/Banka/Sube
alanlarını ZATEN taşıyor (roadmap K1'de eklenmiş) ama **hiçbir yazma-yolu bunu okumuyor** — tanım
ile işlem tarafı bağlı değil (module dosyasının "wire-in eksikliği" notu doğru).
`CashService.TransferAsync`'teki `if (kaynak == hedef) throw` de enum-seviyesinde — iki farklı
banka hesabı (ikisi de `Tur=Banka`) sistemin gözünde AYNI "hedef" olduğundan virman engelleniyor.

### Eylem
1. `CashTransaction` (`src/RentACar.Domain/Entities/CashTransaction.cs`): `KarsiHesap` enum'ının
   YANINA nullable `HesapId` (Guid?, `FinancialAccount` FK) eklenir. Enum SİLİNMEZ (geriye dönük
   satırlar + Kasa/Banka türü hâlâ enum'dan okunur — `HesapId` yalnız hangi SPESİFİK hesap olduğunu
   ekler).
2. `CashInput.cs`, `ExpenseInput.cs`: `Hesap`/`KasaBankaHesap` enum alanının yanına opsiyonel
   `HesapId` eklenir (boş → eski davranış, legacy `AccountRef=null`).
3. `AccountLedgerEntry` üretimi (`CashService.Natural`, `CashService.TransferAsync`,
   `ExpenseService.cs` ilgili metod, `AracKrediService.TaksitOdeAsync`, `RegulationService.cs` 3
   metod, `DepozitoEndpoints.cs`) — Kasa/Banka bacağının `AccountRef`'i `HesapId` verilmişse ONU,
   verilmemişse `null` (legacy) yazar.
4. `CashService.TransferAsync`: `kaynak == hedef` kontrolü `(kaynak, kaynakHesapId) ==
   (hedef, hedefHesapId)` şekline genişler — HesapId farklıysa aynı `LedgerAccountType.Banka` iki
   FARKLI hesap arası virman artık MÜMKÜN.
5. Bakiye/rapor sorguları: `ReportService.GetKasaBankaSummaryAsync`/`GetAccountLedgerAsync`
   (`src/RentACar.Application/Reporting/ReportService.cs` ~L388-420) hesap-bazlı filtre/gruplama
   alır (`AccountRef` doluysa o hesaba, `null` ise "tanımsız/legacy" kovaya toplanır — İKİ KOVA
   karışık toplanmaz, ayrı satır).
6. Form katmanı: `Finance/KasaHub.razor` (virman formu, Kaynak/Hedef select'leri
   `FinancialAccountService.ListActiveAsync()`'ten beslenir, K/H_Banka_Kuru+K/H_Banka_Doviz alanları
   eklenir), `Reports/KasaBankaDefteri.razor` (Hesap select'i tip'ten spesifik hesaba genişler),
   `Finance/FinanceEndpoints.cs` (`/finans/virman`, `/finans/tahsilat`, `/finans/odeme` — HesapId
   form alanı okunur ve servise geçilir), `Expenses/ExpenseList.razor` + endpoint (Kasa_Kodu/Hesap_No
   spesifik seçim), `AracKredileri/AracKrediEndpoints.cs`, `Regulation/RegulationEndpoints.cs`,
   `Finance/DepozitoEndpoints.cs` (hepsi `hesap` string parametresini şimdi enum'a çeviriyor —
   yanına `hesapId` eklenir).
7. Migration: yalnız `CashTransactions.HesapId` (nullable) + `AccountLedgerEntries` tarafında YENİ
   kolon YOK (mevcut nullable `AccountRef` kullanılıyor) — ADDITIVE, geriye dönük veri bozulmaz.
   **Davranış kararı (Opus):** eski satırlar (`AccountRef=null`) yeni "tanımsız hesap" kovası olarak
   mı kalacak yoksa backfill ile bir "Varsayılan Kasa"/"Varsayılan Banka" `FinancialAccount`'a mı
   atanacak — bakiye sürekliliğini etkiler, karar gerektirir.

### Dokunulacak (doğrulanmış)
`src/RentACar.Domain/Entities/CashTransaction.cs`, `src/RentACar.Domain/Enums/LedgerAccountType.cs`
(değişmez, yalnız referans), `src/RentACar.Application/Finance/CashService.cs`,
`src/RentACar.Application/Finance/CashInput.cs`,
`src/RentACar.Application/Expenses/ExpenseService.cs`,
`src/RentACar.Application/Expenses/ExpenseInput.cs`,
`src/RentACar.Application/AracKredileri/AracKrediService.cs`,
`src/RentACar.Application/Regulation/RegulationService.cs`,
`src/RentACar.Application/Reporting/ReportService.cs`,
`src/RentACar.Infrastructure/Persistence/Repositories/CashRepository.cs` (PostAsync/PostBatchAsync
değişmez ama `HesapId` alanı SaveChanges'e girer),
`src/RentACar.Web/Components/Pages/Finance/KasaHub.razor`,
`src/RentACar.Web/Components/Pages/Reports/KasaBankaDefteri.razor`,
`src/RentACar.Web/Finance/FinanceEndpoints.cs`, `src/RentACar.Web/Finance/DepozitoEndpoints.cs`,
`src/RentACar.Web/AracKredileri/AracKrediEndpoints.cs`, `src/RentACar.Web/Regulation/RegulationEndpoints.cs`,
`src/RentACar.Web/Components/Pages/Expenses/ExpenseList.razor`, yeni migration.

### Efor
**~5 gün** (10+ çağrı noktası + 2 rapor sorgusu + 3 form + migration + geriye-dönük veri kararı).
**ZORUNLU adversarial inceleme** (D5 kuralı — çift-sayım, RLS, mevcut bakiyelerin bölünmemesi).

### Bağımlılık
Yok (öncelikli — aşağıdaki 7 ekran + 02-tanim-master'ın 2 ekranı buna bağımlı).

---

## TEMEL/PR-benzeri: Virman geçmişi listelenebilirliği — Yapısal Bulgu #2

### Kök neden (kod kanıtı)
`CashService.TransferAsync` (Kasa↔Banka) **`CashTransaction` yazmıyor**, yalnız iki
`AccountLedgerEntry` postluyor (`SourceType="Virman"`, ortak `SourceId`). `/kasa` sayfasının "Son
İşlemler" tablosu `CashService.ListAsync()` → `CashTransactions` tablosunu okuyor → virman hiç
görünmüyor. `TransferBetweenCariAsync` (cari↔cari) da aynı desende ama `SourceType="CariVirman"`.

### İki seçenek (ucuzdan pahalıya)
**Seçenek A (önerilen, bu planın efor sayımı bunu baz alır):** YENİ SORGU — mevcut şema hiç
değişmez. `AccountLedgerEntry` tablosu `WHERE SourceType IN ('Virman','CariVirman')` filtrelenip
`SourceId`'ye göre çift (Borç/Alacak bacağı) gruplanır → bir rapor/liste DTO'su üretir. Kayıt
numarası yoksa `SourceId`'nin kısaltması veya tarih+sıra gösterilir (canlının "Kayit No" tam
sıra-numarası PARİTESİ olmaz ama işlevsel liste olur). **Risk yok** — okuma-yolu, şema değişmez.

**Seçenek B (tam parite, daha pahalı):** `CashTransactionType`'a `Virman` eklenir, `CashTransaction`
şemasında `CariId` (şu an non-nullable `Guid`) NULLABLE'a çevrilir (virmanda cari yok), gerçek
`VR-000001` sıra no'su `SequenceAllocator`'dan tahsis edilir. Bu, `CashTransaction.CariId`'yi
tüketen HER yeri etkiler (`KasaHub.razor` `_adlar` sözlük araması, `ListExportCatalog.NakitIslemler`,
`GetRentalIslemSayilariAsync` vb.) — blast-radius büyük, D5 zorunlu adversarial gerekir.

Bu plan **Seçenek A**'yı alır (D4, şema riski yok); Seçenek B kullanıcı gerçek sıra-no isterse
ayrı bir eskalasyon olarak not edilir.

### Efor
**~1 gün** (Seçenek A) — bkz. `banka_virman_islem_ara.aspx` altında, bu iş O EKRANIN kendisidir,
ayrı sayılmadı.

---

# 05a-finans-fatura (6 ekran)

## PR: Manuel Fatura formu — bilgi alanları + PARA alanları + e-Fatura bloke

### fatura.aspx
- **desen:** D5 (karma — bkz. a/b/c)
- **eylem:**
  - **(a) Bilgi alanları (D1/D2, PARA değil):** `InvoiceList.razor`'daki 6 alanlı manuel-fatura
    formuna `Islem_Sube` (metin/Şube seç), `Fatura_Saat` (tarih yanına saat inputu), `Evrak_No`,
    `Fatura_Ozel_Kod`, `Odeme_Turu` (Kart/Havale/Nakit), `Gonderim_Sekli` (Mail/Kargo/Posta),
    `Kdv_Sifir_Sebep` (4 seçenekli select) eklenir — `Invoice` entity'sine karşılık gelen nullable
    string/enum alanları eklenir (yalnız ekleme, defter postlaması değişmez).
  - **(b) Domain'de VAR ama forma hiç bağlanmamış PARA alanları (D5, PARA — Opus):**
    `Invoice.Otv`/`TevkifatOran`/`TevkifatTutar`/`DamgaVergisi`/`KaynakFaturaId` şu an entity'de
    dolu olabilir ama `InvoiceList.razor` formu bunları hiç sormuyor/göstermiyor VE
    `InvoiceService.cs`'teki ledger postlama (`Borç Cari / Alacak Gelir+KDV`) bu alanları
    HESABA KATMIYOR (dosya içi not: "v1'de ledger'a yansımaz"). Bu alanları forma açmak +
    dengeli deftere yansıtmak (Tevkifat/ÖTV/Damga ayrı ledger satırı mı, yoksa GenelToplam'a mı
    dahil) **PARA — Opus** kararı gerektirir; yapısal iskelet (form alanları + servis parametresi)
    bu PR'da kurulur, defter formülü Opus'a bırakılır.
  - **(c) e-Fatura/GİB'e özgü alanlar (D8 bloke):** `EFatura_Firma`, `Cari_Entegre`,
    `EFatura_Tipi`/`Fatura_Sablon`/`EFatura_ID`/`FaturaSonDurum`, manuel çoklu döviz girişi
    (EURO/USD/GBP), tenant-config bayrakları (`Fatura_Kesmez`, `Kll_Fatura_Degismez` vb.) —
    gerçek e-Fatura/GİB entegratör kimliği gerekmeden anlamsız.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Finance/InvoiceList.razor`,
  `src/RentACar.Application/Finance/InvoiceService.cs`, `src/RentACar.Domain/Entities/Invoice.cs`
  (yalnız (a) için yeni alanlar; (b) alanları ZATEN var), `src/RentACar.Web/Finance/FinanceEndpoints.cs`
  (`/finans/fatura-manuel`)
- **efor:** (a) 1 gün + (b) 2,5 gün (PARA — Opus) = **3,5 gün**; (c) bloke
- **bağımlılık:** yok

## PR: Fatura Detay Listesi (yeni ekran)

### fatura_detay_listesi.aspx
- **desen:** D3 (veri var — `InvoiceLine` zaten Aciklama/Miktar/BirimNetFiyat/KdvOrani/SatirNet/
  SatirKdv/SatirToplam tutuyor — görünmüyor; ama 46 kolonun çoğu Customer/Rental/Reservation ile
  JOIN gerektirdiğinden normal D3'ün üstünde efor)
- **eylem:** Yeni `src/RentACar.Web/Components/Pages/Finance/InvoiceLineList.razor` (rota:
  `/faturalar/detay-listesi`) — `InvoiceLine` × `Invoice` × `Customer` × `Rental`/`Reservation`
  JOIN'iyle satır-bazlı grid (Cari Ünvan/Adres/Şehir/Mail, Evrak No/Tarih, Plaka, Kira Çıkış
  Ofisi/Detayı/RA No, Rezervasyon Kaynağı, Satır Net/KDV/Toplam, İptal, Vade Tarihi). Filtre: Cari,
  Fatura No aralığı, Plaka, Tarih, Ofis. Export ucu (`ListExportCatalog`'a yeni `FaturaDetaylari`
  metodu). Yeni tablo YOK — mevcut `InvoiceLine`/`Invoice`/`Customer`/`Rental` okunuyor.
- **dokunulacak:** yeni `Components/Pages/Finance/InvoiceLineList.razor`, yeni
  `src/RentACar.Application/Finance/InvoiceService.cs` metodu (`ListLinesAsync` benzeri, filtreli),
  `src/RentACar.Web/Reports/ListExportCatalog.cs` (yeni `FaturaDetaylari` tablosu),
  `src/RentACar.Web/Components/Layout/MainLayout.razor` (nav)
- **efor:** 1,5 gün
- **bağımlılık:** yok

## PR: Fatura Raporları Derinliği (Dönem + KDV)

İki rapor da `Reports/ReportService.cs`+`ReportRepository.cs` üzerinde D4 genişletme; aynı ailede
gruplandı.

### fatura_donem_raporu.aspx
- **desen:** D4
- **eylem:** `ReportRepository.GetFaturaDonemRowsAsync` (şu an yalnız kesilmiş `Invoice`'ları tarih
  aralığına göre listeliyor) **faturalanmamış kira** modu eklenir: `Rental` tablosu `LEFT JOIN
  Invoice` (RentalId eşleşmeyen + `KaynakKiraId` fark faturası da yoksa) filtresiyle "Fatura_Durum=
  Faturalanmamış" sekmesi. Kolon: `Kayit No` (Rental.SozlesmeNo), `Faturalanan` (bool), `Plaka`,
  Baş/Bit Tarih. Filtre: Cari Ara, `Fatura_Durum` (Faturalanmamış/Faturalanmış), İşlem Şube.
- **dokunulacak:** `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
  (`GetFaturaDonemRowsAsync`, ~L197), `src/RentACar.Application/Reporting/ReportService.cs`
  (`GetFaturaDonemAsync`, ~L708), `src/RentACar.Web/Components/Pages/Reports/FaturaDonem.razor`
- **efor:** 1,5 gün
- **bağımlılık:** yok

### kdv_raporu.aspx
- **desen:** D4 (yapısal uyumsuzluk — sapma notu)
- **eylem:** Canlı SATIR=fatura/sütun=oran (geniş format) + Satış/Alış (`Fatura_Turu`) ayrımı
  taşıyor; bizim `GetKdvLineRowsAsync` (`ReportRepository.cs` ~L320) yalnız SATIŞ (`Invoice`)
  tablosunu tarıyor, ALIŞ (`GelenEFatura`) hiç dahil değil. İki iş: (1) satır-bazlı geniş format
  (her fatura bir satır, oran sütunlarına dağıtılmış Tutar/KDV) yeni bir görünüm olarak eklenir —
  MEVCUT özet/pivot format SİLİNMEZ (bizim fazlamız, canlıda yok ama işe yarıyor), yeni bir sekme/
  mod olur; (2) `GelenEFatura` (Alış) KDV'si rapora dahil edilir — ama `GelenEFatura`'da şu an oran
  kırılımı yok (bkz. `gelen_e_fatura_listesi.aspx`-a), o PR ÖNCE gitmeli.
- **dokunulacak:** `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
  (`GetKdvLineRowsAsync`), `src/RentACar.Application/Reporting/ReportService.cs`
  (`GetKdvListesiAsync`, ~L539), `src/RentACar.Web/Components/Pages/Reports/KdvListesi.razor`
- **efor:** 2 gün (normal D4'ün üstünde — iki-format + iki-kaynak birleşimi)
- **bağımlılık:** `gelen_e_fatura_listesi.aspx`-a (KDV oran kırılımı) — Alış-KDV dahil etmeden önce.

## PR: Fatura Listesi — filtre/kolon + toplu faturalama (bloke: XML)

### fatura_islem_listesi.aspx
- **desen:** D5 (karma — bkz. a/b/c)
- **eylem:**
  - **(a) Filtre + kolon (D3):** `InvoiceList.razor`'a filtre paneli (Cari, Fatura No aralığı,
    Plaka Ara, `Fatura_Durumu` Hepsi/Faturalanmayanlar/Faturalananlar, Tarih aralığı, Ofis) +
    kolon (ID, Cari Kod, Vergi Dairesi/No, İptal, Ofis, Müş Özel Kod, Açıklama, Müşteri Ülke,
    Genel Toplam Dvz, Özel Kod) eklenir.
  - **(b) "Seçili Olanları Faturala" toplu aksiyon (D5, PARA — Opus):** Çoklu kira/kayıt seçip
    TEK istekle birden fazla fatura kesme — `InvoiceService`'e `CashService.BatchCollectAsync`
    desenine benzer `BatchCreateAsync` eklenir (ATOMİK, satır-bazlı dengeli, idempotency anahtarı).
    Hangi kiraların "faturalanabilir" sayılacağı ve toplu kesimde KDV/kur çözümü **PARA — Opus**.
  - **(c) "XML'e Aktar" (D8 bloke):** UBL-TR e-Fatura XML şeması gerektirir, entegratör kimliği
    olmadan anlamsız.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Finance/InvoiceList.razor`,
  `src/RentACar.Application/Finance/InvoiceService.cs`, `src/RentACar.Web/Finance/FinanceEndpoints.cs`
- **efor:** (a) 1 gün + (b) 2 gün (PARA — Opus) = **3 gün**; (c) bloke
- **bağımlılık:** yok

## PR: Gelen e-Fatura — KDV oran kırılımı + gider bağlama (+ Sadece Kdv Yansıt: PARA)

### gelen_e_fatura_listesi.aspx
- **desen:** D5 (karma — bkz. a/b)
- **eylem:**
  - **(a) KDV oran kırılımı + araç/gider kategori bağlama (D2):** `GelenEFatura` entity'sine
    oran-bazlı alanlar eklenir (`Kdv20`/`Kdv20Matrah`/`Kdv10`/`Kdv10Matrah`/`Kdv1`/`Kdv1Matrah`/
    `Kdv0`/`Kdv0Matrah`, toplamda `NetTutar`/`KdvTutar` ile TUTARLI olmalı — doğrulama kuralı),
    `VehicleId` (Guid?) + `ExpenseCategoryId`/`Turu` (gider kategorisiyle eşleştirme — Periyodik
    Servis/Hasar-Kaza/Mekanik Arıza/Bakım) eklenir. `GelenEFaturaList.razor`'a bu kolonlar +
    filtre (EFatura_Firma, Fatura No aralığı, Plaka, `Islem_Turu`) eklenir.
  - **(b) "Sadece Kdv Yansıt" (D5, PARA — Opus):** Şu an `GelenEFatura` HİÇ deftere postlamıyor
    (dosya içi not: "gidere dönüştürme para hareketi ileriki adım" — henüz yazılmamış). Bu aksiyon
    gideri değil SADECE alış-KDV'sini (indirilecek KDV) deftere/beyannameye yansıtma — YENİ bir
    ledger-yazan metod (`GelenEFaturaService.YansitKdvAsync` benzeri, Borç Kdv(indirilecek)/Alacak
    Cari veya Gider — hangi karşı hesap **PARA — Opus**). Zorunlu adversarial inceleme.
- **dokunulacak:** `src/RentACar.Domain/Entities/GelenEFatura.cs`, yeni migration,
  `src/RentACar.Application/GelenEFaturalar/GelenEFaturaService.cs`,
  `src/RentACar.Application/GelenEFaturalar/GelenEFaturaInput.cs`,
  `src/RentACar.Web/Components/Pages/GelenEFaturalar/GelenEFaturaList.razor`,
  `src/RentACar.Web/GelenEFaturalar/GelenEFaturaEndpoints.cs`
- **efor:** (a) 1,5 gün + (b) 2 gün (PARA — Opus) = **3,5 gün**
- **bağımlılık:** (a) `kdv_raporu.aspx`'in Alış-KDV dahil etme kısmı BUNA bağımlı (ters yön).

---

# 05b-finans-kasa (27 ekran)

### bakiye_islem.aspx
- **desen:** D5 (PARA — Opus)
- **eylem:** Yeni bağımsız ekran + servis metodu: TEK cari üzerinde, Kasa/Banka'ya DOKUNMADAN
  bakiye ayarlaması (`Islem_Turu=1|2` Alacaklandır/Borçlandır). Karşı hesap ne olacak (Gelir? Gider?
  yeni bir "Muhasebe Düzeltmesi" `LedgerAccountType`?) — dengeyi bozmadan tek-taraflı görünen bu
  düzeltmenin karşı bacağı **PARA — Opus** kararı. Yapısal iskelet: `CashService`'e benzer
  `BakiyeDuzeltmeService.AdjustAsync(cariId, tutar, yon, vade, makbuzNo, aciklama)` + yeni sayfa
  (Musteri arama, Tarih, Vade, Tutar, Döviz, Kur, Makbuz No, Açıklama).
- **dokunulacak:** yeni `src/RentACar.Application/Finance/BakiyeDuzeltmeService.cs`, yeni
  `Components/Pages/Finance/BakiyeDuzeltme.razor`, `src/RentACar.Web/Finance/FinanceEndpoints.cs`
- **efor:** 2 gün (PARA — Opus)
- **bağımlılık:** yok

### banka_hesap_hareketleri.aspx
- **desen:** D3
- **eylem:** D9 (erişilebilirlik) KISMI ZATEN ÇÖZÜLDÜ — `/raporlar/kasa-banka` bugün nav'a eklendi
  (`MainLayout.razor`, commit `d08810c`). Kalan gerçek eksik: Hesap_No (spesifik IBAN), Cari Bilgi,
  Döviz, İşlem Türü, Şube kolonları — TEMEL PR-A'nın (`AccountRef`→`FinancialAccount`) ürünüdür.
  PR-A tamamlandıktan sonra `KasaBankaDefteri.razor`'a bu kolonlar + `Devir_Goster` (açılış bakiyesi
  gösterme anahtarı) eklenir.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Reports/KasaBankaDefteri.razor`,
  `src/RentACar.Application/Reporting/ReportService.cs`
- **efor:** 1 gün
- **bağımlılık:** TEMEL PR-A

### banka_islem_ara.aspx
- **desen:** D8 — **bloke**
- **gerekçe:** Kredi kartı/sanal-POS altyapısı (`banka_islemleri.aspx` ile aynı kök) olmadan
  listelenecek veri yok.
- **bağımlılık:** D8 — banka POS/sanal-pos entegratör kimliği gerekir.

### banka_islemleri.aspx
- **desen:** D8 (kısmi — bkz. not)
- **eylem:** Canlının 4 alt-akışından (`Islem_Turu=1..4`) 2'si ZATEN bizde farklı ekranlardan
  karşılanıyor (Depozit Kapama → `/depozito`; Gelen/Giden Havale → `/finans/tahsilat`+`/finans/odeme`
  Banka hesabıyla, TEMEL PR-A sonrası spesifik hesap seçimiyle) — bunlar için EK İŞ YOK. Kalan 2
  alt-akış (Kredi Kartı Tahsilatı/İade, Provizyon: Kart_No/CVV/Taksit/3D/Sanal_Pos_Deger/Odemelink)
  kart bilgisi işleme/saklama gerektirdiğinden PCI-DSS + banka POS entegratör kimliği ister.
- **dokunulacak:** (ek iş yok kısmı) —
- **efor:** 0 (kapsanan kısım) + bloke (POS kısmı)
- **bağımlılık:** D8 — banka POS/sanal-pos entegratör kimliği.

### banka_nakit_listesi.aspx
- **desen:** D3
- **eylem:** `KasaBankaDefteri.razor`'a Hesap_No (spesifik IBAN) filtresi — TEMEL PR-A ürünü, ek
  kolon/sorgu değişikliği minimal (aynı sayfa, PR-A'nın filtre altyapısını kullanır).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Reports/KasaBankaDefteri.razor`
- **efor:** 0,5 gün
- **bağımlılık:** TEMEL PR-A

### banka_para_listesi.aspx
- **desen:** D3
- **eylem:** `banka_nakit_listesi.aspx` ile AYNI sayfa/aynı iş (`KasaBankaDefteri.razor` Hesap_No
  filtresi) — canlıda iki farklı aspx aynı temel veriyi (banka hareketleri) farklı ön-filtreyle
  gösteriyor olabilir; bizde TEK sayfa + filtre kombinasyonu yeterli.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Reports/KasaBankaDefteri.razor`
- **efor:** 0,5 gün
- **bağımlılık:** TEMEL PR-A
- **gruplama:** `banka_hesap_hareketleri` + `banka_nakit_listesi` + `banka_para_listesi` +
  `kasa_dagilimi` — PR-A'nın ARDINDAN TEK PR'da `KasaBankaDefteri.razor` derinliği olarak birleşir.

### banka_virman.aspx
- **desen:** D5 (TEMEL PR-A kapsıyor)
- **eylem:** Bu ekranın YAPISAL kısıtı (iki farklı banka hesabı arası virman imkânsız) TEMEL PR-A
  ile çözülür — ayrı ek iş YOK. PR-A sonrası `KasaHub.razor`'daki Kaynak/Hedef select'leri
  `FinancialAccountService.ListActiveAsync()`'ten spesifik hesap listesi gösterir.
- **dokunulacak:** (TEMEL PR-A kapsamında)
- **efor:** 0 (PR-A içinde)
- **bağımlılık:** TEMEL PR-A

### banka_virman_islem_ara.aspx
- **desen:** D4
- **eylem:** Yapısal Bulgu #2, **Seçenek A** (yukarıda) — yeni sorgu:
  `AccountLedgerEntry WHERE SourceType='Virman'` çift-bacak `SourceId`'ye göre gruplanır → liste
  DTO'su (Tarih, Kaynak/Hedef Hesap [PR-A'dan `AccountRef`], Tutar, Döviz). Yeni sayfa
  `Reports/VirmanGecmisi.razor` (rota: `/raporlar/virman-gecmisi`) + filtre (Tarih aralığı).
- **dokunulacak:** yeni `src/RentACar.Application/Reporting/ReportService.cs` metodu
  (`GetVirmanGecmisiAsync`), yeni `Components/Pages/Reports/VirmanGecmisi.razor`,
  `src/RentACar.Web/Components/Layout/MainLayout.razor` (nav)
- **efor:** 1 gün
- **bağımlılık:** TEMEL PR-A (hesap adının gösterilmesi için); PR-A olmadan da SourceId-bazlı liste
  çalışır ama "Kaynak/Hedef Hesap No" kolonu boş kalır.

### cari_virman.aspx
- **desen:** D2
- **eylem:** `CariVirman.razor` formuna Tarih (manuel — şu an sunucu "şimdi"), Vade, Makbuz_No,
  İşlem Şube, İşlem Yapan alanları eklenir. `TransferBetweenCariAsync`'e `tarih`/`vade`/`makbuzNo`
  parametreleri eklenir (mevcut dengeli-çift-kayıt mantığı DEĞİŞMEZ — yalnız ek bilgi alanları).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Finance/CariVirman.razor`,
  `src/RentACar.Application/Finance/CashService.cs` (`TransferBetweenCariAsync`),
  `src/RentACar.Web/Finance/FinanceEndpoints.cs` (`/finans/cari-virman`)
- **efor:** 1 gün
- **bağımlılık:** yok

### cari_virman_islem_ara.aspx
- **desen:** D3
- **eylem:** Veri ZATEN var (`TransferBetweenCariAsync` `SourceType="CariVirman"` ile ledger'a
  yazıyor, her iki carinin kendi ekstresinde görünüyor) — TÜM cariler arası birleşik liste yok.
  Yeni sorgu: `AccountLedgerEntry WHERE SourceType='CariVirman'` çift-bacak `SourceId`'ye göre
  gruplanır (Kaynak Cari=Credit bacağı, Hedef Cari=Debit bacağı) → liste. Yeni sayfa/rota
  (`/cari-virman/gecmis` veya `CariVirman.razor`'a ikinci bir tablo).
- **dokunulacak:** `src/RentACar.Application/Finance/CashService.cs` (yeni `ListTransfersAsync`),
  `src/RentACar.Web/Components/Pages/Finance/CariVirman.razor`
- **efor:** 0,5 gün
- **bağımlılık:** yok

### ceza_gecis_listesi.aspx
- **desen:** D8 — **bloke**
- **gerekçe:** Canlı ekran esasen bir e-Devlet/UYAP scraping entegrasyonu (TC_Kimlik+Sifre ile
  hükümet portalından otomatik ceza çekme) — basit bir liste ekranı değil.
- **bağımlılık:** D8 — e-Devlet/UYAP kimliği.

### ceza_listesi.aspx
- **desen:** D3 (karma — bkz. a/b)
- **eylem:**
  - **(a) Filtre + temel kolon (D3):** `PenaltyList.razor`'a filtre paneli (Musteri_No/Ad_Soyad,
    Makbuz_No, Plaka, Tarih aralığı, `Ceza_Durum`, `Odeme_Durum`, Ofis) + kolon (Mail Adresi,
    Sözleşme No, Fatura Tar./No, İşlem Şube, Rez. Kaynağı, Ödeme Tarihi/Şekli) eklenir.
  - **(b) Kısmi ödeme/bakiye takibi (D2, PARA-bitişik):** `Penalty`'ye `OdenenTutar`/`Kalan`
    (hesaplanan) alanı + `CezaDurum`'a `Kismi` değeri eklenir; ödeme kaydı YENİ ledger satırı
    yazmaz (ceza yansıtma zaten var), yalnız TAKİP alanı — ama "kısmi ödeme" ile "tam ödeme"
    arasındaki tutar mutabakatı **PARA — Opus** notu taşır (yanlış "Kalan" hesap-şeması para
    kaybına yol açabilir).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Penalties/PenaltyList.razor`,
  `src/RentACar.Domain/Entities/Penalty.cs`, `src/RentACar.Domain/Enums/CezaDurum.cs`,
  `src/RentACar.Application/Penalties/PenaltyService.cs`, yeni migration
- **efor:** (a) 0,5 gün + (b) 1,5 gün = **2 gün**
- **bağımlılık:** yok
- **not:** Resim/PDF kanıt yükleme YOK — `cezalar.aspx`'teki FileUpload maddesiyle birlikte
  değerlendirilir (bkz. aşağıda), mevcut Belge Merkezi altyapısına (`docs`/memory "belge-paylasim-
  serisi") bağlanabilir.

### cezalar.aspx
- **desen:** D2 (karma — bkz. a/b/c)
- **eylem:**
  - **(a) Çok-satırlı ceza (D2):** Canlı 3 ayrı `Ceza_Tutari1-3`/`Ceza_Sebebi1-3` satırı taşıyor
    (tek kayıtta birden çok ceza maddesi); bizde tek `Tutar`/`Sebep`. `Penalty`'ye `List<PenaltySatir>`
    (Tutar, Sebep) alt-tablo eklenir VEYA (daha basit) `Penalty` başına 1-3 arası ayrı kayıt açma
    UX'i (form'da "+ Satır Ekle" JS, her satır ayrı `Penalty` POST'u) — hangisi CLAUDE.md §5
    "yeni tenant-owned tablo" reçetesine daha uygunsa (alt-tablo) o seçilir.
  - **(b) Dosya/resim kanıt yükleme (D1, mevcut altyapıya bağlı):** `cezalar.aspx`'e FileUpload
    eklenir — repo'da zaten bir Belge Merkezi/PDF dağıtım altyapısı var (memory:
    "belge-paylasim-serisi") ise ona bağlanır, yoksa yeni blob-storage entegrasyonu (küçük).
  - **(c) Ceza_Saat/Ceza_Yeri/Cep_Tel/Makbuz_No/Islem_Sube/Odenme_Tarih (D1 bilgi alanları).**
  - Tutar/sebep birden çok satıra bölünmesinin ceza yansıtma (`PenaltyService.YansitAsync`) ile
    nasıl toplanacağı **PARA — Opus**.
- **dokunulacak:** `src/RentACar.Domain/Entities/Penalty.cs`, yeni migration,
  `src/RentACar.Application/Penalties/PenaltyService.cs`,
  `src/RentACar.Web/Components/Pages/Penalties/PenaltyList.razor`
- **efor:** (a) 1,5 gün + (c) 0,5 gün = **2 gün**; (b) mevcut altyapıya bağlı, ayrı doğrulama gerekir
- **bağımlılık:** yok

### extre_ozeti.aspx
- **desen:** D4
- **eylem:** Yeni satır-bazlı rapor: müşteri+plaka+vade bazlı açık-tutar satırları (Invoice.VadeTarihi
  + Rental.VehicleId JOIN). `/raporlar/cari-bakiye` (toplu bakiye) ile KARIŞTIRILMAZ — bu satır
  seviyesi. Yeni sayfa `/raporlar/extre-ozeti` + filtre (Ofis, Tarih).
- **dokunulacak:** yeni `ReportRepository.cs` metodu, yeni `ReportService.cs` metodu, yeni
  `Components/Pages/Reports/ExtreOzeti.razor`, nav
- **efor:** 1,5 gün
- **bağımlılık:** yok

### genel_borc_alacak.aspx
- **desen:** D3
- **eylem:** `CariBakiye.razor`'a filtre paneli (Cari Ara, Özel Kod, Firma Seç, Sözleşme Durumu,
  Ofis, Borç Türü, Döviz, Min Tutar) + Telefon/Mail Adresi/Banka kolonları + ayrı Borç/Alacak
  kolonları (şu an nette birleşik `Bakiye`) eklenir. **Mutabakat Gönder/Mutabakat** (e-mutabakat
  akışı) BU PR'A DAHİL DEĞİL — ayrı bir dikey (D7, e-posta gönderim altyapısı + onay iş akışı),
  ayrı değerlendirilmeli.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Reports/CariBakiye.razor`,
  `src/RentACar.Application/Reporting/ReportService.cs` (`GetCariBalancesAsync`)
- **efor:** 1 gün (mutabakat hariç)
- **bağımlılık:** yok

### gider_ara.aspx
- **desen:** D3
- **eylem:** `ExpenseList.razor`'a filtre paneli (Cari Ara, Tarih aralığı, Ofis, `Gider_Iade`,
  Plaka Ara, Gider Adı) eklenir. Servis/hasar-özel kolonlar (İşlem KM, Dönüş KM, Hasar Dosya No,
  Yansıtma/Garanti Tutarı) `ServiceRecord`/`Regulation` (hasar) tablolarından JOIN gerektirir —
  bu VERİ zaten var ama farklı entity'lerde; multi-entity JOIN nedeniyle normal D3'ün biraz üstünde.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Expenses/ExpenseList.razor`,
  `src/RentACar.Application/Expenses/ExpenseService.cs`
- **efor:** 1 gün
- **bağımlılık:** yok

### gider_islemleri.aspx
- **desen:** D3 (karma — bkz. a/b/c)
- **eylem:**
  - **(a) Bilgi alanları (D3):** `Odeme_Tarihi` (ayrı ödeme tarihi), `Hazir_Aciklama` (şablon
    açıklama), `Sozlesme_No` (kiraya bağlama, `RentalId` FK), `Islem_Yapan` eklenir.
  - **(b) "Kalan" kısmi ödeme takibi (D2, PARA-bitişik):** `Expense`'e `OdenenTutar`/`Kalan`
    eklenir — gider hep tam ödenmiş varsayımı kırılır. Kısmi ödeme kaydı defter postlamasını
    nasıl böler (tam tutar Borç Gider yazılıp Alacak Kasa/Cari kısmi mi, yoksa iki aşamalı mı)
    **PARA — Opus**.
  - **(c) Spesifik kasa/banka hesabı seçimi:** TEMEL PR-A ürünü, ek iş yok.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Expenses/ExpenseList.razor`,
  `src/RentACar.Domain/Entities/Expense.cs`, yeni migration,
  `src/RentACar.Application/Expenses/ExpenseService.cs`
- **efor:** (a) 1 gün + (b) 1,5 gün (PARA — Opus) = **2,5 gün**
- **bağımlılık:** (c) TEMEL PR-A

### gider_tanimlama.aspx
- **desen:** D1 (mikro düzeltme)
- **eylem:** `ExpenseCategoryList.razor`'daki liste tablosunun `<thead>` satırına `<th>Tür</th>`,
  her satıra `<td>@c.Tur</td>` eklenir — `Tur` alanı `ExpenseCategory` entity'sinde VE edit
  formunda ZATEN var, sadece salt-okunur listede gösterilmiyor. Kod/migration DEĞİŞMEZ.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/ExpenseCategories/ExpenseCategoryList.razor`
  (tek dosya, iki satır ekleme)
- **efor:** ~10 dakika
- **bağımlılık:** yok

### hesap_extresi.aspx
- **desen:** D3
- **eylem:** `/cariler/{Id}/ekstre` (`CustomerStatement.razor`) çekirdek işlev (tarih/borç/alacak/
  bakiye + export + ters kayıt) ÇALIŞIYOR — eksik olan filtre/görünüm-modu paneli: Tarih aralığı
  filtresi, Döviz filtresi, "Toplu/Taksitli" görünüm modu, Sözleşme durumu filtresi. Musteri_No/Ad/
  Soyad arama kutusu EKLENMEZ (bizde zaten URL parametresiyle önceden seçilmiş cari — bu bilinçli
  fark, canlı fazlası olarak not edilir, gerileme sayılmaz).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Customers/CustomerStatement.razor`
- **efor:** 1 gün
- **bağımlılık:** yok

### hgs_gecis_listesi.aspx
- **desen:** D8 — **bloke**
- **gerekçe:** `IHgsService.GetCrossingsAsync` v1 STUB (gerçek adaptör yok, HİÇBİR geçiş verisi
  kalıcı olarak SAKLANMIYOR — `HgsReflectionService` her çağrıda stub'tan boş liste alır). Kalıcı
  bir `HgsGecis` tablosu yok; bağımsız/filtrelenebilir bir liste ekranı yazmak, altında GERÇEK veri
  olmadan israf olur.
- **bağımlılık:** D8 — HGS gerçek adaptör kimliği (entegratör API erişimi).

### kasa_dagilimi.aspx
- **desen:** D3
- **eylem:** `banka_hesap_hareketleri` grubuna katılır (bkz. yukarıdaki gruplama notu) — PR-A
  sonrası `KasaBankaDefteri.razor`'a Kasa_Kodu (spesifik hesap) filtresi zaten eklenmiş olacak.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Reports/KasaBankaDefteri.razor`
- **efor:** 0,5 gün
- **bağımlılık:** TEMEL PR-A
- **gruplama:** bkz. `banka_hesap_hareketleri` altındaki not.

### kasa_tanimi.aspx
- **desen:** ZATEN PLANLI — bkz. not
- **eylem:** Bu ekranın tek gerçek eksiği (`Islem_Mail`/"Uyarı Mail Listesi") `02-tanim-master-
  plan.md`'nin **PR-A** bölümündeki `hesap_no_tanimlama.aspx` maddesinde `FinancialAccount.
  UyariMailListesi` alanı olarak ZATEN planlanmış (o plandaki efor 0,5 gün, üç alan — `HediyeCek`/
  `OzelKod`/`UyariMailListesi` — birlikte). Burada TEKRAR SAYILMADI.
- **efor:** 0 (bkz. 02-tanim-master-plan.md PR-A)
- **bağımlılık:** 02-tanim-master-plan.md PR-A ile koordinasyon (aynı dosya: `FinancialAccount.cs`,
  `FinancialAccountList.razor`).

### kasa_virman.aspx
- **desen:** D5 (TEMEL PR-A + küçük ek)
- **eylem:** Çoklu KASA hesabı seçimi + döviz/kur TEMEL PR-A ile çözülür (ek iş yok). Kalan:
  `Makbuz_No`, `Islemi_Yapan` (oturumdan — form alanı EKLENMEZ, audit zaten taşıyor, kural aynı
  02-tanim-master notuyla tutarlı), `Islem_Sube` — bunlardan yalnız `Makbuz_No`/`Islem_Sube` bilgi
  alanı olarak `KasaHub.razor`'a eklenir.
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Finance/KasaHub.razor`
- **efor:** 0,5 gün (yalnız Makbuz_No/Islem_Sube; hesap seçimi PR-A'da)
- **bağımlılık:** TEMEL PR-A

### kredi_takip_listesi.aspx
- **desen:** D7 (yeni dikey — ad benzerliği eşleşme DEĞİL, kural 3)
- **eylem:** Canlı "Kredi Takip" **müşteriye taksitli satış/kredi ile araç verme** takibi (Cari
  Bilgi + Plaka + Vade + Ödeme Durumu ekseni); bizim `AracKredi` **şirketin banka kredisiyle araç
  ALIMI** takibi — tamamen farklı iş, `AracKredi`'yi genişletmek KAVRAMSAL KARIŞTIRMA olur. Yeni
  entity: `MusteriTaksit` (CariId, VehicleId?, `VehicleSale.Id`? bağlantısı opsiyonel, Vade,
  TaksitTutari, OdemeDurumu) + CLAUDE.md §5 tam reçete (repository/servis/DI/liste+form/test).
  Not ayrıca: `AracKredi.VehicleId` şemada var ama ne formda ne listede kullanılıyor (wire-in
  eksik) — bu AYRI, küçük bir düzeltme (0,25 gün, aynı PR'a eklenebilir).
- **dokunulacak:** yeni `src/RentACar.Domain/Entities/MusteriTaksit.cs`, yeni migration (RLS bloğu
  ELLE), yeni `IMusteriTaksitRepository`+`MusteriTaksitRepository`, yeni `MusteriTaksitService`,
  yeni `Components/Pages/MusteriTaksitleri/MusteriTaksitList.razor` + endpoint + nav,
  `src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor` (VehicleId wire-in, küçük ek)
- **efor:** 4 gün (yeni dikey, CLAUDE.md §5 tam reçete) + 0,25 gün (AracKredi wire-in) = **4,25 gün**
- **bağımlılık:** yok

### nakit_islem.aspx
- **desen:** D3
- **eylem:** Şu an Tahsilat/Ödeme SADECE bir cari'nin ekstre sayfasından yapılabiliyor
  (`CustomerStatement.razor` içinde gömülü form) — canlıda bağımsız ekran (cari arama kutusu
  dahil). Yeni sayfa `/finans/nakit-islem` (rota) — cari arama (No/Ad/Soyad), ardından mevcut
  `CashService.CollectAsync`/`PayAsync` AYNEN çağrılır (mantık DEĞİŞMEZ, yalnız giriş yüzeyi).
  Tarih (manuel — şu an sunucu "şimdi"), `Onerilen_Tutar` (bakiyeden otomatik öneri, UI convenience)
  eklenir. Kasa_Kodu (spesifik hesap) → TEMEL PR-A.
- **dokunulacak:** yeni `Components/Pages/Finance/NakitIslem.razor`, mevcut
  `src/RentACar.Web/Finance/FinanceEndpoints.cs` (`/tahsilat`/`/odeme` uçları AYNEN kullanılır)
- **efor:** 1 gün — **not:** mevcut para-yazan mantık DEĞİŞMİYOR (`CollectAsync`/`PayAsync` zaten
  doğru çalışıyor), bu yalnız UI/giriş-yüzeyi işi; PARA etiketi orijinal taramadan miras — yeni bir
  formül/tutar KARARI gerekmiyor, yine de para formunu değiştirdiğinden bir regresyon-kontrolü
  (adversarial-lite) önerilir.
- **bağımlılık:** Kasa_Kodu alt-parçası için TEMEL PR-A

### nakit_islem_ara.aspx
- **desen:** D3
- **eylem:** `/kasa` (`KasaHub.razor`) erişilebilir + veri gösteriyor ama filtresiz — filtre paneli
  (Cari Ara, İşlem Şube, Tarih aralığı) + Cari Kod kolonu eklenir. `ListExportCatalog.
  NakitIslemler` export'una da Cari adı/kodu kolonu eklenir (aynı boşluk export'ta da var).
- **dokunulacak:** `src/RentACar.Web/Components/Pages/Finance/KasaHub.razor`,
  `src/RentACar.Web/Reports/ListExportCatalog.cs` (`NakitIslemler`, ~L98)
- **efor:** 0,5 gün
- **bağımlılık:** yok

### tahsilat_raporu.aspx
- **desen:** D4
- **eylem:** Şu an `ReportService.GetTahsilatFaturaAsync` yalnız dönem-toplamı (Fatura Adet/Toplam,
  Tahsilat Adet/Toplam, Fark) döndürüyor — canlı sözleşme-SATIRI düzeyinde mutabakat (Sözleşme No,
  Plaka, Müşteri, Matrah, Damga Vergisi, Sözleşme/Müşteri/TPC Toplam-Tahsilat-Bakiye üçlüleri,
  Faturalanan/Fark). Yeni satır sorgusu: `Rental` × `Invoice` (RentalId/KaynakKiraId) × `CashTransaction`
  (RentalId) JOIN'iyle sözleşme başına satır. Filtre: Sözleşme No, Bakiye durumu, Hizmet. "Mail At"
  aksiyonu BU PR'A DAHİL DEĞİL (e-posta gönderim altyapısı, ayrı küçük ek).
- **dokunulacak:** `src/RentACar.Infrastructure/Persistence/Repositories/ReportRepository.cs`
  (`GetTahsilatFaturaAsync`, ~L91 — satır modu eklenir), `src/RentACar.Application/Reporting/
  ReportService.cs`, `src/RentACar.Web/Components/Pages/Reports/TahsilatFatura.razor`
- **efor:** 1,5 gün ("Mail At" hariç)
- **bağımlılık:** yok

---

TOPLAM: 33 ekran planlandı
