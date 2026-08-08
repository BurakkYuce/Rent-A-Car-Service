# KARARLAR — `⚠ KARAR` işaretli fazlar için kullanıcı kararları

> Bu dosya `SIRA.md`'de `⚠ KARAR` ile işaretlenmiş fazların **kilidini açan** kararları tutar.
> Karar burada yazılı değilse faz **kapanmaz**. Her karar, o fazın kodunda ilgili yerde tekrar
> gerekçesiyle not edilir; burası tek doğruluk kaynağıdır.
>
> Tarih: 2026-08-08 · Karar mercii: kullanıcı (oturum içi soru-cevap).

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
