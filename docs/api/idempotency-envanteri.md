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
| E19 | `DonemTahsilatService.KesVeTahsilEtDetayAsync` `FaturaDonemleri/FaturaDonemFiles.cs:70` | E18 + deterministik `RowKey(kiraId, dönemSıra)` | **Sessiz**: aynı fatura, `TahsilatYazildi = false`. Başka hesapla tekrar edilirse de tahsilat yazılmaz ama bu gizlenmez: çağıran "tahsilat daha önce alınmış" bildirir | E18 + E01 | doğrulandı (test) |
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
| `POST finans/tahsilat` | E01 | `tahsilatAnahtar` (DTO, yalnız `kiraId` ile; sunucuda yeniden hesaplanır) ▸ başlık; ikisi de yoksa 400 | 409 `mukerrer` (aynı ya da farklı içerik); iki sekme/iki kullanıcı aynı `tahsilatAnahtar` → ikincisi 409 |
| `POST finans/odeme` | E02 | başlık zorunlu | 409 |
| `POST finans/fatura` | E15 | yok (yapısal; başlık yok sayılır) | 400 "Kira zaten tam faturalanmış…" |
| `POST finans/donem-fatura` | E18/E19 | yok; tahsilat `RowKey(kira, sıra)` | 200 aynı `faturaId`, `tahsilatYazildi=false`, `bilgi` dolu (gizlenmez) |
| `POST finans/dis-hizmet` | E33 | başlık zorunlu | 409 |
| `POST finans/dis-hizmet/{id}/iptal` | E34 | yok (yapısal) | 400 "Kayıt zaten iptal edilmiş." |
| `POST finans/depozito/al` | E09 | başlık zorunlu | aynı içerik 200 aynı `id`; farklı içerik 409 |
| `POST finans/depozito/irat` | E12 | başlık zorunlu | aynı içerik 200 aynı `id`; farklı tutar/kira 409 |

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

**SPA uygulaması — kira formu sabit paneli (F4.4, `features/kira-formu/finans-paneli/`):**
- `tahsilatAnahtar`'ın kaynağı kira detayıdır: `GET kiralar/{id}` → `tahsilat` (`TahsilatBilgisi`; liste/pano ile
  aynı üretim; FinanceWrite + iptal olmayan kira; bakiye ≤ 0'da da dolar). Tahsilat isteği başlıksız gider.
- Form bir **satır kopyası** tutar (`TahsilatKopyasi`): boştaki form her yeni detayla kopyayı tazeler; kullanıcı
  formu doldurduysa ya da gönderim SONUÇLANMADIYSA (ağ/5xx/oturum/doğrulama) kopya donar ve yeniden deneme aynı
  anahtarla gider (anahtar sessizce yenisiyle ya da anahtarsızla değiştirilmez). 2xx ya da 409 `mukerrer`
  sonrası düğme yeni detay gelene dek kapalıdır; gelen detayın anahtarı alınır (ikinci meşru tahsilat).
- Bayat anahtarın 409'u ("kira kaydı değişmiş") HİÇBİR ŞEY yazmaz: istek `sessiz` gider, kayıt yeniden yüklenir,
  form içinde nötr uyarı + sunucu `detail`'ı gösterilir ("Mükerrer işlem" toast'u parayı kaydedildi sandırır).
- Ödeme, depozito al/irat, dış hizmet: işlem başına `Idempotency-Key` (`formGonderimi`/`GonderimKilidi`).
  Fatura, dönem faturası, dış hizmet iptali yapısal: başlık gönderilmez.

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
