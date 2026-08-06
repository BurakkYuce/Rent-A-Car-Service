# Ekleme Reçeteleri — desen bazlı (D1–D9)

Eksik 48 ekran için 48 ayrı reçete yazmak israf olurdu: onlarca ekran aynı akışı paylaşıyor.
Bu belge **9 deseni** tanımlar; modül dosyalarında her ekrana bir desen kodu + en fazla iki cümlelik
sapma notu iliştirilmiştir.

Ortak temel: **CLAUDE.md §5** (yeni tenant-owned tablo reçetesi). Aşağıdaki desenler ondan sapılan
noktaları anlatır.

---

## D1 — Basit sözlük / master
*Örnek: renk, yakıt türü, vites türü, iptal sebebi, araç sahibi grubu*

CLAUDE.md §5 birebir uygulanır (entity → config → migration + **elle RLS bloğu** → repository →
servis → DI → liste sayfası + endpoint + nav → test). İş kuralı yok, yalnız CRUD + benzersizlik.

**Maliyet:** ~0,5 gün / 5 ekran, tek PR. **Not:** `ITenantCache`'e bağlanabilir (master/referans cache).

## D2 — Kural taşıyan master
*Örnek: araç grubu, tarife tanımı, sigorta ürünü, ayarlar anahtarları*

D1 + iş kuralı + kuralın tüketildiği yere bağlama (fiyat motoru, vade panosu…). Alan eklemek
yetmez; **kuralı okuyan yolun testi** de gerekir.

**Maliyet:** 1–2 gün/ekran.

## D3 — Liste / arama (yeni tablo YOK)
*Örnek: fatura detay listesi, virman geçmişi, HGS geçiş listesi*

Veri zaten var, görünmüyor. Mevcut repository'ye filtre + kolon + sayfalama + export ucu eklenir.
**En ucuz sınıf** — parite kazancı/maliyet oranı en yüksek olan burası.

**Maliyet:** ~0,5 gün/ekran.

## D4 — Rapor (agrega)
*Örnek: KDV alış-satış raporu, gelir tablosu derinliği, boş araç raporu*

`OrtakSorgular`'a sorgu + rapor servisi + `/raporlar/*` sayfası + export + nav.
**Kural:** P&L **yalnız defterden** okunur; kaynak-varlık tutarı asla toplanmaz (çift sayım yasağı).

**Maliyet:** 1 gün/ekran.

## D5 — Para hareketi YAZAN
*Örnek: banka hesapları arası virman, toplu tahsilat varyantları, gelen e-fatura muhasebeleştirme*

Çift-taraflı defter (Σ Borç = Σ Alacak), **idempotency anahtarı** (monoton bileşenli), düzeltme
**ters kayıtla**, DB-immutability trigger'ı. **ZORUNLU adversarial inceleme** — ayrı bir ajan kodu
çürütmeye çalışır (işaret hatası, çok-döviz, çift-sayım, RLS sızıntısı, yetki).

**Maliyet:** 2–4 gün/ekran. **Bu sınıfta commit öncesi Critical/High/Medium bulgu kalamaz.**

## D6 — Mega form
*Örnek: rezervasyon derinliği, filo kiralama sözleşmesi*

Kira mega-formunun deseni: tek ana form tüm panelleri sarar (`hidden` attr), ikincil işlemler ana
formun DIŞINDA + `form=` attribute'üyle bağlanır (iç içe form yasak), sekme/ayna JS'i
`rc-kira-tabs.js`, **canlı hesap SUNUCUDAN** (`/kiralar/hesapla`) — UI formül taşımaz.

**Maliyet:** 5–10 gün.

## D7 — Yeni dikey
*Örnek: sözleşme-bağlı anket, dönüş-bağlı şikayet akışı, hukuk takibi derinliği*

Entity kümesi + servis + 3–5 ekran + rapor. Ad benzerliğine aldanmamak kritik: canlının anketi
**sözleşmeye bağlı 8 soruluk** bir akış, bizim `/anketler` jenerik bir geri bildirim kaydı.

**Maliyet:** 3–5 gün/dikey.

## D8 — Entegrasyon bağımlı — **KOD YAZILMAZ**
*Örnek: e-Fatura/GİB, KABİS, e-Devlet ceza sorgusu, XML broker, SMS, POS/sanal pos*

Bu ekranlar kimlik/credential olmadan yazılamaz; port'lar ve stub'lar hazır. **Açmadan önce
kullanıcıya sorulur.** Rapora "bloke" diye girer, parite borcu olarak sayılmaz.

## D9 — Erişilebilirlik düzeltmesi
*Bugün tam 2 ekran: `/raporlar/kasa-banka`, `/raporlar/servis-ozet`*

Kod yazılmış, rotalı ve çalışıyor ama `MainLayout.razor`'daki gruba eklenmemiş → kullanıcı
ulaşamıyor. Tek satır nav + menü testi.

**Maliyet:** ~10 dakika. **Parite tablosunda `⚠ ERİŞİLEMEZ` olarak görünür.**
