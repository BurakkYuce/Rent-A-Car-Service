# Idempotency envanteri (F1.4)

Para yaratan her servis metodu için: çift gönderim mekanizması ve **aynı işlem ikinci kez gönderildiğinde bugün ne olduğu**.
Tablo koddan ölçüldü (satır numaraları bu PR'ın commit'ine göre). Her satır `tests/RentACar.IntegrationTests/IdempotencyEnvanteriTests.cs` içindeki aynı numaralı testle kilitli.
Tabloyu değiştiren PR testi de değiştirmek zorunda.

## Sözlük

| Mekanizma | Anlamı |
|---|---|
| **IslemAnahtari** | İsteğe bağlı çağıran anahtarı (`Guid?`). Blazor formu render başına `Guid.NewGuid()` basar. `/api/ui` uçları bunu `IdempotencyBasligi.Anahtar(ctx, deterministik)` ile doldurur (aşağıya bakın). Anahtar yoksa her çağrı bağımsız işlemdir. |
| **Deterministik** | Sunucunun ürettiği anahtar: `TahsilatAnahtar` (kira + bakiye + işlem sayısı), `CashService.RowKey(parti, i)`, HGS `(cari, plaka, dönem)` MD5'i, gelen e-fatura `RowKey(faturaId, i)`, ceza/gider ödeme `…:odeme:{sıra}`. **Başlıktan önceliklidir.** |
| **Yapısal** | Varlığın kendi durumu ya da doğal anahtarı: kira başına tek fatura, fatura başına tek iade, `TersAlinanId`, `Odendi` bayrağı, `Durum` geçişi. Anahtar gerektirmez. |

| Sonuç | HTTP (`/api/ui`, F1.1 `UiHata`) | Harici `RentACar.Api` |
|---|---|---|
| **409 mükerrer** (`MukerrerIslemException`) | 409 `mukerrer` → SPA kaydı yeniden yükler | 409 `duplicate_submission` |
| **400 doğrulama** (`ValidationException`) | 400 `dogrulama` | 400 `validation` |
| **Sessiz** | 200 (ilk işlemin sonucu ya da no-op) | 200/201 |

### Sessiz başarı kuralı: içerik birebir aynı olmalı

Adversarial MEDIUM-1 düzeltmesi: sessiz başarı veren her satırda kayıtlı işlemin **hedefi ve tutarı** gelen istekle karşılaştırılır.

- **Aynıysa:** sessiz başarı.
- **Farklıysa:** **409** `MukerrerIslemException.FarkliIcerikMesaji` = "Bu işlem anahtarı farklı içerikle zaten kullanılmış; işlem daha önce kaydedilmiş olabilir. Yeniden göndermeden önce kayıtları kontrol edin." Hiçbir şey yazılmaz.

Önceden aynı anahtar başka cari/tutarla gelince ikinci isteğin parası yazılmıyordu ama kullanıcıya "başarılı" dönüyordu.

Karşılaştırma yerleri:

| Yer | Karşılaştırılanlar |
|---|---|
| Defter kümesi (`Infrastructure/Persistence/DefterKumesi.cs`: `LedgerPoster` ve depozito) | hesap türü, hesap referansı (cari/kasa/banka), yön, tutar, döviz ve kur, DB hassasiyetine yuvarlanmış halde. Depozito iratında ayrıca kira atfı. |
| Manuel fatura | cari, net, KDV, döviz, manuel/iade bayrakları, kira bağı yok |
| Gider ödemesi | gider ve tutar. Tutar boşsa ("kalanın tamamı") yalnız kayıtlı ödeme gideri kapattıysa aynı sayılır. |
| Dönem faturası | açık verilmiş KDV oranı |

Tarih ve açıklama karşılaştırılmaz, çünkü yeniden gönderimde "şimdi" farklıdır.

Kural yarış yolunda da geçerli:
- `LedgerPoster` catch'i;
- depozito catch'i (kilit cari başına);
- gider ödeme catch'i (kilit gider başına);
- manuel fatura `PK_Invoices` dalı;
- dönem faturası kilit içi `Kesildi` dalı.

## Envanter

| # | Servis metodu | Mekanizma | İkinci gönderim (sıralı = eşzamanlı) | Kilit | Durum |
|---|---|---|---|---|---|
| E01 | `CashService.CollectAsync` `Application/Finance/CashService.cs:83` | IslemAnahtari → `IX_CashTransactions_TenantId_IslemAnahtari` (repo `CashRepository.cs:143`) · kira panelinde **deterministik** `TahsilatAnahtar` (`Web/Finance/TahsilatAnahtar.cs:19`) | **409** "Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer)." · anahtarsız → iki ayrı tahsilat | kısıt | doğrulandı (test, eşzamanlı dahil) |
| E02 | `CashService.PayAsync` `CashService.cs:87` | IslemAnahtari (E01 ile aynı index) | **409** | kısıt | doğrulandı (test) |
| E03 | `CashService.BatchCollectAsync` `CashService.cs:129` | Deterministik `RowKey(parti, i)` (aynı index) | **409** "Bu toplu işlem zaten kaydedilmiş.", hiçbir satır yazılmaz | kısıt | doğrulandı (test) |
| E04 | `CashService.BatchPayAsync` `CashService.cs:134` | Deterministik `RowKey(parti, i)` | **409** | kısıt | doğrulandı (test) |
| E05 | `CashService.TekCariTopluKapatAsync` `CashService.cs:265` | IslemAnahtari (E01 index) + cari danışma kilidi (`CashRepository.cs:207`) | **409** — ilk gönderim kalemi tam da kısmi de kapatsa. **F1.4 değişikliği** (önce: tam kapatmada 400 "zaten kapatılmış", kısmide 409) | anahtar önce (servis + kilit içi) | doğrulandı (test: tam + kısmi) |
| E06 | `CashService.TransferAsync` (kasa↔banka virman) `CashService.cs:401` | IslemAnahtari → `SourceId` + künye `KasaVirmanBilgi.Id`; `IX_AccountLedgerEntries_Virman_Idem` | Aynı içerik: **sessiz** no-op. Farklı tutar/yön/hesap: **409** farklı içerik (`LedgerPoster.cs:46`, F1.4 MEDIUM-1; önceden her unique ihlali sessizce yutuluyordu). Künye PK'si başka kiracıda: 400 | kısıt + içerik karşılaştırma | doğrulandı (test E06, M4) |
| E07 | `CashService.TransferBetweenCariAsync` `CashService.cs:475` | IslemAnahtari → `SourceId` + `CariVirmanBilgi.Id`; `IX_AccountLedgerEntries_CariVirman_Idem` | Aynı içerik: **sessiz**. Başka cari/tutar: **409** farklı içerik | kısıt + içerik karşılaştırma | doğrulandı (test E07, M4) |
| E08 | `CashService.ReverseAsync` `CashService.cs:530` | Yapısal `IX_CashTransactions_TenantId_TersAlinanId` + ön-kontrol | **409** "Bu işlem zaten ters kaydedilmiş." **F1.4 değişikliği** (önce: sıralı 400, yarışta 409). Ters kaydın tersi iş kuralı: 400 | ön-kontrol + kısıt, aynı tip ve metin | doğrulandı (test, eşzamanlı + Api) |
| E09 | `DepozitoService.AlAsync` `Finance/DepozitoService.cs:33` | IslemAnahtari → `SourceId`; `IX_AccountLedgerEntries_Depozito_Idem` | Aynı içerik: **sessiz**, aynı id (= anahtar). Başka cari/tutar/hesap: **409** farklı içerik | anahtar önce + içerik (kilit içi ve yarış catch'i) | doğrulandı (test E09) |
| E10 | `DepozitoService.IadeAsync` `DepozitoService.cs:47` | E09 ile aynı | Aynı içerik: **sessiz**, tamamı iade edilse de (önce: tam iadenin tekrarı bakiye çitinde 400). Başka cari/tutar/hesap: **409** farklı içerik. Farklı carilerin aynı anahtarla yarışı: biri yazılır, diğeri 409 | anahtar önce + içerik (`CashRepository.cs:317`, `:402`) | doğrulandı (test E10, M2 sıralı + eşzamanlı) |
| E11 | `DepozitoService.MahsupAsync` `DepozitoService.cs:58` | E09 ile aynı | E10 ile aynı kural | anahtar önce + içerik | doğrulandı (test E11) |
| E12 | `DepozitoService.IratAsync` `DepozitoService.cs:67` | IslemAnahtari → `SourceId` + `DepozitoIrat.Id` | E10 ile aynı kural; ayrıca başka kira atfı → **409**. Başka kiracının Id'sine çarpan anahtar: 400 | anahtar önce + içerik + kira | doğrulandı (test E12, M6) |
| E13 | `BakiyeDuzeltmeService.AdjustAsync` `Finance/BakiyeDuzeltmeService.cs:75` | IslemAnahtari → `SourceId`; `IX_AccountLedgerEntries_BakiyeDuzeltme_Idem` | Aynı içerik: **sessiz**, aynı id. Başka cari/yön/tutar: **409** farklı içerik | kısıt + içerik karşılaştırma (`LedgerPoster`) | doğrulandı (test E13, M4) |
| E14 | `InvoiceService.CreateManualAsync` `Finance/InvoiceService.cs:445` | IslemAnahtari = **fatura PK'si** (`PK_Invoices`) | Aynı içerik (cari, net, KDV): **sessiz**, mevcut fatura id'si. Başka cari ya da tutar: **409** farklı içerik. Bu kural hem sıralı ön-kontrolde hem yarış yolunda geçerli. Önce ön-kontrol her durumda ilk faturanın id'sini sessizce dönüyordu (P1). Yarışı kaybeden istek ise PK'ye çarpıp 400 "Kira zaten faturalanmış." alıyordu. Id başka kiracıdaysa 400 "İşlem anahtarı başka bir kayıtla çakıştı" (`InvoiceRepository.cs:251`) | ön-kontrol + PK + içerik | doğrulandı (test E14, M1 sıralı + eşzamanlı, A2b) |
| E15 | `InvoiceService.CreateFromRentalAsync` `InvoiceService.cs:118` | Yapısal `IX_Invoices_TenantId_RentalId` + fark sırası `(KaynakKiraId, KaynakKiraFarkSira)` + kira danışma kilidi | **400** "Kira zaten tam faturalanmış (yeni ek bedel yok)." (yarışta "Kira bu sırada faturalandı…", tip aynı: `ValidationException`). Dönüş sonrası yeni bedel varsa ikinci çağrı meşru fark faturasıdır | kilit + kısıt | doğrulandı (test, eşzamanlı dahil) |
| E16 | `InvoiceService.BatchCreateFromRentalsAsync` `InvoiceService.cs:84` | E15'e devreder (atomik değil, bilinçli) | **Sessiz**: yeni belge yok, her kira "atlananlar" listesine yazılır | E15 | doğrulandı (test) |
| E17 | `InvoiceService.CreateIadeAsync` `InvoiceService.cs:516` | Yapısal `IX_Invoices_TenantId_KaynakFaturaId` + ön-kontrol | **400** "Bu fatura zaten iade edilmiş." (yarışta da aynı metin) | ön-kontrol + kısıt | doğrulandı (test) |
| E18 | `InvoiceService.CreateDonemFaturasiAsync` `InvoiceService.cs:273` | Yapısal: dönem durumu + `(KaynakKiraId, KaynakKiraFarkSira)` + kira danışma kilidi | **Sessiz**, mevcut fatura id'si. **F1.4 değişikliği**: yarışı kaybeden istek 400 ("Kira faturaları bu sırada değişti" / "kesilecek tahakkuk kalmadı") alıyordu; artık kilit içinde Kesildi görülür ve aynı id döner (`InvoiceRepository.cs:325`). Açık verilen KDV oranı mevcut faturanınkinden farklıysa **409** farklı içerik (`InvoiceService.cs:366`) | kilit içi durum + oran karşılaştırma | doğrulandı (test E18 eşzamanlı, M7) |
| E19 | `DonemTahsilatService.KesVeTahsilEtDetayAsync` `FaturaDonemleri/FaturaDonemFiles.cs:70` | E18 + deterministik `RowKey(kiraId, dönemSıra)` | **Sessiz**: aynı fatura, `TahsilatYazildi = false`. Başka hesapla tekrar edilirse de tahsilat yazılmaz ama bu gizlenmez: çağıran "tahsilat daha önce alınmış" bildirir. **Low-B (R04):** `RowKey`'li kayıt bu kiranın tahsilatı ama tutarı/dövizi/kuru dönem faturasından FARKLIYSA (Blazor ham anahtarla önden alınmış) **409** `mukerrer` + `mevcut{ayniIcerik=false}`; fatura kesilmiş, dönem tahsilatı yazılmamış | E18 + E01 + içerik karşılaştırma | doğrulandı (test, `LowTemizligiBTests.R04_*`) |
| E20 | `OtomatikTahsilatService.CalistirAsync` `FaturaDonemleri/OtomatikTahsilatService.cs:90` | Aday çiti (yalnız Planlandi) + E19 | **Sessiz**: `Kesilen = 0, Tahsilat = 0`, her dönem atlananlarda | aday listesi | doğrulandı (test) |
| E21 | `ExpenseService.CreateAsync` (tekil gider) `Expenses/ExpenseService.cs:41` | **Önce yoktu.** F1.4: `ExpenseInput.IslemAnahtari` → mevcut `IX_Expenses_TenantId_IslemAnahtari` (kolon ve index zaten vardı, migration yok) + repo'ya catch (`ExpenseRepository.cs:174`) | Anahtarlı: **409** "Bu gider zaten kaydedilmiş (çift gönderim)." · anahtarsız: iki ayrı gider (bugünkü Blazor formu anahtar göndermiyor) | kısıt | doğrulandı (test) |
| E22 | `ExpenseService.BatchCreateAsync` `ExpenseService.cs:57` | Deterministik `RowKey(parti, i)` (E21 index) | **409** "Bu toplu gider zaten kaydedilmiş." | kısıt | doğrulandı (test) |
| E23 | `ExpenseService.OdemeEkleAsync` (gider ödeme takibi, deftere yazmaz) `ExpenseService.cs:109` | Deterministik `gider:{id}:odeme:{sıra}` + IslemAnahtari `IX_GiderOdemeleri_TenantId_IslemAnahtari` | Aynı gider + aynı tutar: **sessiz** `null`, kalanın tamamı ödense de (önce: tam ödemenin tekrarı 400 "kalanı yok"). Başka gider/tutar: **409** farklı içerik (P4; önce sessiz `null`). Farklı giderlerin aynı anahtarla yarışı: biri yazılır, diğeri 409 | anahtar önce + içerik (`ExpenseRepository.cs:80`, `:150`) | doğrulandı (test E23, M3 sıralı + eşzamanlı) |
| E24 | `GelenEFaturaService.GiderlestirAsync` `GelenEFaturalar/GelenEFaturaService.cs:198` | Deterministik `RowKey(faturaId, i)` (E21 index) + ön-kontrol | **409** "'…' ETTN'li fatura zaten giderleştirilmiş." **F1.4 değişikliği** (önce: sıralı 400, yarışta 409) | ön-kontrol + kısıt | doğrulandı (test) |
| E25 | `PenaltyService.YansitAsync` `Penalties/PenaltyService.cs:113` | Yapısal: `Durum = Yeni` + `FOR UPDATE` | **400** "Yalnız 'Yeni' durumundaki ceza yansıtılabilir." **F1.4 değişikliği**: yarışı kaybeden istek önce sessizce `false` dönüyordu (Blazor "başarılı" gösteriyordu); artık aynı 400 (`PenaltyRepository.cs:169`) | kilit içi durum | doğrulandı (test, eşzamanlı dahil) |
| E26 | `PenaltyService.KismiOdeAsync` `PenaltyService.cs:148` | IslemAnahtari `IX_PenaltyOdemeleri_TenantId_IslemAnahtari` + deterministik `ceza:…:odeme:{sıra}` + danışma kilidi | Anahtarlı: **409** "Bu ceza ödemesi zaten kaydedilmiş (çift gönderim)." (tam ödemede de). **F1.4 değişikliği** (önce: tam ödemenin tekrarı 400 "ödenecek bakiye yok"). Anahtarsız tam ödemenin tekrarı 400; anahtarsız kısmi tekrar ikinci ödemedir | anahtar önce (`PenaltyRepository.cs:207`) | doğrulandı (test: tam + kısmi) |
| E27 | `RegulationService.MtvOdeAsync` `Regulation/RegulationService.cs:148` | IslemAnahtari `IX_MtvOdemeleri_TenantId_IslemAnahtari` (kısmi ödemede zorunlu) + yapısal `Odendi` + `FOR UPDATE` | Anahtarlı: **409** "Bu MTV ödemesi zaten kaydedilmiş (çift gönderim)." (tam ödemede de). **F1.4 değişikliği** (önce: kaydı kapatan ödemenin tekrarı 400). Anahtarsız tam ödemenin tekrarı **400** "MTV zaten ödendi." | anahtar önce (`RegulationRepository.cs:63`) | doğrulandı (test: anahtarlı tam, kısmi, anahtarsız) |
| E28 | `RegulationService.MuayeneOdeAsync` `RegulationService.cs:215` | E27 ile aynı (`IX_MuayeneOdemeleri_TenantId_IslemAnahtari`) | E27 ile aynı ("Bu muayene ödemesi…", "Muayene zaten ödendi.") | anahtar önce (`RegulationRepository.cs:157`) | doğrulandı (test) |
| E29 | `RegulationService.SigortaOdeAsync` `RegulationService.cs:326` | Yapısal: `Odendi` + `SourceId = poliçeId` `IX_AccountLedgerEntries_SigortaOdeme_Idem` | **400** "Sigorta zaten ödendi." (yarışta da aynı metin, catch düz `ValidationException`) | ön-kontrol + kısıt | doğrulandı (test) |
| E30 | `ServiceRecordService.YansitAsync` `ServiceRecords/ServiceRecordService.cs:165` | Yapısal: `Yansitildi` + `SourceId = servisId` `IX_AccountLedgerEntries_ServisYansitma_Idem` | **400** "Servis maliyeti zaten yansıtıldı." (yarışta aynı) | ön-kontrol + kısıt | doğrulandı (test) |
| E31 | `HgsReflectionService.ReflectAsync` `Hgs/HgsReflectionService.cs:24` | Deterministik `SourceId = MD5(cari, plaka, dönem)`; `IX_AccountLedgerEntries_TenantId_SourceType_SourceId_Direction` | Aynı tutar: **sessiz**, aynı sonuç, ikinci yazım yok. Aynı dönem, farklı geçiş tutarı: **409** farklı içerik (önce sessizce yutuluyordu, fark hiç borçlandırılmıyordu) | kısıt + içerik karşılaştırma (`LedgerPoster`) | doğrulandı (test E31, M5) |
| E32 | `AracKrediService.TaksitOdeAsync` `AracKredileri/AracKrediService.cs:74` | IslemAnahtari → `Expense.IslemAnahtari` (E21 index) + `FOR UPDATE` | Anahtarlı: **409** "Bu taksit ödemesi zaten kaydedilmiş (çift gönderim)." — son taksitte de. **F1.4 değişikliği** (önce: ara taksitte 400 [catch `IdempotencyKisiti`'ye bağlı değildi], son taksitte sessiz `false`). Anahtarsız: sonraki taksidi öder; hepsi ödendiyse `false` | anahtar önce (`AracKrediRepository.cs:87`) | doğrulandı (test: ara + son taksit) |
| E33 | `DisHizmetService.CreateAsync` `DisHizmetler/DisHizmetFiles.cs:66` | IslemAnahtari `IX_DisHizmetAlimlari_TenantId_IslemAnahtari` | **409** "Bu dış hizmet kaydı zaten girilmiş (çift gönderim)." | kısıt | doğrulandı (test) |
| E34 | `DisHizmetService.IptalEtAsync` `DisHizmetFiles.cs:121` | Yapısal `Durum` (kilit içi yeniden kontrol) | **400** "Kayıt zaten iptal edilmiş." (yarışta "…(eşzamanlı istek)", tip aynı) | ön-kontrol + kilit | doğrulandı (test) |
| E35 | `VehicleSaleService.CreateAsync` `VehicleSales/VehicleSaleService.cs:44` | Yapısal: araç `Satildi` + `IX_VehicleSales_TenantId_VehicleId` (Durum = Tamamlandı) | **400** "Araç zaten satılmış." (yarışta aynı) | kilit içi + kısıt | doğrulandı (test) |
| E36 | `DonemKapanisFisiService.KapatAsync` `Periods/DonemKapanisFisiService.cs:42` | Yapısal: kapanış tarihi + kiracı danışma kilidi (`DonemKapanisRepository.cs:27`) | **400** "Dönem zaten … tarihine kapalı. …" (yarışta aynı metin) | ön-kontrol + kilit | doğrulandı (test) |
| E37 | `RentalAddOnService.AddAsync` `RentalAddOns/RentalAddOnService.cs:30` (Low-B) | IslemAnahtari (yalnız `/api/ui` ucu; başlık ZORUNLU) + kısmi unique `IX_RentalAddOns_TenantId_IslemAnahtari` + kira satır kilidi altında yeniden arama | **409** `mukerrer` her durumda (kalem kira toplamını değiştirir; sessiz başarı yok). Bu kiranın kalemiyse `mevcut{id, belgeNo=ad, tutar=brüt, doviz=TRY, ayniIcerik}` (tanım + miktar birebir → `true`); başka kiranın kalemiyse `mevcut` yok. Blazor ve SYS-* sistem kalemleri anahtarsız (davranış değişmedi) | kapsam → anahtar (ön-kontrol) → iş kuralları; kilit altı yeniden kontrol; kısıt | doğrulandı (test `LowTemizligiBUiTests.Ek_hizmet_*`, eşzamanlı 6 istek) |

**Kapsam dışı (para yazmaz, listelendi):** `MusteriTaksitService.OdemeIsaretleAsync` (`MusteriTaksitleri/MusteriTaksitFiles.cs:134`, takip bayrağı; tekrar aynı durumu yazar), `RentalService.ProvizyonAlAsync` (`Bookings/RentalService.cs:379`, manuel provizyon izi, deftere yazmaz), `IPosService` (henüz para yoluna bağlı değil). `RentACar.Api` (harici JWT API) tahsilat/ödeme/virman uçları anahtar almıyor; Non-Goals gereği değiştirilmedi.

## `/api/ui` için anahtar seçimi (F1.2+ uçları)

```csharp
// para yaratan uç, deterministik anahtarı yok → başlık ZORUNLU (yoksa 400)
input.IslemAnahtari = IdempotencyBasligi.ZorunluAnahtar(ctx);
// panel/liste DTO'su TahsilatAnahtar taşıyorsa (başlık onu ezemez)
input.IslemAnahtari = IdempotencyBasligi.ZorunluAnahtar(ctx, deterministik: dto.TahsilatAnahtar);
// anahtarsız çalışabilen uç (opsiyonel)
input.IslemAnahtari = IdempotencyBasligi.Anahtar(ctx);
```

`ZorunluAnahtar` kullanılmalı: anahtarsız çağrı her seferinde yeni işlemdir (E01, E21). Başlıksız bir SPA isteği çift yazıma açık kalır.

- `Web/Common/IdempotencyBasligi.cs`: `Idempotency-Key` başlığını okur, kiracı ve kullanıcıyı **oturum claim'lerinden** alır.
- `Application/Common/IslemAnahtariTuretici.cs`: `UUIDv5(2adf1c10-4f5c-4c27-bad3-3294d841ee0c, "{tenantId}|{userId}|{başlık}")`. Ad alanı sabittir, asla değişmez.
- Öncelik (`Sec`): deterministik anahtar ▸ başlıktan türetilen ▸ `null`. Başlık deterministik anahtarı ezemez.
- Başlık 16–128 karakter, yalnız görünür ASCII. Biçimsiz ya da çok değerli başlık 400 (`errors["Idempotency-Key"]`), deterministik anahtar olsa bile.
- İstemci değeri hiçbir zaman doğrudan anahtar ya da PK olmaz.
- İstemci anahtarı her 2xx'ten sonra yeniler (README İmzalar §3).
- Yapısal mekanizmalı satırlar (E15–E18, E25, E29, E30, E34–E36) anahtar kullanmaz; başlık yok sayılabilir.
- Deterministik satırlar (E03/E04/E22 parti, E19/E20, E24, E31) sunucu anahtarını kullanır.

### `/api/ui/v1/finans/*` uç eşlemesi (F4.4 — sabit panel; F8 yeniden kullanır)

Kilit: `tests/RentACar.IntegrationTests/UiFinansApiTests.cs` + `UiFinansAdversarialTests.cs`. Uç kodu: `Web/Api/Finans/FinansApi.cs`.

| Uç | Satır | Anahtar | İkinci gönderim |
|---|---|---|---|
| `POST finans/tahsilat` | E01 | `tahsilatAnahtar` (DTO, yalnız `kiraId` ile; sunucuda yeniden hesaplanır) ▸ başlık; ikisi de yoksa 400 | 409 `mukerrer` (aynı ya da farklı içerik); iki sekme/iki kullanıcı aynı `tahsilatAnahtar` → ikincisi 409. **İki ayrı 409 (F4.4 HIGH-1):** bu anahtarla bu kiraya yazılmış tahsilat VARSA "zaten kaydedildi (No …)" + ProblemDetails `mevcut: { id, belgeNo, tutar, doviz }` (kaybolan yanıttan sonraki tekrar); YOKSA (bayat/yabancı anahtar) "değişti … tutarı yeniden girin", `mevcut` yok. **`mevcut.ayniIcerik` (3. tur M-A):** kayıt gelen istekle birebir aynıysa (tutar `decimal` eşitliği, döviz, hesap türü, hesap) `true` → "zaten kaydedildi"; farklıysa (iki sekme/iki kullanıcı aynı anahtarla, ya da tutar değiştirilmiş tekrar) `false` → "başka bir tahsilat yazıldı … girdiğiniz X YAZILMADI" — SPA formu SİLMEZ |
| `POST finans/odeme` | E02 | başlık zorunlu | 409 |
| `POST finans/fatura` | E15 | yok (yapısal; başlık yok sayılır) | 400 "Kira zaten tam faturalanmış…" |
| `POST finans/donem-fatura` | E18/E19 | yok; tahsilat `RowKey(kira, sıra)` | 200 aynı `faturaId`, `tahsilatYazildi=false`, `bilgi` dolu (gizlenmez) |
| `POST finans/dis-hizmet` | E33 | başlık zorunlu | 409 |
| `POST finans/dis-hizmet/{id}/iptal` | E34 | yok (yapısal) | 400 "Kayıt zaten iptal edilmiş." |
| `POST finans/depozito/al` | E09 | başlık zorunlu | aynı içerik 200 aynı `id`; farklı içerik 409 |
| `POST finans/depozito/irat` | E12 | başlık zorunlu | aynı içerik 200 aynı `id`; farklı tutar/kira 409 |

**Önce mevcut kayıt (F4.4 adversarial HIGH-1):** yeniden hesaplamadan ÖNCE gelen `tahsilatAnahtar` ile yazılmış
kasa işlemi aranır (`CashService.IslemAnahtariylaBulAsync`, RLS'li). Bu kiranın tahsilatıysa 409 `mukerrer` "Bu
tahsilat zaten kaydedildi (No …, tutar …)" + `mevcut` uzantısı (OpenAPI `MukerrerProblemi`/`MevcutIslem`). Önce:
ilk istek yazılıp yanıt kaybolunca AYNI anahtarla doğru tekrar, yeniden hesaplamada "kayıt değişti … tekrar deneyin"
alıyor, SPA yeni anahtarla İKİNCİ tahsilatı yazdırıyordu. Başka kiranın tahsilatına ait anahtar bilgi SIZDIRMAZ
(aşağıdaki "ait değil" 409'u). Sonuç kodu değişmedi (409 — F1.4 sözleşmesi); ayrım mesaj + uzantıdadır.

**`tahsilatAnahtar` doğrulaması (F4.4a adversarial MEDIUM-3):** uç, DTO'dan gelen değeri okunan kira + güncel
bakiye + güncel işlem sayısıyla `TahsilatAnahtar.Uret` üzerinden YENİDEN hesaplar (bakiye DB ölçeğiyle "300.0000"
ya da sade "300" kabul). Eşit değilse 409 `mukerrer`: ya ekran açıldıktan sonra kirada işlem oldu (bayat; SPA kaydı
yeniden yükler ve yeni anahtarı alır) ya da anahtar bu kiraya ait değil (başka kiranın ya da başka bir işlemin
tahmin edilebilir anahtarı — ör. dönem tahsilatının `RowKey`'i). Ham değer anahtar olarak korunur: Blazor pano/kira
listesi ile SPA aynı anahtara düşer. Ek çit: `DonemTahsilatService` "zaten kaydedilmiş" dalında `RowKey`'li kaydın
bu kiranın tahsilatı olduğunu doğrular; değilse sessiz no-op yerine 400 (fatura kesilmiş, tahsilat yazılmamış).

Uç katmanının servise EKLEDİĞİ giriş kuralları (hepsi yazmadan önce 400/403):
- Kiraya bağlı işlemde kira `RentalService.GetAsync` ile okunur → şube kapsamı dışında 403 `yetki_yok` (fatura,
  dönem faturası ve kiraya bağlı tahsilat/irat servislerinde kapsam guard'ı yok; kapı uçtadır).
- Kiraya bağlı tahsilat/ödemede `cariId` kiranın müşterisi olmalı (`errors.cariId`) — Blazor'un tüm girişleri zaten öyle gönderir.
- İptal kiraya tahsilat bağlanamaz (`errors.kiraId`); iade ödemesi bağlanabilir.
- Sınırlar (adversarial MEDIUM-1/L2): tutar ve baz (tutar × açık kur) < 10^15 (`numeric(19,4)`), kur < 10^13
  (`numeric(19,6)`), 4 ondalığa yuvarlanınca 0 kalan tutar reddedilir; metin alanları kolon uzunluğunda
  (`aciklama` 512, `alinanHizmet`/`hizmetAlinanFirma` 256, `komisyonFaturaNo` 64). Kalan taşmalar (ör. otomatik kurla
  kira Tahsilat + delta) için `/api/ui` hata eşlemesi PostgreSQL 22001/22003'ü 400 `dogrulama`'ya çevirir (genel
  mesaj, iç ayrıntı yok); başka SQLSTATE 500 kalır.
- Bu kurallar anahtar kontrolünden önce çalışır: aradaki durum değişikliğinde (kira iptal edildi) birebir tekrar
  409 yerine 400 alır — LOW-1 ile aynı sınıf, para etkisi yok.

Servis düzeyinde (Blazor da kapsanır):
- **HIGH-1:** `KurCozucu` temel para (TRY) işleminde açık kur ≠ 1'i reddeder (`ValidationException(…, "kur")`).
  Önce 100 TRY @5 kabul ediliyor, baz 500'e şişiyordu (kira Tahsilat, cari, kasa).
- **MEDIUM-2:** `CashService` tahsilat/ödeme ve `DepozitoService` al/iade/irat carinin kiracıda var olduğunu
  doğrular (`errors.cariId`); rastgele ya da başka kiracının cari kimliğine yetim defter kümesi yazılmaz.
- **L1:** depozito al/iade hesap seçiminde hesap-döviz çiti (tahsilattaki FAZ-50 M4 gibi).

**SPA sözleşmesi (adversarial L3/L4 — kod değil, istemci kuralı):**
- **Her işlem kendi `Idempotency-Key`'ini üretir.** Başlıktan türetilen anahtar işlem türünü içermez; aynı başlık
  farklı türde işlemlerde (ör. depozito al + tahsilat + dış hizmet) ayrı tablolara/kümelere düştüğü için HER biri
  ayrı kayıt yazar — birbirini mükerrer saymaz. Aynı türde (tahsilat↔ödeme ortak index) ise ikincisi 409 alır.
- **Deterministik anahtar varken başlık TÜKETİLMEZ.** `tahsilatAnahtar` gönderilen istekte başlık yalnız biçim
  denetiminden geçer; aynı başlıkla `tahsilatAnahtar`'SIZ yeniden deneme YENİ tahsilat yazar. İstemci bir
  tahsilatı hangi anahtar kümesiyle gönderdiyse yeniden denemeyi de BİREBİR aynı gövdeyle yapar.
- **Deterministik tahsilat 409'unun sınıfı istemcide (F4.4 M-C + 5. tur, `core/form/tahsilat-denemesi.ts`).** Sunucu
  `mevcut` (+ `ayniIcerik`: tutar, döviz, hesap türü, hesap, kur, açıklama, kanal, açık tarih) döner. İstemci sonucu
  bilinmeyen (ağ/5xx) denemeleri İÇERİKLERİYLE, formdan bağımsız ve ANAHTARA bağlı uygulama geneli kayıtta tutar
  (`TahsilatDenemeKaydi`, root; Nakit ↔ Kart, kira listesi ↔ Panel ortak; çıkışta silinir). Anahtar tek kayıt taşıdığı
  için: `mevcut` belirsiz bir denemeyle (tutar + döviz) eşleşirse "önceki denemeniz kaydedilmiş", eşleşmezse "başka
  işlem yazıldı, önceki denemeniz de kaydedilmedi" — iki durumda da tutar TEMİZLENİR (ikinci basış bilinçli yeniden
  giriş ister; çift yazım yok). Belirsiz deneme yoksa (iki sekme) form korunur, yalnız dokunulmamış ön-dolu tutar yeni
  bakiyeyle yenilenir. 409 kaydı silmez (`mevcut`suz 409 eşzamanlı yarış olabilir); yalnız o anahtarın 2xx'i siler.

**SPA uygulaması — kira formu sabit paneli (F4.4, `features/kira-formu/finans-paneli/`):**
- `tahsilatAnahtar`'ın kaynağı kira detayıdır: `GET kiralar/{id}` → `tahsilat` (`TahsilatBilgisi`; liste/pano ile
  aynı üretim; FinanceWrite + iptal olmayan kira; bakiye ≤ 0'da da dolar). Tahsilat isteği başlıksız gider.
- Form bir **satır kopyası** tutar (`TahsilatKopyasi`): boştaki form her yeni detayla kopyayı tazeler; kullanıcı
  formu doldurduysa ya da gönderim SONUÇLANMADIYSA (ağ/5xx/oturum/doğrulama) kopya donar ve yeniden deneme aynı
  anahtarla gider (anahtar sessizce yenisiyle ya da anahtarsızla değiştirilmez). 2xx ya da 409 `mukerrer`
  sonrası düğme yeni detay gelene dek kapalıdır; gelen detayın anahtarı alınır (ikinci meşru tahsilat).
- `mevcut.ayniIcerik=false` (başka tahsilat yazıldı, gelen tutar YAZILMADI): UYARI "Başka bir tahsilat yazıldı",
  form SİLİNMEZ (tutar korunur), kayıt yeniden yüklenir (yeni anahtar), kullanıcı bilinçli yeniden gönderir — üç
  ekranda da (sabit panel, kira listesi Tahsil Et, pano Tahsil Et).
- 409'da otomatik yeniden gönderim YOK, kayıt yeniden yüklenir, TUTAR TEMİZLENİR ve yeniden ön-doldurulmaz
  (kullanıcı güncel bakiyeye bakıp bilinçli girer). `mevcut` VARSA (kaybolan yanıt): form temizlenir, "Tahsilat
  zaten kaydedildi" bilgisi (interceptor, üç ekranda da). YOKSA (bayat anahtar): nötr "Kira kaydı değişmiş" uyarısı
  (`mukerrerBasligi`) + sunucu `detail`'ı ("tutarı yeniden girin").
- Ödeme, depozito al/irat, dış hizmet: işlem başına `Idempotency-Key` (`formGonderimi`/`GonderimKilidi`).
- Kira formu ek hizmet ekleme (`POST kiralar/{id}/ek-hizmetler`, E37): aynı desen; 409'da kayıt yenilenir,
  `mevcut.ayniIcerik` ise form temizlenir, otomatik yeniden gönderim yok.
  Fatura, dönem faturası, dış hizmet iptali yapısal: başlık gönderilmez.

### `/api/ui/v1/finans/*` uç eşlemesi (F8.1a — finans ekranları, 1. yarı)

Kilit: `tests/RentACar.IntegrationTests/UiFinanceHubApiTests*.cs`. Uç kodu: `Web/Api/FinansHub/`. Envanter satırları
DEĞİŞMEDİ; uçlar mevcut servisleri çağırır. Uç katmanı ekleri: tutar ≤ 4 ondalık (kuruşa yazan kalemlerde ≤ 2), kur ≤ 6
ondalık, `numeric(19,4)` sınırı, metin kolon uzunlukları, gövdedeki var olmayan/başka kiracının cari kimliği 400
(`errors[alan]`), yoldaki cari 404.

| Uç | Satır | Anahtar | İkinci gönderim |
|---|---|---|---|
| `POST finans/kasa/virman` | E06 | başlık zorunlu; yanıt `id` = türetilen anahtar | aynı içerik 200 aynı `id`; farklı 409 |
| `POST finans/kasa/islemler/{id}/ters` | E08 | yok (yapısal; FinanceReverse) | 409 `mukerrer`. Kira bağlıysa kira şube kapsamı durumdan önce (403) |
| `POST finans/bakiye-duzeltme` | E13 | başlık zorunlu | aynı içerik 200 aynı `id`; farklı 409 |
| `POST finans/cari-virman` | E07 | başlık zorunlu; yanıt `id` = anahtar | aynı içerik 200 aynı `id`; farklı 409 |
| `POST finans/cariler/{cariId}/toplu-kapat` | E05 | başlık zorunlu | 409 `mukerrer`. ÖNCE bu anahtarla yazılmış kayıt aranır: bu carinin kapatmasıysa `mevcut{id, belgeNo, tutar, doviz, ayniIcerik}` (hesap, kanal, açık tutarların toplamı, açıklama); başka işlemse `mevcut`suz 409 |
| `POST finans/toplu-tahsilat` | E03 | başlık zorunlu → parti anahtarı; satır `RowKey(parti, i)` | 409 `mukerrer` + `mevcut` (ilk satırın belgesi, parti toplamı; `ayniIcerik` tüm satırlar birebir aynıysa) |
| `POST finans/toplu-gider` | E22 | başlık zorunlu → parti anahtarı | 409 `mukerrer` (servis metni) |
| `POST finans/depozito/iade` | E10 | başlık zorunlu | aynı içerik 200 aynı `id`; farklı 409 |
| `POST finans/depozito/mahsup` | E11 | başlık zorunlu | aynı içerik 200 aynı `id`; farklı 409 |
| `POST finans/otomatik-tahsilat/calistir` | E20 | yok (aday çiti + E19) | 200 `kesilen=0`, her dönem `atlananlar`da |
| `POST finans/donem-kapanis/kilitle` | E36 | yok (yapısal) | 400 "Dönem zaten … kapalı". Tarih bugünden (İstanbul) ileri olamaz |

Kasa/banka negatif bakiye guard'ı YOK (kasıtlı). Sabit kur (`PUT finans/kurlar/sabit/{id}`) para yazmaz ama çözülen
kuru belirler: tam değiştirme zorunlu `surum` (xmin) ile, uyuşmazlık 409 `cakisma`.

### `/api/ui/v1` araç finans uç eşlemesi (F6.1b)

Kilit: `tests/RentACar.IntegrationTests/UiAracFinansTests*.cs`. Uç kodu: `Web/Api/AracFinans/`.

| Uç | Satır | Anahtar | İkinci gönderim |
|---|---|---|---|
| `POST arac-kredileri/{id}/taksit-ode` | E32 | başlık zorunlu; gövdede `sira` (ödenecek taksit) | Önce aynı anahtarla yazılmış gider aranır → 409 `mukerrer` + `mevcut{…, ayniIcerik}` (sıra, hesap türü, hesap, açık tarih). SONRA bayatlık: `sira ≠ OdenenTaksit+1` kilit altında → 409 `cakisma` (iki sekme iki taksit ödemez). Anahtar başka işlemin ise `mevcut`suz 409 |
| `POST arac-kredileri` | yeni | başlık zorunlu → kredi **Id** | aynı içerik 409 `mevcut.ayniIcerik=true`; farklı içerik `false`; yarışta PK ihlali → 409 |
| `POST musteri-taksitleri`, `…/plan` | yeni (deftere yazmaz) | başlık zorunlu → Id; planda Id'ler anahtardan türetilir (`MusteriTaksitService.PlanSatirId`) | 409 `mukerrer` + `mevcut` (planda tutar = plan toplamı); yarım plan yazılmaz |
| `POST musteri-taksitleri/{id}/odendi` | kapsam dışı satırın kilitli hali | yok (yapısal) | ödenmiş taksit → 409 `mukerrer` + `mevcut` (tarih sessizce ezilmez) |
| `POST arac-siparisleri` | yeni (deftere yazmaz) | başlık zorunlu → Id | 409 `mukerrer` + `mevcut` (tutar = adet × birim fiyat) |
| `POST baflar`, `POST hasar-dosyalari` | yeni (para yok) | başlık isteğe bağlı → Id | başlıkla 409; başlıksız bağımsız kayıt |
| durum geçişleri (sipariş, BAF, hasar, filo delta) | yapısal | yok | satır kilidi altında durum çiti (`SatirSurumu`); PUT'lar zorunlu `surum` → 409 `cakisma` |

TRY işlemde açık kur ≠ 1 uçta 400 (`errors.kur`); dövizde boş kur `KurCozucu` ile çözülür. Taksit ödemesinde hesap-döviz
çiti artık uygulanıyor (`HesapCozucu.CozAsync(…, kredi.Currency)`).

### `/api/ui/v1` servis / sigorta / fiyat uç eşlemesi (F9.1)

Kilit: `tests/RentACar.IntegrationTests/UiServiceInsuranceTests*.cs`. Uç kodu: `Web/Api/ServiceInsurance/`.

| Uç | Satır | Anahtar | İkinci gönderim |
|---|---|---|---|
| `POST regulasyon/mtv/{id}/odeme`, `POST regulasyon/muayeneler/{id}/odeme` | E27 / E28 | başlık ZORUNLU (Blazor'da yalnız kısmi ödemede) | Önce aynı anahtarla yazılmış ödeme aranır (MTV ve muayene tabloları) → 409 `mukerrer` + `mevcut{…, ayniIcerik}` (tutar — boşsa "kaydı kapattı mı" —, ceza, hesap türü, hesap, açık tarih); başka kaydın ödemesiyse `mevcut`suz 409. SONRA bayatlık: isteğe bağlı `beklenenKalan` kilit altında okunan kalanla farklıysa 409 `cakisma` (iki sekme "kalanın tamamı"nı iki kez ödemez) |
| `POST regulasyon/sigortalar/{id}/odeme` | E29 | yok (yapısal: poliçe başına tek ödeme) | Ödenmiş poliçe → 409 `mukerrer` + `mevcut{id=poliçe, tutar=prim+zeyil, ayniIcerik}` (zeyil ek prim, hesap türü, hesap; defter izinden). Yarışı kaybeden kilit altında "zaten ödendi" görür → aynı 409. Poliçe satırı artık `FOR UPDATE` ile kilitlenir |
| `POST servisler/{id}/yansit` | E30 | yok (yapısal: SourceId = servis) | Yansıtılmış kayıt → 409 `mukerrer` + `mevcut{tutar=yansıtılan, ayniIcerik=aynı cari}`. Kilit altında durum/işçilik/kusur yeniden denetlenir; tutar değiştiyse 409 `cakisma` |
| `POST servisler/{id}/kalemler` | yeni (rücu tabanını büyütür) | başlık ZORUNLU → kalem **Id** | Kilit altında aynı Id aranır (durum çitinden önce) → 409 `mukerrer` (+ `mevcut` bu kaydın kalemiyse); `ToplamIscilik` iki kez artmaz |
| `POST servisler`, `POST regulasyon/{sigortalar,mtv,muayeneler}`, `POST maliyet-teklifleri` | yeni (deftere yazmaz) | başlık isteğe bağlı → kayıt Id | başlıkla 409 `mukerrer` + `mevcut`; yarışta PK ihlali → 409; başlıksız bağımsız kayıt |
| tanım PUT'ları (tarifeler, tarife matrisi, gruplar, ürünler, kurallar, broker, ek hizmet, servis tanımı), `PUT servisler/{id}/bilgi`, `PUT maliyet-teklifleri/{id}` | yapısal | yok | zorunlu `surum`; kilit altında karşılaştırma (`IRowVersionStore`) → 409 `cakisma` |

Ödemelerde tutar en çok 2 ondalık, 0 ve negatif red; MTV/muayene yalnız TRY (hesap-döviz çiti TRY); sigortada TRY'de
kur ≠ 1 red, döviz poliçede boş kur `KurCozucu`, `(prim + zeyil) × çözülen kur` < 10^15 ve hesap-döviz çiti poliçe dövizi.

### `/api/ui/v1` finans belge uç eşlemesi (F8.1b)

Kilit: `tests/RentACar.IntegrationTests/UiFinanceDocumentTests*.cs`. Uç kodu: `Web/Api/FinansBelge/`.
Sıra (DEVIR §5): kapsam (başka şube 403, başka kiracı 404) → anahtarla yazılmış kayıt (409 `mukerrer` +
`mevcut{…, ayniIcerik}`) → tarih/dönem kilidi → servis. Ceza ödemesinde envanter LOW-1 bu uçta kapalıdır.

| Uç | Satır | Anahtar | İkinci gönderim |
|---|---|---|---|
| `POST faturalar/manuel` | E14 | başlık zorunlu → fatura **Id** | 409 `mukerrer` + `mevcut` (aynı içerik: cari, net, KDV, manuel/iade değil → `ayniIcerik=true`). Yarışı kaybeden istek serviste PK'ye çarpar; aynı içerikte mevcut id 200 döner (ikinci belge yazılmaz) |
| `POST faturalar/{id}/iade` | E17 | yok (yapısal; FinanceReverse) | 400 "Bu fatura zaten iade edilmiş." (yarışta 400/409; tek iade) |
| `POST faturalar/toplu` | E16 | yok | her kira önce kapsamdan geçer (biri dışarıdaysa hiçbiri kesilmez, 403); tekrar yeni belge üretmez, atlananlarda görünür |
| `POST cezalar` | yok (defter yazmaz) | yok | her çağrı yeni ceza (boşluksuz no tüketir) — bilinen açık |
| `POST cezalar/{id}/yansit` | E25 | yok (yapısal) | 400 "Yalnız 'Yeni' durumundaki ceza yansıtılabilir." |
| `POST cezalar/{id}/odeme` | E26 | başlık zorunlu | 409 `mukerrer`; bu cezanın ödemesiyse `mevcut` (aynı kalem + hesap + tutar → `ayniIcerik=true`), başka cezanınsa `mevcut` yok |
| `POST cezalar/{id}/iptal` | yapısal | yok | yansıtılmış/ödemeli ceza 400 |
| `POST giderler` | E21 | başlık zorunlu | 409 `mukerrer` + `mevcut` (brüt, KDV, döviz, ödeme yöntemi, tip, cari, araç) |
| `POST giderler/{id}/odeme` | E23 | başlık zorunlu | 409 `mukerrer` + `mevcut` (servisin sessiz `null`'ı bu uçta 409'dur) |
| `POST gelen-efatura/{id}/giderlestir` | E24 | deterministik `RowKey(faturaId, i)`; başlık yok sayılır | 409 `mukerrer` |
| `POST satislar` | E35 | yok (yapısal) | 400 "Araç zaten satılmış." |

Gider ve araç satışında döviz saklanabilir ISO koda indirgenir ("TL" → TRY; #279 N1). `gelen-efatura/sync` GİB
entegrasyonu yapılandırılmamışken 400 döner (dürüst stub).

## Açık işler

- **LOW-2 (e-Fatura hayalet gönderimi):** dönem faturası yarışında kaybeden istek `eInvoice.SendAsync`'i (`InvoiceService.cs:353`) çağırıyor. Bu çağrı, `PostDonemAsync` mevcut id'yi dönmeden **önce** yapılıyor. Stub bugün `false` döndüğü için etkisi yok. Gerçek GİB bağlanınca yazılmayan bir fatura için ETTN alınır. **Gerçek e-Fatura açılmadan önce düzeltilmeli:** gönderimi commit'ten sonraya taşı ya da yalnız yazılan faturada yap. Aynı desen kira/fark faturası yarışında da (`CreateFromRentalAsync` / `PostFarkFaturasiAsync`) geçerli.
- **LOW-1 (anahtardan önce çalışan doğrulamalar):** bazı servis doğrulamaları anahtar kontrolünden önce çalışıyor:
  - `AracKrediService.cs:84`: iptal kredi;
  - `AracKrediService.cs:86`: tarih politikası;
  - `PenaltyService.cs:157/159`: tarih politikası ve dönem kilidi.

  Arada durum değişirse (kredi iptal edildi, dönem kapandı) anahtarlı tekrar 409 yerine 400 alır. Para etkisi yok: hiçbir şey yazılmaz.
- **Kapatıldı (bu PR):** `LedgerPoster`'ın başka kiracının künye PK'sine çarpan anahtarı sessizce yutması artık 400.

## Kural: sonuç zamanlamaya ve tutara bağlı olmaz

F1.4 öncesinde bazı akışlarda aynı mantıksal çift gönderimin sonucu değişiyordu:

- **Sıralı mı eşzamanlı mı:** servis ön-kontrolü 400, DB kısıtı 409 ya da sessiz. Etkilenenler: E08, E14, E18, E24, E25.
- **İlk gönderim bakiyeyi tam mı kısmi mi kapattı:** "bakiye yok" çiti anahtar kısıtından önce çalışıyordu. Etkilenenler: E05, E10–E12, E23, E26–E28, E32.

Düzeltme iki yoldan yapıldı:

1. Ön-kontrol, kısıtla **aynı tipi ve metni** fırlatır.
2. Anahtar varsa **ilk** kontrol edilir. Kontrol kilidin arkasında, bakiye/durum çitlerinden önce yapılır.

Operasyonlar arası tek tip sonuç hedeflenmedi. Her satır bugünkü türünü korur: 409 mükerrer, 400 iş kuralı ya da sessiz. Tek hedef, bir satırın sonucunun zamanlamadan ve tutardan bağımsız olmasıdır.
- **LOW-A (açık, SPA sözleşmesi):** kur/hesap istekte açık verilmediyse çağrı anında çözülür; ilk yazım başarılıyken kur değişirse birebir tekrar `farklı içerik` (409) alır. Para kaybı/çift yazım yok. Mesaj bu yüzden "yeniden göndermeden önce kontrol edin" der. **SPA kuralı:** 409 `mukerrer` + farklı içerikte yeni anahtarla OTOMATİK yeniden gönderim YAPILMAZ; kayıt yeniden yüklenir (F3.3 interceptor).
