# RentPro — Mimari ve Proje Tanıtımı

> Bu belge projeyi ilk kez görecek biri için yazıldı: **ne olduğu, nasıl kurulduğu, hangi
> kararların neden verildiği.** Kod okumadan sistemin bütününü anlamayı hedefler.
> Günlük çalışma kuralları için `CLAUDE.md`, kalıcı kararlar için `docs/KARARLAR.md`, yol haritası
> (Blazor → Angular geçişi) için `docs/roadmap/` klasörüne bakın.

---

## 1. Proje nedir

**RentPro**, `referans-sistem.example` adresinde çalışan **referans sistem** adlı rent-a-car / filo ERP
yazılımının (ASP.NET WebForms, ~156 ekran) **sıfırdan yazılmış fonksiyonel eşdeğeridir.**

Kopyalanan şey kod değil, **iş kuralları**: bir aracın filoya girişinden kiraya çıkışına, dönüşte
km/yakıt farkının hesaplanmasından faturaya ve tahsilata, oradan da aracın satılıp kâr-zararının
kapanmasına kadar bütün süreç.

Klasik "CRUD uygulaması" değil; merkezinde **çift taraflı muhasebe defteri** olan bir mali sistem.
Ekranlar bu defterin etrafında duruyor.

| | |
|---|---|
| **Hedef** | Çok kiracılı (multi-tenant) SaaS — her kiralama firması aynı kurulumda, verileri birbirinden yalıtılmış |
| **Durum** | Çekirdek akış uçtan uca çalışıyor; 68 fazlık derinleştirme planının 50'si tamamlandı |
| **Ölçek** | ~2.100 entegrasyon testi, 103 tablo, 183 veritabanı migration'ı, ~150 ekran |
| **Kalan** | Gerçek dış entegrasyonlar (e-Fatura/GİB, SMS, HGS, banka/POS) — hepsi stub, kimlik bilgisi bekliyor |

---

## 2. Teknoloji yığını

| Katman | Seçim | Neden |
|---|---|---|
| Platform | **.NET 10 (LTS)**, C# | Uzun destek, tek dil |
| Web | **ASP.NET Core + Blazor Server, statik SSR** | Aşağıya bakınız — bilinçli ve alışılmadık bir karar |
| Veri | **EF Core 10 + PostgreSQL** (Npgsql) | Row Level Security için Postgres şart |
| Para | `decimal` / `numeric(19,4)` | `double` ile para tutmak yasak |
| Mimari | Temiz mimari (4 katman) | Bağımlılık yönü daima içe doğru |
| Test | xUnit + **gerçek PostgreSQL** | In-memory DB, RLS'i test edemez |
| Paketler | Central Package Management (`Directory.Packages.props`) | Sürümler tek dosyada |

### Blazor "statik SSR" kararı

Blazor genelde **interaktif** kullanılır: tarayıcı ile sunucu arasında kalıcı bir WebSocket
(circuit) açılır, her tuş vuruşu sunucuya gider. Bu proje bunu **bilinçli olarak kullanmıyor.**

Formlar klasik `method="post"` ile minimal-API uçlarına gidiyor — 1990'ların web'i gibi. Sebep:

- **Dayanıklılık.** Circuit kopunca kullanıcı yarım kalmış bir formla baş başa kalır. Bir kiralama
  sözleşmesi doldururken bunun olması kabul edilemez.
- **Ölçek.** Her açık sekme için sunucuda canlı bir circuit tutmak, yüzlerce şubeli bir SaaS'ta
  pahalıdır.
- **Öngörülebilirlik.** POST → doğrula → yaz → yönlendir zinciri test edilebilir; bir devlet
  makinesinin ara durumları değil.

Bedeli: canlı hesaplama gereken yerlerde (kira mega-formu) küçük JavaScript parçaları ve sunucudan
`GET /kiralar/hesapla` gibi hesap uçları yazıldı. **Formül asla istemciye taşınmadı** — tarayıcı
sunucuya sorar, cevabı gösterir.

---

## 3. Katmanlar

```
RentACar.Domain          →  Application  →  Infrastructure  →  Web / Api / PublicSite
   (hiçbir şeye              (iş kuralı)     (EF Core, repo,      (sunum)
    bağımlı değil)                            Postgres)
```

| Proje | Satır | İçerik |
|---|---:|---|
| `RentACar.Domain` | ~6.400 | Varlıklar (103 tablo), enum'lar, `Money` tipi. Hiçbir dış bağımlılığı yok. |
| `RentACar.Application` | ~23.900 | 93 servis — doğrulama, iş kuralı, yetki, fiyat motoru, raporlar. Repository *arayüzleri* burada. |
| `RentACar.Infrastructure` | ~16.300 (+183 migration) | EF Core yapılandırması, repository *gerçeklemeleri*, RLS, arka plan işleri. |
| `RentACar.Web` | ~33.500 | 150 Razor sayfası + 90 endpoint dosyası. Yönetim arayüzü. |
| `RentACar.Api` | ~1.300 | JWT korumalı REST API (mobil/entegrasyon için). |
| `RentACar.PublicSite` | ~2.100 | Kiracıya özel halka açık site: vitrin, blog, müsaitlik/fiyat sorgu, talep formu. |
| `tests/RentACar.IntegrationTests` | ~52.400 | 325 dosya, ~2.100 test. Kod tabanının en büyük parçası. |

**Dikkat çeken oran:** test kodu, uygulama kodundan büyük. Bu tesadüf değil — bkz. bölüm 8.

### Adlandırma kuralı

Sınıf adları İngilizce (`Vehicle`, `Customer`, `Reservation`), **alan ve form adları Türkçe**
(`Plaka`, `Cari`, `Tutar`, `Sube`). Sebep: alan adları doğrudan iş dilinden geliyor; "Plaka"yı
"LicensePlate"e çevirip formda tekrar "Plaka" yazmak, iki kez düşünmeye zorlayan gereksiz bir
katman olurdu.

---

## 4. Çok kiracılık — iki bağımsız savunma katmanı

Bu, projenin **en kritik** tasarım kararı. Bir kiracının verisini diğerine sızdırmak, bu tür bir
üründe telafisi olmayan hatadır. Bu yüzden koruma tek katmana bırakılmadı.

### Katman 1 — Uygulama: EF global query filter

Kiracıya ait her varlık `ITenantOwned` arayüzünü taşır (`Guid TenantId`). `AppDbContext`
açılışta **merkezî bir döngüyle** bu arayüzü uygulayan tüm varlıklara otomatik olarak
`x => x.TenantId == TenantId` filtresini ekler.

Kritik nokta: filtre **tek tek yazılmaz.** Yeni bir tablo eklerken geliştirici filtreyi unutabilir;
merkezî döngü unutmayı imkânsız kılar. Bir test (`ModelGuardTests`) bunu ayrıca doğrular.

### Katman 2 — Veritabanı: PostgreSQL Row Level Security

Uygulama katmanı hatalı olsa bile (bir `IgnoreQueryFilters()`, ham SQL, bir hata) veritabanı
kendi başına savunur. Her kiracı tablosunda:

```sql
ALTER TABLE "Vehicles" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Vehicles" FORCE  ROW LEVEL SECURITY;   -- tablo sahibini de bağlar
CREATE POLICY tenant_isolation ON "Vehicles"
  USING      ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
  WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
```

78 tabloda bu politika kurulu.

### İki veritabanı rolü

| Rol | Yetki | Kim kullanır |
|---|---|---|
| `racar_owner` | DDL, migration. RLS'i esnetebilir. | **Yalnız migration.** Uygulama ASLA bununla bağlanmaz. |
| `racar_app` | `NOSUPERUSER NOBYPASSRLS` | Uygulama **daima** bununla bağlanır. RLS'i atlayamaz. |

Kiracı kimliği bağlantı başına `set_config('app.tenant_id', …)` ile taşınır
(`TenantConnectionInterceptor`). Politika bunu okur.

`Tenants` ve `Users` **platform tablolarıdır** — RLS yok; owner yazar, uygulama okur.

**Test disiplini:** izolasyon testleri her zaman `racar_app` ile bağlanır. `racar_owner` ile
yazılan bir izolasyon testi hiçbir şey kanıtlamaz.

---

## 5. Para ve muhasebe — sistemin kalbi

### Çift taraflı defter

Tek bir tablo bütün mali gerçeği tutar: `AccountLedgerEntry`.

```
AccountType   : Cari | Kasa | Banka | Gelir | Kdv | Gider | Depozito | DonemSonucu
AccountRef    : hangi varlık (müşteri, araç, kasa hesabı…)
Direction     : Debit (Borç) | Credit (Alacak)
Money         : (Tutar, Döviz, Kur) — AmountInBase = Tutar × Kur
SourceType/Id : bu satırı hangi belge doğurdu
```

**Değişmez kural:** her işlem **dengeli** bir küme yazar —
`Σ Borç(baz para) == Σ Alacak(baz para)`. Bunu geliştiricinin dikkatine bırakmıyoruz;
`LedgerPoster.PostAsync` dengeyi kontrol eder ve bozuksa yazmayı reddeder.

Örnek — 1.000 TL tahsilat:

| Hesap | Yön | Tutar |
|---|---|---:|
| Kasa | Borç | 1.000 |
| Cari (müşteri) | Alacak | 1.000 |

Cari bakiye = `Σ İşaretli Baz` (Borç +, Alacak −). Pozitif = müşteri borçlu.

### Türetilmiş kurallar

- **Silme yok, düzeltme ters kayıtla.** Yanlış tahsilat silinmez; yönleri çevrilmiş yeni bir kayıt
  yazılır. Mali geçmiş yeniden yazılamaz.
- **Veritabanı seviyesinde değişmezlik.** Fatura/defter tablolarında `rc_prevent_mutation()`
  BEFORE UPDATE OR DELETE trigger'ı. Uygulama hatalı olsa bile `UPDATE` geçmez.
- **Boşluksuz sıra numarası.** `RZ-000001`, `TH-000042`… `SequenceAllocator` ile, kaydın kendisiyle
  **aynı transaction'da**. Mali belgede numara atlaması denetimde soru işaretidir.
- **P&L yalnız defterden okunur.** Kârlılık, Araç Karnesi, Filo Analiz gibi raporlar tutarları
  kaynak belgeden (sözleşme, sipariş) değil **yalnız defterden** toplar. Kaynak belge tutarını
  rapora eklemek çift sayım demektir ve bu proje geçmişinde tam olarak bu sınıf bir hata yaşandı.
- **İdempotency.** Formlar her render'da bir işlem anahtarı basar; kısmi unique index aynı anahtarla
  ikinci yazımı reddeder ve uygulama bunu sessizce yutar. Çift tıklama iki tahsilat üretemez.

### Bilgi alanı vs. defter alanı

Sistemde çok sayıda tutar alanı var ama **hepsi deftere yazmaz.** Politika:

> Yeni eklenen tutar/oran alanları (hukuk dosyası tahsilatı, sigorta zeyil primi, acente komisyon
> oranı, maliyet simülasyonu…) **bilgi alanıdır.** Gerçek para hareketi yalnız Kasa/Banka
> tahsilat-ödeme akışından geçer.

Sebep: iki yolu birden açmak çift sayım üretir. Bu ayrım her seferinde **kırılgan bir regresyon
testiyle** kilitlenir — alan uçuk bir değerle doldurulur ve ilgili raporun rakamlarının
**değişmediği** doğrulanır.

---

## 6. Yetki ve kapsam

**Roller:** Admin · Yönetici · Operatör · Muhasebe
**İzinler:** `ManageUsers` · `OperationsWrite` · `FinanceWrite` · `ViewReports`
Eşleme `RolePermissions` matrisinde.

Yine **iki katman**:

1. **Servis:** `PermissionGuard.Require(user, Permission.FinanceWrite)` → `ValidationException`
2. **Web:** `RequirePermission(...)` / `[Authorize(Roles = …)]`

Web katmanı atlanabilir (doğrudan API çağrısı, bir uç unutulmuş olabilir); servis katmanı atlanamaz.

**Şube kapsamı:** `BranchScope` tek kural. Operatör yalnız kendi şubesini görür, diğer roller
tümünü. Kural tek yerde tanımlı; her liste yüzeyi onu çağırır.

> **Kalıcı ders:** guard **giriş noktasına** konur. Bir dönemde guard yanlış katmana (kabul/dönüşüm
> noktasına) konduğu için bütün bir iş akışı kilitlendi. Kontrol, işlemin başladığı yerde olmalı.

---

## 7. Kişisel veri (KVKK)

- Müşteri **TC kimlik / ehliyet / pasaport** numaraları veritabanında **şifreli** durur
  (`*Enc` kolonları, `ISecretProtector`). Personel TC ve maaşı aynı desende.
- Şifreli veride arama yapılamaz. Bu yüzden TC benzersizliği ve tam eşleşme araması
  **blind index** ile çözüldü: `TcKimlikHash` (HMAC-SHA256) + kısmi unique index.
  **Kısmi TC araması bilinçli olarak yok** — şifreli veride mümkün değil.
- Anahtar (`Pii:HmacKey`) üretimde **zorunlu**; yoksa uygulama açılışta reddeder. Anahtar ve
  DataProtection key-ring'i yedekleme kapsamında (`docs/ops/yedekleme.md`).
- Okuma yolunda değerler bellekte çözülür; tüketiciler düz metni görür, veritabanında yalnız
  şifreli metin + hash durur.

---

## 8. Nasıl doğruluyoruz — güven modeli

Bu projenin sahibi C# kodunu satır satır incelemiyor. Doğruluk şuradan geliyor:

1. **Gerçek veritabanına karşı entegrasyon testleri.** In-memory sahte veritabanı yok; her test
   gerçek PostgreSQL'de, RLS ve roller kurulu hâlde çalışıyor. RLS'i sahte veritabanında test
   edemezsiniz — asıl korumak istediğiniz şeyi test etmemiş olursunuz.

2. **Bağımsız oracle ilkesi.** Testteki *beklenen değer*, test edilen koddan **türetilmez.**
   "3 gün × 100 TL = 300 TL" sabiti elle yazılır; `BookingMath`'ten hesaplatılmaz. Aksi hâlde test,
   kodun kendi hatasını onaylar.

3. **Para tutan her değişiklikte zorunlu adversarial inceleme.** Ayrı bir inceleyici, kodu
   *çürütmeye* çalışır: işaret hatası, çok döviz, eşzamanlılık, idempotency, kiracı sızıntısı,
   yetki. Her hipotez canlı veritabanına karşı ampirik olarak denenir. Critical/High/Medium bulgu
   varsa düzeltilmeden birleştirme yok. **Çürütme denemeleri kalıcı regresyon testine dönüştürülür.**

4. **Küçük, tek konulu PR'lar** — her biri kendi CI koşusu ve kendi canlı duman testiyle.

Bu modelin işe yaradığının kanıtı: adversarial incelemeler defalarca **gerçek para hataları**
buldu — çift kapatılabilen borç kalemleri, kilit dışında kalan bir kontrol yüzünden ikiye katlanan
dönem tahsilatı, tarayıcının sessizce boşalttığı bir alan yüzünden her kayıtta silinen KDV
varsayılanı. Hiçbiri normal testlerle yakalanmazdı.

---

## 9. Kapsanan iş akışı

```
Araç alımı / sipariş
      ↓
Filoya giriş  →  Sigorta · MTV · Muayene · Kredi taksitleri · Servis/bakım
      ↓
Müsaitlik sorgu  →  Teklif  →  Rezervasyon  →  KİRA SÖZLEŞMESİ
                                                     ↓
                                            Çıkış (km, yakıt, hasar)
                                                     ↓
                                            Dönüş (fark hesapları)
                                                     ↓
                                     Fatura (KDV) → Tahsilat → DEFTER
                                                     ↓
                                     Ceza · HGS yansıtma · Hasar dosyası
      ↓
Araç satışı  →  kâr/zarar kapanışı  →  Araç Karnesi (ömür boyu P&L)
```

**Yan sistemler:** kasa/banka/virman, gider yönetimi, cari hesaplar ve ekstre, depozito,
dış hizmet alımı (B2B), dönemsel faturalama, personel/vardiya, CRM ve hukuk dosyası,
anket, şikayet, asistans talebi, filo planlama.

**Raporlar:** kasa/banka defteri, gelir-gider, cari bakiye ve yaşlandırma, filo doluluk,
kârlılık (araç/grup/şube/segment), **Araç Karnesi** (araç başına ömür boyu P&L, doluluk, RevPACD,
ADR, km maliyeti, ROI, geri ödeme süresi, TCO, amortisman), Filo Analiz, Tut/Sat sinyali,
servis maliyeti, vade panosu, karşılaştırmalı durum analizi.

---

## 10. Öne çıkan iki tasarım parçası

### Kira mega-formu

Kaynak sistemdeki `Kiralama.aspx` ekranının eşdeğeri: **tek sayfada** 8 ana sekme, birinin altında
8 alt sekme, artı sağda yapışkan bir finans paneli (5 alt sekme). Bir kiralama sözleşmesinin
bütün alanları burada.

Statik SSR ile bunu kurmanın yolu: **tek bir ana form** bütün panelleri sarar (gizli paneller
`hidden` ile saklanır ama submit'e dahil olur); ikincil işlemler ana formun *dışında* durur ve
kontroller `form="..."` niteliğiyle bağlanır (iç içe form HTML'de yasaktır). Sekme geçişi, alan
aynalama ve arama küçük bir JS dosyasında; **hesaplama sunucuda.**

### Fiyat motoru

Bir kiralamanın fiyatı tek bir çarpmadan gelmiyor. Zincir: tarife matrisi → gün kademesi
(günlük / 7 gün / haftalık 8-29 / aylık 30+) → müşteri segment indirimi → kanal (acente/kaynak)
eşleşmesi → kampanya kodu (kodlu kural otomatik seçime **sızmaz**) → promosyon → doluluk çarpanı
(tavanlı) → genç sürücü / ek sürücü / drop ücretleri (sistem satırları olarak) → KDV zinciri.

Motor **saf**tır ve çıktısı ekrandaki önizlemeyle **birebir aynı** koddan gelir — önizleme ile
kayıt arasında fark oluşması yapısal olarak imkânsız.

---

## 11. Yerelde çalıştırma

**Önkoşul:** .NET 10 SDK, PostgreSQL 15+ (`brew services start postgresql@15`)

```bash
# Roller + veritabanı (tek seferlik, idempotent)
psql -d postgres -f scripts/db-init-roles.sql
#   racar_owner / racar_owner_pw   → migration
#   racar_app   / racar_app_pw     → uygulama (NOBYPASSRLS)
#   veritabanı: racar

# Çalıştır — migration ve seed açılışta otomatik uygulanır
ASPNETCORE_URLS=http://localhost:5220 dotnet run --project src/RentACar.Web
```

**Giriş** (`http://localhost:5220/login`) — iki aşamalı: önce firma, sonra kullanıcı.

| Firma | Kullanıcı | Şifre | Rol |
|---|---|---|---|
| `yucerent` | `umit` | `***REMOVED***` | Admin |
| `yucerent` | `operator` | `***REMOVED***` | Operatör |
| `demo` | `umit` | `***REMOVED***` | Admin |

**Testler:**

```bash
RACAR_TEST_PG_ADMIN="Host=localhost;Port=5432;Username=burak;Database=postgres" \
  dotnet test RentACar.slnx -c Debug
```

`PostgresFixture` her test sınıfı için gerçek bir veritabanını rolleriyle birlikte kurar.

---

## 12. Yeni bir tablo eklemenin reçetesi

Bu sıra **atlanmamalı** — özellikle 3. ve 4. adım, EF'in üretmediği ve unutulduğunda sessizce
güvenlik açığı bırakan kısımlardır.

1. `Domain/Entities/X.cs` — `ITenantOwned, IAuditable`
2. `AppDbContext`'e `DbSet<X>` + `Persistence/Configurations/` altına yapılandırma
   (**`HasQueryFilter` YAZMA** — merkezî döngü hallediyor)
3. `dotnet ef migrations add AddX`
4. **Migration'a RLS bloğunu ELLE ekle** — EF üretmez: `ENABLE` + `FORCE ROW LEVEL SECURITY` +
   `tenant_isolation` politikası + `GRANT … TO racar_app`. Mali belgeyse ayrıca değişmezlik
   trigger'ı.
5. Repository arayüzü (Application) + gerçeklemesi (Infrastructure)
6. Servis (doğrulama + yetki guard'ı + iş kuralı)
7. DI kaydı (iki tarafta)
8. Web: Razor sayfası + endpoint + `Program.cs` + navigasyon
9. Test: CRUD + benzersizlik + **kiracı izolasyonu (`racar_app` ile)** + yetki

---

## 13. Pahalıya öğrenilmiş dersler

Bunlar teorik değil; her biri gerçek bir hatadan geliyor ve kod tabanında karşılığı olan bir
korumaya dönüştü.

| Ders | Ne olmuştu |
|---|---|
| **Yorumdaki hafifletme bayatlar** | "Bu form o alanı göndermiyor, güvenli" notu zamanla geçersizleşti; rezervasyon düzenlemesi her kayıtta **%20 zam** yapmaya başladı. Güvence yorumda değil, kodda guard olarak durur. |
| **Form gidiş-dönüşü test edilir** | Türkçe yerelde ondalık `1250,5000` basılınca tarayıcı sayı alanını boşaltıyor, sonraki kayıt değeri **NULL'luyor**. Formu değiştirmeden iki kez kaydedip hiçbir alanın kaymadığı doğrulanmalı. |
| **Migration varsayılanı anlam yükler** | Yeni "Kalan" kolonuna EF'in verdiği `0`, mevcut bütün poliçelere **"ödendi"** dedirtiyordu. |
| **Bakiye çiti çift kapatmayı engellemez** | "Seçim borcu aşamaz" kuralı yalnız tek borç varken tutuyor. 100+900 borçta aynı kalem tekrar tekrar kapatılıp **alınmamış tahsilat** yazılabiliyordu. Çözüm: kalıcı tahsis kaydı. |
| **Kontrol kilidin arkasında olmalı** | Bir kontrol advisory kilidin dışında kaldığı için eşzamanlı iki istek birlikte geçti ve dönem tahsilatı **çift sayıldı**. |
| **Yeni durum eklerken eski sorguları tara** | "İptal değilse bakımda say" mantığıyla yazılmış sorgular, yeni "Rezerve" durumu eklenince gerçekleşmemiş randevuları **aracı günlerce bakımda** gösterecekti. |
| **Yerel yeşil ≠ CI yeşil** | PostgreSQL `timestamptz` mikrosaniye, .NET tick 100 ns. Mac'te tesadüfen hizalı olan tarih eşitliği Linux CI'da patlıyor. Test tarihleri tam saniyeye hizalanır. |
| **Bir alanı bağlarken tüm yazan formları tara** | Biri atlanırsa o form kaydettiğinde alan sessizce sıfırlanır. |
| **Açılış uyarılarını say** | "200 OK + testler yeşil" yetmez. Açılıştaki 29 uyarı, 29 çözülemeyen şifreli kayıt demekti. |

---

## 14. Bilinçli olarak yapılmayanlar

| Konu | Durum |
|---|---|
| e-Fatura / GİB entegrasyonu | **Stub.** Port hazır; kimlik bilgisi gerekiyor. |
| SMS, gerçek HGS, banka/POS | **Stub.** Aynı gerekçe. |
| XML acente/broker entegrasyonu | **Stub.** Aynı gerekçe. |
| Kaynak sistemle kuruş kalibrasyonu | Fiyat motoru oranlarının canlı sistemle birebir doğrulanması yapılmadı. |
| Kasa/banka negatif bakiye engeli | **Kasıtlı yok.** Negatif bakiye bir hata değil, gerçek bir durum; yetersiz bakiye guard'ı bilinçli olarak konmadı. |
| Çok taraflı bakiye (müşteri/firma/acente) | **Eklenmeyecek.** Kira bedeli üç tarafa bölüşülmüyor; kiralayan öder, acente komisyonu ayrıca muhasebeleşir. |

---

## 15. Nereye bakmalı

| Soru | Dosya |
|---|---|
| Günlük çalışma kuralları, kilitli kararlar | `CLAUDE.md` |
| Yol haritası, faz sırası (Angular geçişi) | `docs/roadmap/README.md` |
| Verilmiş kararlar, bilinçli "yapılmaz"lar, açık işler | `docs/KARARLAR.md` |
| Kaynak sistemle ekran karşılaştırması | `docs/parite/YONETICI-OZETI.md` |
| Yedekleme / operasyon | `docs/ops/` |
| Defterin kalbi | `src/RentACar.Application/Finance/CashService.cs` |
| Fiyat motoru | `src/RentACar.Application/Pricing/RentalQuoteEngine.cs` |
| Kiracı izolasyonu | `src/RentACar.Infrastructure/Persistence/AppDbContext.cs` |
