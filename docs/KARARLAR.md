# KARARLAR — kalıcı kullanıcı kararları, bilinçli "yapılmaz"lar ve açık işler

> **Kaynak:** Bu dosya, eski parite roadmap'inin `KARARLAR` ve `YAPILMAZ` belgelerinin birleşimidir.
> 68 fazlık parite roadmap'i tamamlandıktan (PR #217) sonra, 2026-09-21'de buraya taşındılar. Eski faz
> dosyaları git geçmişindedir (`git log -- docs/roadmap/`). Yeni roadmap (Angular geçişi) `docs/roadmap/`
> altındadır.
>
> Kaynak koddaki "KİLİTLİ KARAR (docs/KARARLAR.md …)" notları buraya bakar. Her karar kodda ilgili
> yerde gerekçesiyle tekrarlanır; **tek doğruluk kaynağı burasıdır.**
>
> Karar tarihi: 2026-08-08 · Karar mercii: kullanıcı (oturum içi soru-cevap).

---

## Açık işler (2026-09-21 itibarıyla)

Tamamlanan 68 fazdan geriye kalan, bilinçli olarak açık bırakılmış maddeler. Hiçbiri Angular geçişini
bloklamaz; her biri ayrı iş olarak ele alınır.

| Madde | Durum | Açılırsa |
|---|---|---|
| **FAZ-74** — rotatif kredi faiz/amortisman formülü | Güvenli red: "Rotatif" seçimi anlaşılır mesajla reddediliyor (`MaliyetHesapService`) | Formül kullanıcıdan gelince küçük takip PR'ı |
| **FAZ-24** — "Aşağıya Yansıt" düğmesinin para anlamı | Buton var; yalnız kendi tablosu içinde oranları diğer aktif kaynaklara kopyalıyor. Komisyon hesabına ne zaman/nasıl gireceği kullanıcıya hiç sorulmadı | Önce kullanıcıya soru; para mantığı bağlanırsa zorunlu adversarial |
| **FAZ-50** — eski defter satırlarını hesaba atama | (a) null-toleranslı okuma uygulandı: legacy satırlar "hesap belirtilmemiş" kovasında. (b) "Varsayılan Kasa/Banka"ya backfill yapılmadı | (b) istenirse ayrı backfill migration'ı; o hesabın raporlanan bakiyesi aniden yükselir, kullanıcı onayı şart |
| **FAZ-51** — ÖTV / tevkifat / damga vergisinin tam defter paritesi | Karar: yalnız faturada kalır (aşağıda) | Tam defter paritesi istenirse ayrı para fazı, zorunlu adversarial |
| **FAZ-47/48** — çok taraflı bakiye (müşteri / firma / rez. kaynağı) | Karar: eklenmez (aşağıda) | İhtiyaç doğarsa yeni para fazı |
| **e-Fatura hayalet gönderim (F1.4 LOW-2)** | Dönem/kira/fark faturası yarışını kaybeden istek `eInvoice.SendAsync`'i mevcut id'yi dönmeden önce çağırıyor (`InvoiceService.cs:~353`). Bugün stub `false` → etkisiz | Gerçek GİB entegrasyonu açılmadan ÖNCE düzeltilmeli (gönderim yalnız kazanan transaction'dan) |
| **Kimlik bekleyen entegrasyonlar** | e-Fatura/GİB XML aktarımı, XML broker/acente, gerçek HGS, banka/POS. Stub ve portlar hazır; iyzico adaptörü var ama para yoluna bağlı değil. SMS ve e-posta 2026-08-17'de gerçek | Açmadan önce kullanıcıya sorulur; credential sohbete yazılmaz |

---

## GENEL POLİTİKA — yeni tutar alanları deftere yazmaz

**Karar:** Kalan fazlarda eklenen tutar/oran alanları (Hukuk dosyası tahsilat/kalan, Sigorta zeyil
primi, Araç kredisi özet kartları, Ayarlar'daki fiyat/muhasebe parametreleri, Rezervasyon kaynağı
komisyon/önödeme/indirim/puan oranları) **BİLGİ ALANIDIR — muhasebe defterine YAZMAZ.**

**Neden:** Gerçek para hareketi Kasa/Banka tahsilat-ödeme akışından geçer. İki yolu birden açmak
çift-sayım üretir; mevcut tasarım da zaten böyle (Hukuk entity'si "postlamaz" diye yazılmış).

**Nasıl uygulanır:** Her böyle alan için (a) entity/servis XML notunda "deftere girmez" gerekçesi,
(b) ekranda kullanıcıya yazılı uyarı, (c) **kırılgan regresyon testi** — alan uçuk bir değerle
doldurulduğunda ilgili rapor/hesap sayılarının değişmediği doğrulanır. FAZ-24 ve FAZ-10'da bu desen
uygulandı, örnek olarak alınabilir.

---

## FAZ-50 — Hesap-bazlı Kasa/Banka defteri: geçmiş kayıtlar

**Karar:** **Null-toleranslı okuma.** Geçmiş defter kayıtlarına DOKUNULMAZ; hangi
`FinancialAccount`'tan olduğu bilinmediği için raporlarda "hesap belirtilmemiş" kovasında görünürler.

**Neden:** Geçmişte hangi kasadan/hangi bankadan olduğunu bilmiyoruz — backfill, mali kayda tahmin
yazmak olurdu ve `rc_prevent_mutation` değişmezlik trigger'ını gevşetmeyi gerektirirdi.

**Sonuç:** FAZ-50 bu seçenekle açılır; FAZ-56/57/58/59/64 bağımlılığı çözülür. Eski kayıtları elle
doğru hesaba atama ekranı **kapsam dışı** (istenirse ayrı iş).

---

## FAZ-71 — Tarife km kademesi

**Karar 1:** Km değeri **GÜNLÜK limittir**, `limit × gün` uygulanır.
Kullanıcının tarifi: *"günlük 200 girdin ve 5 günlük kiralama → müşteri 1000 km katedene kadar ek km
çıkmaz."* Mevcut `VehicleGroup.GunlukKmLimiti` ile aynı dil.

**Karar 2:** 7 gün / haftalık (8-29) / aylık (30+) kademeleri için **ayrı alanlar eklenir**
(`KmHaftalik`, `KmAylik`) — Km6'ya düşürme varsayımı KULLANILMAZ.

---

## FAZ-56 — Bakiye Düzeltme: karşı hesap

**Karar:** **Yeni bir "Muhasebe Düzeltmesi" hesap türü** açılır (`LedgerAccountType` genişlemesi +
migration).

**Neden:** Düzeltmeler gerçek gelir/giderle KARIŞMAMALI. Gelir/Gider hesabına yazmak Araç Karnesi,
Karlılık ve Filo Analiz raporlarını şişirirdi — daha önce tam bu sınıf bir atıf hatası düzeltilmişti
(CLAUDE.md §6 "Araç ön muhasebe").

**Not:** Raporlarda bu tür AYRI satırda gösterilir, gelir/gider toplamına karışmaz.

---

## FAZ-30 — Otomatik tahsilat elle tetikleme

**Karar:** Elle tetikleme, Ayarlar'daki `DonemselOtomatikTahsilat` anahtarından **bağımsız** çalışır.

**Neden:** O anahtar "her gece kendiliğinden çalışsın mı" sorusunun cevabıdır; kullanıcı ekranda
sözleşmeyi seçip açıkça tıkladığında niyet nettir.

**Nasıl:** Ekranda "otomatik job kapalı" bilgi uyarısı gösterilir (kullanıcı ikisini karıştırmasın).

---

## FAZ-49 — Rezervasyon kaynağı komisyon/oran alanları

**Karar:** Oran alanları (komisyon, önödeme, indirim, puan) **bilgi alanı olarak eklenir** — genel
politikaya uygun. İş kuralı bayrakları (uzatma yasağı, tarih değiştirilemezlik, km sınırsızlığı)
GERÇEKTEN uygulanır; oranlar hiçbir hesaba girmez.

---

## FAZ-29 — Tek cari toplu kapatma

**Karar 1 (bakiye çiti):** Seçim carinin güncel borcunu aşamaz; avans/fazla tahsilat bu ekrandan
yapılamaz (Kasa ekranı kullanılır).

**Karar 2 (KALEM-BAZLI TAHSİS — adversarial H1 sonrası):** "Hangi tahsilat hangi borç kalemini ne
kadar kapattı" bilgisi **kalıcı bir tahsis tablosunda** tutulur. Kapanan kalem ekranda kapalı görünür
ve yeniden seçilemez.

> **DÜZELTME NOTU:** Önce "bakiye çiti çift kapatmayı engeller" denmişti; **bu YANLIŞTI**. Adversarial
> inceleme ampirik çürüttü: çit yalnız carinin TEK borcu varken tutuyor. 100 + 900 = 1000 borçta
> 100'lük kalem kapatılıp (bakiye 900) aynı kalem yeniden seçilince 100 ≤ 900 olduğu için geçiyor ve
> alınmamış tahsilat yazılıyordu. Bakiye çiti tek başına YETERSİZDİR; tahsis kaydı şarttır.

**Karar 3 (kısmi kapatma):** Bir kalemin yalnız bir bölümü kapatılabilir; kalan açık kalır ve
listede "400/1000 kapalı" olarak görünür.

**Karar 4 (kira bağı):** Kapatılan kalem bir kira faturasından geliyorsa tahsilat o kiraya bağlanır —
kira bakiyesi ve tahsilat-mutabakat raporu cari ekstresiyle tutarlı kalır.

**Ayrıca kapatılacak adversarial bulgular:** H2 (bakiye kontrolü + kayıt aynı transaction'da,
`(tenant, cari)` danışma kilidi arkasında — depozito deseni), M1/M2 (FK ihlali → temiz red),
M3 (yuvarlanmamış karşılaştırma + aşağı yuvarlama), M4 (plaka çözümlemesi test edilebilir yere).

---

## FAZ-47 + FAZ-48 — Çok-taraflı bakiye (Müşteri / Firma / Rez. Kaynağı)

**Karar:** **EKLENMEZ.** İşte kira bedelinin üç tarafa bölüşülmesi olmuyor; kiralayan öder, acente
komisyonu ayrıca muhasebeleşir.

**Sonuç:** `MstToplam/MstBakiye/FirmaToplam/FirmaBakiye/RezKaynakToplam/RezKaynakBakiye/*Fatura`
kolonları AÇILMAZ; iki ekran (kiralama + rezervasyon) tek-taraf modelinde kalır. İhtiyaç sonradan
doğarsa ayrı bir para fazı olarak açılır (zorunlu adversarial). **Bu karar FAZ-47 ve FAZ-48'in
`‼ RİSK` işaretini kaldırır** — geriye kalan maddeleri düz alan/UI derinliğidir.

---

## FAZ-12 Bölüm C — Araç gelir-gider tablosu birleştirme

**Karar:** **Seçenek B** — Ek Hizmet raporuna ARAÇ bazlı pivot modu eklenir. Mevcut üç rapor
(Karlılık / Filo Analiz / Ek Hizmet) yerinde kalır; kullanıcı alışkanlığı bozulmaz.

---

## FAZ-16 — Servis faturası/ödemesi defter bağı

**Karar:** **Bağlanmaz.** Servis kaydı bilgi olarak zenginleşir; gerçek maliyet Giderler ekranından
girilmeye devam eder (mevcut tasarım). Çift-sayım riski sıfır kalır.

---

## FAZ-79 — "Potansiyel gelir" kolonunun fiyat kaynağı

**Karar:** **Tarife matrisi** (fiyat motorunun kullandığı onaylı `RateMatrix`). Eski `RateCard`
kullanılmaz — yeni tarifeler oraya girilmediği için potansiyel rakam zamanla gerçekten koparaydı.

**Not:** Bu faz, "P&L yalnız defterden" kuralının en sıkı uygulanacağı yerdir; kaynak-varlık
alanının `Gelir`/`Gider`/`NetKar` toplamına eklenmesi **Critical** bulgudur.

---

## FAZ-64 — Gider kısmi ödemesi ve defter

**Karar:** **Gider ilk girişte TAM tutarıyla deftere yazılır**; "ödenen/kalan" yalnız TAKİP
alanıdır. Mevcut defter davranışı değişmez (düşük risk).

**Bedeli (bilinçli):** kasadan çıkış anı defterde ayrı görünmez.

---

## FAZ-60 — Trafik cezasında "Kalan"

**Karar:** **SATIR BAZINDA kalan.** Her ceza satırının kendi ödeneni ve kalanı olur.

> Kullanıcı, önerilen basit modelin (tek kalan) yerine bunu seçti — daha doğru takip, karşılığında
> form/servis karmaşıklığı ve daha geniş test. Zorunlu adversarial incelemede "hangi satırın ne kadar
> ödendiği" ile toplam arasındaki tutarlılık ayrıca sınanmalı.

---

## FAZ-51 — Fatura ÖTV / Tevkifat / Damga

**Karar:** **Fatura üzerinde bilgi kalır**, deftere ayrı satır yazılmaz (genel politika). Defter
bugünkü gibi net + KDV + toplam yazar.

**Not:** Tam parite istenirse ayrı faz — özellikle **tevkifatta yön hatası** (kim kesiyor) klasik
bir para hatası kaynağıdır, zorunlu adversarial ister.

---

## FAZ-84 — Tahsilat kanalı

**Karar:** Kanal bilgisi **tahsilat belgesine kalıcı yazılır** (raporlanabilir olsun). Defter
şemasına DOKUNULMAZ — P&L hâlâ yalnız `AccountLedgerEntry`'den okunur.

---

## Denetim izinde firma IBAN/VKN kısmi maskesi (2026-09-25, #319 L2)

**Karar (kullanıcı):** Denetim izinde (AuditLogs + `/api/ui/v1/denetim` + Blazor denetim ekranı) FİRMANIN KENDİ
banka IBAN'ı (`Hesaplar.Iban`) ve VKN'si (`Ayarlar.FirmaVergiNo`) **son 4 karakteri görünür** yazılır:
`********1234` (boşluklar atılır; önek SABİT 8 yıldız — uzunluk bilgi taşımaz, biçim yeniden maskelemede aynı kalır).
Boşluksuz 8 karakterden kısa değer TAM maske (`***`). Gerekçe: IBAN değişikliği (ödeme yönlendirme dolandırıcılığı)
izde fark edilebilmeli; tamamen `***` iken eski/yeni ayırt edilemiyordu.

**Kapsam dışı (TAM maske sürer):** müşteri/personel PII (TC, VKN, `BankaIban`, ehliyet, pasaport, nüfus cüzdanı,
maaş), sırlar (`*Enc`/`*Hash`/`*Token`, parola, API anahtarı), iç içe nesnelerdeki anahtarlar ve tablo adı bilinmeyen
çağrılar. İzin listesi `AuditSecretMask.PartialMaskFields`'ta (tablo + TAM anahtar çifti) — yeni alan eklemek
kullanıcı kararıdır. Bilinen sınır: şahıs firmasında VKN = TCKN olabilir; o durumda TCKN'nin son 4 hanesi görünür
(kullanıcı aynı kuralı VKN'ye bilerek uyguladı).

---

## Karar GEREKMEYEN / kendiliğinden çözülenler

- **FAZ-13, FAZ-15, FAZ-41, FAZ-18, FAZ-82:** genel politika (yeni tutar alanları deftere yazmaz)
  bu fazların `⚠ KARAR` işaretini kaldırır.
- **FAZ-73:** doluluk %50 tavan formülü değişmiyor → PARA kararı gerekmiyor (spec teyidi).
- **FAZ-74:** rotatif kredi faiz/amortisman formülü açık kalır; faz **güvenli-red** ile ilerler,
  kalem-toplama yapısı bu karardan bağımsız kurulur. Formül kararı geldiğinde küçük takip-PR.
- **FAZ-17:** çok katmanlı fiyat yalnız alan/altyapı; "resmi tutar" DEĞİŞMEZ, `AracSiparis`
  "defter postlamaz" ilkesi korunur → genel politika kapsamında.
- **FAZ-46, FAZ-53, FAZ-57, FAZ-58, FAZ-67, FAZ-11:** karar taşımıyor.
- **BLOKE (kimlik/credential gerekir, açmadan önce kullanıcıya sorulur):** e-Fatura/GİB XML aktarımı
  (FAZ-54, FAZ-55'in entegratör kısmı), XML broker/acente entegrasyonu (FAZ-49 D8 listesi),
  SMS/HGS/banka-POS.

---

## ÇALIŞMA DÜZENİ (2026-08-08 kullanıcı kararı)

- **Bloke entegrasyonlar** (e-Fatura/GİB XML, SMS, gerçek HGS, banka/POS, XML broker):
  **stub/port hazırlanır**, gerçek bağlantı credential geldiğinde tek sınıf değişimiyle açılır.
  Ekranlar kimlik beklemeden tamamlanır. **Şifre/credential sohbete YAZILMAZ** — geldiğinde
  konfigürasyona nasıl konacağı ayrıca anlatılır.
- **FAZ-74 rotatif kredi:** güvenli-red. Kalem-toplama yapısı kurulur; "Rotatif" seçilirse
  anlaşılır mesajla reddedilir. Formül gelince küçük takip-PR.
- **Paralellik:** aynı anda 3-4 faz geliştirilir (farklı kollardan), **merge SIRAYLA** yapılır —
  migration içeren her faz `AppDbContextModelSnapshot.cs`'i değiştirdiği için eşzamanlı merge
  snapshot çakışması üretir (bilinen tuzak).
- **PR düzeni:** faz başına ayrı PR, kendi CI'ı ve kendi canlı duman testiyle. Bir şey ters
  giderse tek faz geri alınır.

---

## YAPILMAZ — bilinçli olarak eklenmeyen ekranlar ve gerekçeleri

Parite taraması canlıda olup bizde olmayan her şeyi listeledi. Bunların bir kısmı **eksik değil,
karar**: taklit edilmesi mimari gerileme olurdu ya da karşılığı zaten var. Bu bölüm o kararların
**tam gerekçelerini** tek yerde toplar.

> Gerekçeler ekran-bazlı plan dosyalarından (`docs/parite/plan/*.md`) **aynen** taşındı; kısaltılmadı.
> Bir karar yanlış bulunursa ilgili plan dosyasında tartışılır ve buraya güncel hâli taşınır.

**Toplam: 12 tam + 1 kısmi (alt-parça) karar.**

---

### `hesap_tanimalama.aspx`

**Modül:** Tanım/Master

Alan bazında **tam örtüşme** zaten var (`HesapKodu.Kod/Ad/Aciklama/Aktif` ↔ canlı `Kod_Adı+Açıklama`). KISMİ işareti, canlı ekranın grid kolonu güvenilir çıkarılamadığı için (kolon kaynağı=yok) verilen bir **ölçüm belirsizliği** — gerçek bir fonksiyonel fark tespit edilmedi. Yapılacak bir iş yok; ilerideki bir tarama canlı grid kolonunu doğrularsa yeniden değerlendirilir.

### `tabletyonetim.aspx`

**Modül:** Tanım/Master

Saha tableti / dijital imza toplama DONANIM entegrasyonu (imza-tableti varsayılanları, RTF sözleşme şablon yükleme, aksesuar-fotoğraf şablonları). RentACar bu sürümde bulut-SaaS, saha donanımı filosu yönetmiyor; taklit edilmesi var olmayan bir donanım katmanını simüle eder — mimari gerileme. Kullanıcı ileride fiziksel saha-tablet operasyonu isterse ayrı bir roadmap kararı olarak açılmalı.

### `referans-uzak.aspx`

**Modül:** Tanım/Master

İş kuralı içermeyen, 3. parti uzak-erişim destek aracına (TeamViewer benzeri) yönlendiren indirme sayfası. RentACar iş kapsamı dışı; hiçbir karşılık gerekmiyor.

### `fiyat_kampanya_yonetimi.aspx`

**Modül:** Fiyat/Tarife + Raporlar

Extractor profili düşük güvenli (alan=3, satır=0 — muhtemelen DevExpress callback'i statik HTML çıkarımına yakalanmamış). Gerçek işlevi `kampanya_ara.aspx` (aşağıda) ile örtüşüyor; "Kampanya Yenile" aksiyonu zaten `RentalRuleService`'teki `KampanyaKodu` REPLACE mekanizmasıyla (CLAUDE.md FAZ 3) karşılanıyor. Ayrı bir ekran/efor açmak yerine kapsam `kampanya_ara.aspx` planına birleştirildi.

### `genel_rapor.aspx`

**Modül:** Fiyat/Tarife + Raporlar

Kullanıcının kendi alan/pivot tanımlayabildiği genel bir rapor-oluşturucu (custom report builder — "Alan Ekle/Düzenle", pivot tablo `DataTableJson`, kayıtlı rapor, "Tümünü Sil/Aktar"). Bizim mimarimiz sabit-şema tenant-owned tablolar + sabit rapor sayfaları üzerine kurulu (CLAUDE.md §2 "temiz mimari, katmanlı"); kullanıcının serbestçe alan/pivot tanımlayabildiği bir BI-motoru inşa etmek kendi başına haftalar sürecek ayrı bir kategori (D6/D7'den daha büyük), ROI düşük — repo zaten 20+ sabit rapor sunuyor. Taklit edilmesi mimari gerileme olur (talimat madde 6 örneğiyle birebir örtüşen durum). Özel bir kırılım isteği gelirse mevcut raporlardan birine (örn. Karlılık) yeni boyut eklemek (D4) yeterli.

### `cikis.aspx — Object moved`

**Modül:** Sistem

Zaten fonksiyonel karşılığı **kodda doğrulandı**: `POST /auth/logout` (`src/RentACar.Web/Identity/AuthEndpoints.cs:45-49`) cookie sign-out yapıp `/login`'e yönlendiriyor; çağrı noktası `Components/Layout/MainLayout.razor:255-257`. Canlı ekran da içerik üretmiyor (302 redirect) — bu ÇIKIŞ ekranının doğası, kıyaslanacak alan/kolon yok. Ek iş gerekmez.

### `default.aspx — referans sistem (ana sayfa BI süiti)`

**Modül:** Sistem

Canlının 24-sekmeli monolitik BI dashboard'unu (Doluluk/Şube Karşılaştırma/Gelir Raporu/vb.) tek bir sayfaya geri toplamak, halihazırda **her biri kendi modülünde ayrı, test edilmiş `/raporlar/*` ekranımız** olan raporları (bu modülün kapsamı dışında, örn. Filo Analiz, Finans Analiz) tekilleştirmek olur — mimari gerileme (modüler-rapor kararının tersine gitmek). `src/RentACar.Web/Components/Pages/Home.razor` zaten filo KPI şeridi (Doluluk/RevPACD/ADR, satır 168), 6-ay gelir trendi (satır 176-182) ve ilgili rapora hızlı-link (`/raporlar/filo-analiz`, `/raporlar/finans-analiz`, `/raporlar/periyodik-servis`) içeriyor — "hangi rapora gidilir" keşfi zaten çözülü. Şube arama kutusu + tarih-aralığı kısayolları gibi tek-tek UI kolaylıkları, ilgili `/raporlar/*` ekranının kendi parite incelemesinde ele alınmalı (bu modülün kapsamı orası değil).

### `fonksiyonlar.aspx — (başlık yok)`

**Modül:** Sistem

Canlı profilde hiçbir kanıt yok (tip=diğer, alan=0, kolon=0) — eyleme dönüştürülebilir somut bir hedef tanımlanamıyor. Kural 6'nın "bilinçli tasarım farkı" değil ama "kanıtsız → iş planına giremez" hâli. Tahmin yapılmadı; gerçek bir gap olup olmadığı ancak canlıya çerezli tarayıcı erişimiyle (bkz. kullanıcı hafızası: canlı çerez erişim yöntemi) yeniden bakılırsa netleşir — bu plan kapsamında iş kalemi ÜRETİLMEDİ.

### `globalsearch.aspx — (başlık yok)`

**Modül:** Sistem

Kodda **iki ayrı** karşılığı doğrulandı: (1) `MainLayout.razor:18-19` sol menü üstünde `GET /ara` arama kutusu ("Evrak No / Plaka ara…"), (2) `MainLayout.razor:241-246` topbar'da "Ctrl+K" kısayol ipuçlu hızlı-arama kutusu (aynı `/ara`'ya post eder). Sayfa karşılığı `src/RentACar.Web/Components/Pages/Search/Ara.razor` (plaka/cari/kira/rez/fatura no arar). Canlının header'daki AJAX arama kutusu kavramsal ve konumsal olarak **tam örtüşüyor** — parite doğrulandı, "❓ DOĞRULANAMADI" belirsizliği bu incelemeyle kapandı, iş kalemi yok.

### `kullanicilar.aspx — Kullanıcılar`

**Modül:** Sistem

Canlının hacmini oluşturan **kullanıcı-bazlı ~100 menü/izin checkbox'ı** (Dash_Doluluk, Tanimlar, Arac_Tanimla, Yeni_Kiralama, Nakit_Islemler, Web_Yoneticisi vb., her menü öğesi için ayrı toggle + "Yetki Kopyalama") bizde **bilinçli olarak** rol+yetki-matrisi modeliyle karşılanıyor (CLAUDE.md §4: `UserRole`/`Permission`/`RolePermissions` + `/yetki` ekran-bazlı override — `src/RentACar.Web/Components/Pages/Authorization/Yetki.razor`, rol-üstü sıkılaştırma, deny-by-default). Kullanıcı-bazlı yüzlerce checkbox'ı taklit etmek bu matrisin üstüne İKİNCİ, çelişebilecek bir yetki kaynağı eklemek olur — mimari gerileme. `src/RentACar.Web/Components/Pages/Users/UserList.razor` zaten CRUD+parola-sıfırlama+şube atama yapıyor, doğru örtüşüyor. Not: "Yetki Kopyalama" (rol+şube'yi bir kullanıcıdan diğerine kopyalama kısayolu) küçük bir UX kolaylığı olarak `/yetki` veya `/kullanicilar`'a ayrıca D2 eklenebilir ama bu, checkbox-modeli kararından bağımsız, isteğe bağlı bir iş — bu planda zorunlu kalem olarak YAZILMADI.

### `mobil_teslimat.aspx — Mobil Teslimatlar`

**Modül:** Sistem

Canlıdaki `Rac_Tablet_Say` (tablet sayacı) alanı doğruluyor: bu, referans sistem'in **ayrı bir native/tablet saha uygulaması** ekosistemine ait bir oturum-log tablosu (Kayıt No, Çıkış Zamanı, Teslim Zamanı, Çıkış Ofisi). Bizim mimarimiz TEK platform — responsive Blazor Server statik-SSR — tüm cihazlardan (masaüstü/tablet/telefon) AYNI kira mega-formunu (`/kiralar/{id}`) kullanır; teslim/dönüş zaman-damgaları ve çıkış ofisi zaten `RentalContract` üzerinde tutuluyor (Kira mega-form deseni, CLAUDE.md §6). Ayrı bir "tablet oturumu" tablosu eklemek, aynı gerçeği iki kaynaktan (RentalContract + ayrı oturum log'u) taşımak anlamına gelir — senkronizasyon riski yaratan bilinçli bir mimari gerileme olur. Gerçek bir native saha-app kararı gelirse (kullanıcı talebi), o zaman D7 (yeni dikey) olarak yeniden değerlendirilir.

### `web_site_yonetimi.aspx — Web Sayfanızın Yönetimi`

**Modül:** Sistem

PLAN-TALIMATI'nın kendi örneği: canlı **çok-dilli (5 dil) tam tema/CMS/SEO/menü yönetim paneli** (Web_Language_*, Web_Slider_*, Web_WhyWe_*, Web_Comment_* testimonial, Web_Ayar_Color_* tema, Web_Menu_* 12-tip menü-builder+301 yönlendirme, Web_Translate_*, sayfa-bazlı Web_Seo_*). Bizim halka-açık site modülü (`/web-sitesi` `src/RentACar.Web/Components/Pages/WebSite/WebSiteHub.razor` + `/site-icerik` `SiteIcerikYonetim.razor` + `/blog-yonetim`) **PR-0..9'da bilinçli olarak** araç-vitrini + basit statik-sayfa/SSS + blog'a daraltıldı (bkz. kullanıcı hafızası: "Public site özellik TAMAMLANDI"), tek-dilli. Çok-dilli tema motoru + sürükle-bırak menü-builder + testimonial/ slider yönetimi + sayfa-bazlı çok-dilli SEO'yu şimdi eklemek, kapsamı bilinçli daraltılmış bir modülü canlının **farklı ürün kategorisindeki** (tam CMS) haline geri büyütmek olur — mimari gerileme + orantısız efor. Kod doğrulaması: `WebSiteHub.razor` başlığı "Web Sitesi — İlanlar" (satır 24), yalnız araç ilanı odaklı; modül-kapalıysa `_modul` guard'ı sunucuda da kontrol ediyor (satır 16-20, çift-savunma deseni — `[Authorize]` yalnız rolü kontrol eder, menüyü gizlemek tek başına koruma değildir). Gerçek genişlik farkı kabul edilir, iş planına girmez.

### `para_tanimlama.aspx` — KISMİ karar (yalnız Kur alt-parçası)

**Modül:** Tanım/Master

Bu ekranın **Ülke** alanı yapılacak (D1, FAZ-20 kapsamında). Yapılmayan yalnız **Kur alanı**:

> Kur zaten AYRI ve tek kaynaktan yönetiliyor (`/kurlar`, TCMB otomatik + tenant sabit kur).
> `Currency` entity'sine statik/manuel bir `Kur` alanı eklemek **çift-kaynak** yaratır — iki yerde
> kur olur, hangisinin geçerli olduğu belirsizleşir. Para yolunda bu belirsizlik kabul edilemez.

---

### Bu kararlar nasıl gözden geçirilir

Her biri bir **varsayıma** dayanıyor (ör. "saha tableti kullanmıyoruz", "çoklu dil kapsam dışı").
Varsayım değişirse karar da değişir. Değiştirmek isteyen:

1. İlgili plan dosyasındaki gerekçeyi oku (`docs/parite/plan/`).
2. Gerekçedeki varsayımın hâlâ geçerli olup olmadığını söyle.
3. Geçerli değilse ekran normal akışa girer: iş olarak tanımlanır ve kullanıcının kararıyla
   `docs/roadmap/DEGISIKLIKLER.md` üzerinden roadmap'e eklenir (faz sırası kilitlidir).
