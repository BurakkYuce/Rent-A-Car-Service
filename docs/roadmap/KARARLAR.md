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
