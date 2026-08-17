# CLAUDE.md — RentACar (TürevRent fonksiyonel klonu)

> Bu dosya, bu repoda çalışan her Claude Code / geliştirici oturumunun **ilk okuması gereken** bağlam ve kurallardır. Kod yapısını tekrar anlatmaz; **kararları, sözleşmeleri ve "neden"leri** taşır.

## 1. Proje nedir
`turev2.turevrac.com` adresindeki **TürevRent** (ASP.NET WebForms, ~155 ekran, rent-a-car/filo ERP) yazılımının **sıfırdan, fonksiyonel eşdeğeri**. Hedef: **çok-kiracılı (multi-tenant) SaaS**, .NET 10 üzerinde temiz mimari.

## 2. Yığın (locked — değiştirmeyin)
- **.NET 10 (LTS)**, ASP.NET Core, **Blazor Server *statik* SSR** (interaktif circuit DEĞİL — formlar `method="post"` minimal-API uçlarına gider).
- **EF Core 10 + PostgreSQL (Npgsql)**. Para = `decimal`/`numeric`.
- **Temiz mimari**, katmanlar: `RentACar.Domain` → `Application` → `Infrastructure` → `Web` (+ `tests/RentACar.IntegrationTests`). Bağımlılık yönü içe doğru; Domain hiçbir şeye bağımlı değil.
- Central Package Management (`Directory.Packages.props`), `.slnx` çözüm dosyası.
- Form alan adları ve domain alanları **Türkçe** (Plaka, Cari, Tutar, Sube…); sınıf adları İngilizce (Vehicle, Customer, Quotation).

## 3. Çalışma modeli / güven sözleşmesi (EN ÖNEMLİ)
Kullanıcı **C# kodunu incelemez**. Doğruluk şuradan gelir:
1. **Testler** — her modül entegrasyon testiyle gelir (gerçek PostgreSQL).
2. **Canlı parite** — uygulama çalıştırılıp gözle doğrulanır.
3. **Bağımsız oracle ilkesi**: testteki *beklenen değerler*, kodu yazan mantıktan DEĞİL, elle kurulmuş senaryodan türetilir (ör. "3 gün × 100 = 300" sabiti, `BookingMath`'ten değil). Beklenen değeri asla rapor/servis kodundan üretme.
4. **Küçük, gözden geçirilebilir PR'lar** — her biri ayrı commit.
5. **Para tutan her PR için zorunlu adversarial inceleme**: ayrı bir ajan, kodu *çürütmeye* çalışır (işaret hatası, çok-döviz, idempotency, RLS sızıntısı, yetki) — canlı DB'ye karşı ampirik. Critical/High/Medium bulgu varsa düzeltilmeden commit yok.

**Süreç notları:**
- Commit mesajları detaylı ve Türkçe (ne + neden + test özeti). Son satır: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Varsayılan dal `main`. Yeni iş için **önce feature branch** aç, sonra commit/push (kullanıcı onayıyla).
- Tarihsel olarak push engelliydi → bundle ile teslim ediliyordu. **Artık Mac'te `gh` ile push açık** (origin = `github.com/BurakkYuce/Rent-A-Car-Service`).

## 4. Kilitli teknik kararlar (non-negotiable)
### Çok-kiracılık (gün-0)
- Her tenant verisi `ITenantOwned` (`Guid TenantId`).
- **İki savunma katmanı**: (a) EF global query filter `HasQueryFilter(x => x.TenantId == TenantId)`; (b) **Postgres RLS** — her tenant tablosunda `ENABLE` + `FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy.
- **İki rol**: `racar_owner` (DDL/migration, RLS bypass edebilir — uygulama KULLANMAZ) ve `racar_app` (runtime, `NOSUPERUSER NOBYPASSRLS` — uygulama DAİMA bununla bağlanır).
- Tenant, bağlantı başına `set_config('app.tenant_id', …)` ile taşınır (`TenantConnectionInterceptor`). RLS policy bunu `NULLIF(current_setting('app.tenant_id',true),'')::uuid` ile okur.
- `Tenants` ve `Users` **platform tablolarıdır** (RLS yok; owner yazar, app okur).

### Para & muhasebe
- `Money` = `readonly record struct (decimal Amount, string Currency, decimal Rate)`; `AmountInBase => Amount * Rate`.
- **Çift-taraflı defter**: `AccountLedgerEntry` (`AccountType`, `AccountRef`, `Direction` Debit/Credit, `Money`, `SourceType`, `SourceId`). `LedgerAccountType`: Cari/Kasa/Banka/Gelir/Kdv/Gider. `LedgerDirection` **`AccountLedgerEntry.cs` içinde** (Domain.Entities, Enums'ta DEĞİL).
- Her işlem **DENGELİ** küme yazar: `Σ Borç(base) == Σ Alacak(base)` — repo `PostAsync`/`LedgerPoster` bunu zorlar.
- **Cari bakiye** = `Σ SignedBase` (Debit:+, Credit:−), AccountRef=cariId. Pozitif = müşteri borçlu.
- Düzeltme **ters kayıtla** yapılır (silme/update yok). İdempotency: kısmi unique index + `UniqueViolation` yutma.
- **DB-immutability**: fatura/defter gibi tablolarda `rc_prevent_mutation()` BEFORE UPDATE OR DELETE trigger.
- **Boşluksuz sıra no** (per tenant): `SequenceAllocator.NextAsync(db, tenant, "XNo")` → `RZ-000001` vb. Insert ile AYNI transaction.

### Yetki
- `UserRole`: Admin/Yonetici/Operator/Muhasebe. `Permission`: ManageUsers/OperationsWrite/FinanceWrite/ViewReports. Matris: `RolePermissions`.
- Servis guard: `PermissionGuard.Require(user, Permission.X)` → `ValidationException`. Web ayrıca `RequirePermission`/`[Authorize(Roles=…)]` (çift savunma).
- **Şube kapsamı**: `BranchScope.Effective(user)` → Operatör yalnız `AssignedBranch`'ini görür; diğerleri tümünü.

### PII / KVKK (F2 — blind-index)
- Customer `TcKimlik/EhliyetNo/PasaportNo` at-rest **şifreli** (`*Enc`, `ISecretProtector`); düz-metin kolonlar legacy (uygulama YAZMAZ, açılışta `PiiBackfill` şifreleyip null'lar; ileride düşecek). Personel `TcKimlikEnc/MaasEnc` aynı desen.
- TC benzersizliği+tam-eşleşme araması **blind-index**: `TcKimlikHash` (HMAC-SHA256, `IPiiHasher`), kısmi unique index `(TenantId, TcKimlikHash)`. Kısmi TC araması bilinçli YOK.
- Anahtar `Pii:HmacKey` — **üretimde zorunlu** (Web/Api açılışta reddeder); dev/test sabit anahtar. Anahtar + DataProtection key-ring'i yedek kapsamında (bkz. `docs/ops/yedekleme.md`).
- Okumalar `CustomerRepository.Decrypt` ile bellekte çözülür — tüketiciler düz değeri görür; DB'de yalnız cipher+hash.

## 5. Yeni tenant-owned tablo ekleme reçetesi
1. `Domain/Entities/X.cs` : `ITenantOwned, IAuditable`, `Id` Guid (ValueGeneratedNever), audit timestamp'leri.
2. `AppDbContext`: `DbSet<X>` + config'i `Persistence/Configurations/` altında ilgili alan-dosyasına `internal sealed class XConfig : IEntityTypeConfiguration<X>` olarak ekle (kolon tipleri `numeric(19,4)`, `HasIndex (TenantId, doğal-anahtar) IsUnique`). **`HasQueryFilter` YAZMA** — `OnModelCreating`'deki merkezi döngü tüm `ITenantOwned` entity'lere tenant filtresini otomatik uygular (`ModelGuardTests` kapsamı doğrular).
3. `dotnet ef migrations add AddX --project src/RentACar.Infrastructure --startup-project src/RentACar.Infrastructure`.
4. **Migration'a RLS bloğunu ELLE ekle** (EF üretmez): `ENABLE`+`FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation … USING/WITH CHECK (NULLIF(current_setting('app.tenant_id',true),'')::uuid)` + `GRANT … TO racar_app`. (Mali belgeyse ayrıca immutability trigger; değilse tam CRUD grant.)
5. `IXRepository` (Application) + `XRepository` (Infrastructure, `IDbContextFactory<AppDbContext>`, `AsNoTracking`).
6. `XService` (Application) — doğrulama + guard + iş kuralı.
7. DI: `Application/DependencyInjection.cs` (`AddScoped<XService>`) + `Infrastructure/DependencyInjection.cs` (`AddScoped<IXRepository, XRepository>`).
8. Web: `Components/Pages/.../XList.razor` + `X/XEndpoints.cs` + `Program.cs` `MapXEndpoints()` + `_Imports.razor` using + `MainLayout.razor` nav.
9. Test: `tests/…/XTests.cs` — bağımsız oracle (CRUD + benzersizlik + **tenant izolasyon** `racar_app` ile + yetki).

**Tuzak:** opsiyonel `decimal?`/`int?`/`DateTimeOffset?` form alanları boş string ("") gelince `[FromForm]` 400 verir → web ucunda `string?` parametre alıp `FormParse.Dec/Int/Date/Id` ile çevir.

## 6. Mevcut durum (2026-07-15, PR #76 itibarıyla — 1242 entegrasyon testi yeşil)
**Tamam (ilk seri Faz 1–5, ~29 PR):**
- İskelet: çok-kiracılık (RLS+FORCE), audit log, boşluksuz no, 2-aşamalı login, rol/yetki matrisi, şube kapsamı.
- Araç, Cari, Rezervasyon→Kira→Teslim/Dönüş, çift-taraflı defter, Kasa/Banka (tahsilat/ödeme/virman/ters), Fatura (KDV), Gider.
- Sigorta/MTV/Muayene + vade panosu, Ceza + HGS yansıtma, Araç satış + BAF, Servis/bakım.
- Raporlar: kasa/banka defteri, gelir-gider, cari bakiye+yaşlandırma, filo doluluk, servis maliyeti.
- Müsaitlik arama, detay sayfaları, liste arama/filtre/sayfalama, sözleşme/fatura yazdırma, denetim görünümü.
- **Şube master** (PR #27, additive — metin alanları korundu, FK'ye çevrilmedi), **Teklif→rezervasyon** (PR #28), **Rezervasyon takvimi** (PR #29).
- **Kira MEGA-FORMU** (2026-07-10, PR #28-#31, 1044 test): `/kiralar/yeni` + `/kiralar/{id}` TEK sözleşme ekranı (TürevRent Kiralama.Aspx paritesi; RentalDetail emekli). 8 ana sekme + Ayrıntılar'da 8 alt-sekme + sticky finans paneli (5 alt-sekme). Desenler: tek ana form tüm panelleri sarar (hidden attr — submit'e girer); ikincil işlem formları ana formun DIŞINDA + kontroller `form=` attribute'üyle (iç içe form yasak); sekme/ayna/arama JS'i `rc-kira-tabs.js` (hash deep-link `#sekme=`, gizli-panel `invalid` yakalama, `[data-mirror]` iki yönlü senkron flatpickr-farkındalıklı, datalist id-çözümü); CANLI hesap SUNUCU motorundan — `GET /kiralar/hesapla` (KiraHesapService→PricingService) + `GET /kiralar/donus-hesapla` (PreviewReturnAsync→ReturnMath), UI formül taşımaz. Açık kira güncelleme: `RentalService.UpdateOpenAsync` + `RentalUpdateInput` (whitelist TİP düzeyinde — para/tarih alanları tipte yok). RentalContract'a 31 bilgi kolonu (Kaynak fix dahil). TUZAKLAR: hash yazımı `location.pathname+search+'#'` ile (çıplak `#` Blazor'da base href'e çözülüp path'i köke düşürür); datetime-local prefill `.UtcDateTime` (binding AssumeUniversal — LocalDateTime basmak hayalet +1 gün uzatma üretiyordu); Personel dropdown `ListForSelectAsync` (OperationsWrite, PII'sız — ListAsync ManageUsers'lı, operatörde patlar).
- **Araç ÖN MUHASEBE** (2026-07-13, PR #37-#42, 1085 test): (1) Karlilik ATIF düzeltmesi — fark faturası (`KaynakKiraId`), ServisYansitma, ceza RentalId-fallback gelirleri "(Atanmamış)"tan doğru araca (toplam değişmez, dağıtım düzelir). (2) **Araç Karnesi** `/raporlar/arac-karne/{id}` (finans-rol kapılı; Karlilik/VehicleList/VehicleDetail'den drill-down): defterden P&L (yıllık + kaynak/kategori kırılım %'li) + "neyi ne zaman" olay çizelgesi (`DeftereYansir` bayraklı; kira=faturalanmış-mı) + kurumsal KPI (Doluluk cap-100, RevPACD, ADR, km-maliyet, marj, ROI [satılmışta kapanış: satış gelirde−alım düşülür], geri-ödeme ayı [decimal, >1200→null — int-taşma dersi], TCO, gerçekleşen amortisman [satılmışta =AlimBedeli — kalıntı çift sayılmaz], EKONOMİK kâr) + `MaliyetHesapService` başabaş modeli. (3) **Filo Analiz** `/raporlar/filo-analiz`: TÜM filodan tohumlu satırlar (hareketsiz araç 0-P&L görünür; silinmiş "(bilinmeyen araç)" mutabakat satırı, kohort-dışı) + sıralama + yaş kohortu (gün-hassas ay). (4) Export'lar (`KarneExportKatalog`, test-edilebilir). DESENLER: P&L yalnız DEFTERDEN (kaynak-varlık tutarı asla toplanmaz → çift-sayım sıfır; karne==Karlilik parite testi kalıcı kilit); KPI'lar sahiplik-penceresi ÖMÜR-BOYU — sayfa dönem-filtresi P&L'i daraltır KPI'yı DARALTMAZ (karışık-payda yasak; ömür toplamları repo'dan ayrı döner); olay tutarları bilgi amaçlı brüt/native. TUZAKLAR: ledger'da VehicleId kolonu yok — gider `AccountRef`'te, gelir `SourceId`→kaynak-varlık→araç zinciriyle; `ServiceRecord` deftersiz (gerçek maliyet ayrıca Expense'le girer, yoksa gider-kategoride 0 görünür — doğru); dövizli ödeme uçları (Sigorta/Servis/Depozito/Satış) kur'u çağırandan alır default `1m` — yazma-yolu zaafı AÇIK İŞ (öneri: KurService'e bağla); oranlar yalnız POZİTİF paydayla (negatif dönem-neti yüzdesi yanıltır → null).

- **"HEPSİ" SERİSİ** (2026-07-14/15, PR #43-#76, 1242 test — eksik/ertelenmiş işlerin tam kapanışı, 6 faz):
  - **FAZ 1 para**: `KurCozucu` (açık kur>0 kazanır; null→TRY=1, değilse KurService; bulunamazsa gürültülü red — dövizli ödeme uçlarındaki `kur=1m` zaafı KAPANDI); depozito İRAT→Gelir (`DepozitoIrat` tablosu, karne atfı); AracKredi taksiti GERÇEK gider postlar (ExpenseType.Finansman, idempotent `kredi:{id}:taksit:{sira}`); kira-seviyesi `OzelKdvOran`/`DamgaVergisi` → fatura varsayılanı.
  - **FAZ 2 analitik**: vade panosu araç boyutu + karne vade bloğu + bakım-km; **Tut/Sat sinyali** (3 kural: gider/İkinciEl>0.45, sınıf-ortalamasının 1.5 katı, km-maliyet trendi — `TutSatHesap` saf, eşikler `TutSat` appsettings-override); `SinifEndeks` + filo HAVUZ KPI (Σ havuz, ortalama değil); kalıntı projeksiyonu (azalan bakiye); `VehicleKmLog` zaman serisi (Dönüş/Servis/Manuel; dönem-km).
  - **FAZ 3 fiyat motoru v1 tamam**: A0 kampanya-kodu çiti (kodlu kural otomatik seçime SIZMAZ — canlı bug fix); `GunHaftalik`(8-29)/`GunAylik`(30+) kademeleri; müşteri segment indirimi (`Customer.Sinif`+`RentalRule.MusteriSegment`, segment-birebir öncelik); Kaynak→Kanal bağlama (yalnız aktif `ReservationSource` eşleşirse); promosyon kodu (REPLACE, kapsam-dışı gürültülü red); genç/ek-sürücü + drop ücretleri = sistem `RentalAddOn` satırları (`SYS-*`, `FeeLineService`, BaseGross'a dokunmaz, önizleme==kayıt); tenant KDV varsayılan zinciri `kdvRate ?? OzelKdvOran ?? TenantSettings.VarsayilanKdvOrani ?? 0.20` + `KdvOranSnapshot` (net-mod kilidi); doluluk çarpanı (`DolulukFiyatKural` tablosu, CHECK≤50 + uygulama kemeri, rezervasyon-update'te surge atlanır).
  - **FAZ 4 mega-form S2**: manuel provizyon yaşam döngüsü (`ProvizyonDurum`, deftere yazmaz); **dönemsel faturalama** B1-B4 (`FaturaDonemi` planı [ProRata + kalan-yöntemi son dönem], kesim=fark-with-cap [`KaynakKiraId` unique → çift-faturalama yapısal imkânsız], StickyPanel dönem paneli + kes-ve-tahsil, `DonemFaturaJob` ayar-kapılı [`DonemselFaturalamaJob`/`DonemselOtomatikTahsilat` TenantSettings kolonları, default false]); **B2B Dış Hizmet Alımı** TAM DEFTERLİ (`DisHizmetAlimi`, gapless `DH-`, bedel Borç Gider[araç]/Alacak Cari + komisyon Borç Cari/Alacak Gelir, iptal ters-kayıt SourceType korunur; GelirGider İKİ YÖNLÜ netleme dersi); mega-form küçükleri (HGS listesi, doluluk kutuları, Opsiyon, Risk guard [giriş noktasında, onay rolü sunucuda], Ek Koşullar PDF); OTA 8 bileşen kolonu (`Reservation.Ota*`).
  - **FAZ 5 Şube-FK yetki geçişi C1-C5 TAMAM** (#70-#74): `assigned_sube_id` claim çift-yazım → `BranchScope.BranchFilter`+`InScope` TEK kural → tüm liste yüzeyleri FK-farkındalı (ICS dahil) → booking belgelerinde türetilmiş `CikisSubeId` (`IOfficeScoped`+`OfficeBranchInterceptor`: CikisOfisi→Location→SubeId; composite FK; backfill; operatör artık şubesinin TÜM ofislerini görür — bilinçli genişletme) → **C5 daraltma: iki FK doluysa FK TEK BAŞINA karar verir** (rename-çakışması sızıntısı F2 kapalı; FK'sız satırda metin yolu KALICI). Adversarial kapanışları: `RentalAddOnService` + `QuotationService` tekil şube-guard'ları.
  - **FAZ 6 genişlik**: `/tarife-aktar` toplu fiyat içe aktarım (Beklemede-çitli — onay kolonu enjeksiyonu yok sayılır, motor onaysızı seçmez); Home filo KPI şeridi (havuz Doluluk/RevPACD/ADR) + 6-ay gelir mini-trend (`GetAylikGelirTrendAsync`); `FiloBildirimUretici` 2 yeni bildirim türü ("Bakım-Km" kalan≤1000 [sentetik anahtar: hedef-km Unix-saniye → döngü başına tek] + "Tut/Sat" ≥2 sinyal [ay-çıpası anahtar]) — rapor sayfalarıyla AYNI kaynaklar (`OrtakSorgular.PeriyodikServisAsync`/`TutSatHamAsync`).
  - DESENLER: para PR'larında zorunlu adversarial inceleme (probe'lar kalıcı teste çevrilir); üreticiler servis DEĞİL `Uretici.RunAsync(db, tenant, now)` (yetki yüzeyi büyümez); paylaşımlı sorgular `OrtakSorgular`'da (O12a); guard GİRİŞ noktasında + rol doğrulaması SUNUCUDA.

- **BİLDİRİM OMURGASI** (2026-08-17): `ISmsService` artık **gerçek** — `TwilioSmsService` (WhatsApp ile AYNI Twilio hesabı; ayrı sağlayıcı sözleşmesi yok; gönderen önceliği tenant `SmsBaslik` → `Twilio:SmsFrom` → `Twilio:MessagingServiceSid`; teslim durumu SID ile doğrulanır). **E-posta sıfırdan açıldı**: `IEmailSender` portu + `MailKitEmailSender` (port 465→SslOnConnect, 587→STARTTLS; istisna sızmaz, Türkçe hata döner) + `BildirimKanaliService` (tenant SMTP ayarını okur/çözer; **`TenantSettingsService` üzerinden GEÇMEZ** — o ManageUsers ister, bildirimi tetikleyen akışlar operatör/kullanıcısızdır). `TenantSettings`e `SmtpGonderenAdres`/`SmtpGonderenAd` eklendi (kimlik kullanıcı adı ≠ gönderen; SPF/DKIM gönderen adrese bakar). Ayarlar'a E-posta + SMS test butonları (WhatsApp testiyle aynı desen: gerçek kod yolu + teslim doğrulaması).
- **MÜŞTERİ BİLDİRİMLERİ** (2026-08-17): `MesajSablon` (tenant × tür × kanal, tek şablon) + `GidenMesaj` (gönderim kaydı; **idempotency ŞEMADA** — `(TenantId,Anahtar)` unique). `MusteriBildirimService` TEK giriş: şablon çöz → yer tutucu doldur → **önce KAYIT sonra GÖNDERİM** (ters sırada çökme çift mesaj üretir) → durum kalıcılaştır. `SablonDoldur` saf: bilinmeyen yer tutucu AYNEN kalır (sessiz boşluk yerine görünür hata), e-postada DEĞERLER html-kaçışlı / şablonun kendi HTML'i korunur, SMS'te kaçış yok. `GidenMesaj.DegerlerJson` KRİTİK: şablon olay anında yoksa mesaj **Kuyrukta** bekler ve şablon yazılınca doğru içerikle gider — bu kolon olmasa kurulumun ilk günündeki tüm mesajlar kalıcı ölürdü. Durumlar: Kuyrukta/Gonderildi/Basarisiz/**IzinYok** (KVKK terminal). Tetikleyici: public talep → `TalepAlindi`; job → `TeslimHatirlatma` (yarın çıkış) + `IadeHatirlatma` (bugün dönüş) + kuyruk yeniden deneme (max 5, 3 günden eski denenmez). Hatırlatma anahtarı GÜN bileşeni taşır (yoksa çok günlük kirada tek mesaj çıkardı). Job yolu scoped servis kullanamadığı için `DogrudanMesajRepository`/`DogrudanAyarRepository` var — mantık KOPYALANMAZ, aynı servis job'ın context'i üzerine kurulur. Ekran: `/mesaj-sablonlari` (ManageUsers). SMTP göndericisi Web'den **Infrastructure'a taşındı** (Web/Api/PublicSite üçü de kullanıyor).
- **ÖDEME ADAPTÖRÜ — iyzico** (2026-08-17): `IPosService` **BARINDIRILAN ödeme sayfası** modeline göre yeniden şekillendi (`BaslatAsync`/`SonucAsync`/`KapatAsync`/`IptalAsync`/`IadeAsync`). Kart verisi sunucumuza UĞRAMAZ — doğrudan `/payment/preauth` ucu kart numarası ister, o yol PCI kapsamını üstümüze alacağı için BİLİNÇLİ kullanılmıyor (`RentalContract.cs:162` kararıyla tutarlı). Uç haritası sandbox'a karşı **AMPİRİK** doğrulandı: preauth `/payment/iyzipos/checkoutform/initialize/preauth/ecom`, auth `.../auth/ecom`, sonuç `/payment/iyzipos/checkoutform/auth/ecom/detail`, kapama `/payment/postauth`, iptal `/payment/cancel`, iade `/payment/refund`. TUZAKLAR: imza yükü SIRALI (`randomKey+uriPath+requestBody`) ve gövde BİREBİR aynı metin olmalı (yeniden serileştirme → "Geçersiz imza 1000"); imza küçük-harf HEX; `status=success` yalnız SORGU başarılı demek, ödeme başarısı `paymentStatus=="SUCCESS"` (karıştırmak ödenmemiş kiralamayı ödenmiş sayar); iade `paymentId` DEĞİL `itemTransactions[].paymentTransactionId` ister; tutar biçimi InvariantCulture nokta-ondalık (tr-TR virgül üretip isteği reddettirir); `basketItems` toplamı `price`a eşit olmalı; `itemType` `VIRTUAL`/`PHYSICAL` (VIRTUAL_PRODUCT → 5057). `IyzicoImza` saf ve dokümandaki örnek başlıkla birebir kilitli. Kimlikler repo DIŞINDA (`~/.racar-iyzico.env`); `IyzicoCanliSandboxTests` kimlik yoksa ATLANIR (CI). **Para yoluna henüz BAĞLI DEĞİL** — provizyon/defter bağlama ayrı PR ve zorunlu adversarial inceleme.
  **DÜRÜST STUB KURALI (kalıcı):** yapılandırma yokken hiçbir stub "başarılı" dönmez — SMS/POS/KABİS `false`'a çevrildi (e-Fatura zaten öyleydi). Sahte başarı kalıcı kayda yanlış yazdırır: sahte ETTN, var olmayan POS işlem referansı, yapılmamış KABİS bildirimi. `IntegrationStubTests` bunu port başına kilitler.

**Bilerek ertelendi (kullanıcı kararıyla):**
- **Gerçek entegrasyonlar** (e-Fatura/GİB, gerçek HGS/banka/POS) — **stub**; kimlik/credential gerektirir, açmadan önce kullanıcıya sor. (SMS ve e-posta 2026-08-17'de kapandı.)
- **Canlı TürevRent kuruş-kalibrasyonu** (fiyat motoru oranlarının canlıyla birebir doğrulanması).

**Eksik (genişlik) — 2026-08-06 canlı taramasıyla ÖLÇÜLDÜ:** canlıda 156 ekran; bizde **6 tam · 28 kısmi · 48 yok · 57 para-sınıfı · 5 erişilemez · 8 canlıda-bozuk · 4 doğrulanamadı**. Eksiklerin çoğu ekranın hiç olmaması değil **alan derinliği**; çekirdek akış (araç→müşteri→rezervasyon→kira→dönüş→fatura→tahsilat) uçtan uca çalışıyor. **`docs/parite` artık GÜNCEL** — önce `docs/parite/YONETICI-OZETI.md` oku; ekleme reçeteleri `docs/parite/10-ekleme-desenleri.md` (D1–D9). Ham HTML repo dışında (`~/turev-parite-2026-08/`), erişim yöntemi `docs/parite/README.md`'de.

## 7. Yerelde çalıştırma (Mac)
Önkoşul: .NET 10 SDK + PostgreSQL (Homebrew `postgresql@15` çalışıyor).
```bash
# postgres çalışmıyorsa:
brew services start postgresql@15
# roller + db + grant (tek seferlik, idempotent) — bkz. scripts/db-init-roles.sql + bu reçete
# (racar_owner / racar_owner_pw, racar_app / racar_app_pw, db racar)
# migration + seed açılışta otomatik:
ASPNETCORE_URLS=http://localhost:5220 dotnet run --project src/RentACar.Web
```
**Bağlantı (appsettings.json):** runtime = `racar_app`, migrator = `racar_owner`, db `racar`, port 5432.
**Giriş (seed):** firma `yucerent` / kullanıcı `umit` / şifre `umit1376` (Admin); `operator`/`umit1376` (Operatör); firma `demo` / `umit` / `umit1376`.

## 8. Test
```bash
RACAR_TEST_PG_ADMIN="Host=localhost;Port=5432;Username=burak;Database=postgres" \
  dotnet test RentACar.slnx -c Debug
```
- `PostgresFixture` test başına gerçek DB + roller (RLS dahil) kurar; `[Collection("postgres")]`.
- `host.ScopeFor(tenant, userId, userName, role=Admin, assignedBranch)` ile servis çağır.
- İzolasyon testleri MUTLAKA `racar_app` ile bağlanır (fixture öyle ayarlı).
- `RACAR_TEST_PG_ADMIN` superuser admin bağlantısı (Mac'te kullanıcı adın superuser, ör. `burak`, trust auth).

## 9. Sıradaki iş (öneri sırası)
Kimliksiz (credential'sız) backlog 2026-07-15 itibarıyla TÜKENDİ ("hepsi" serisi §6). Kalanlar:
1. **Gerçek entegrasyonlar** (e-Fatura/GİB, SMS, HGS, banka/POS) — kimlik/credential gerekir; port'lar/stub'lar hazır. Açmadan önce kullanıcıya sor.
2. **Canlı TürevRent kuruş-kalibrasyonu** — fiyat motoru oranlarının canlı sistemle birebir doğrulanması (çerez erişimi mevcut, bkz. memory).
3. Kullanıcının yeni istekleri / canlı kullanım geri bildirimi.
