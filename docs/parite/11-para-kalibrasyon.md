# Para Kalibrasyonu — canlının formülleri, kaynağından

**Tarih:** 2026-08-06 · **Yöntem:** salt-okuma. Canlı sisteme tek bir kayıt açılmadı, tek bir POST
gönderilmedi.

## Neden formül okuma, neden kayıt karşılaştırması değil

Planlanan yöntem "canlıdaki kapanmış sözleşmelerin girdi+çıktısını oku, aynı girdiyi bizde çalıştır,
kuruş farkını tablola" idi. **Ölçüm bunu çürüttü:** canlı `yucerent` hesabında 26 araç, 25 müşteri,
99 ceza, 999 kredi-takip satırı var ama **`kira_listesi` tek satır ve tutarları 0,00**. Fiyat sorgu
ekranı (`musait_arac_listesi.aspx`) da querystring parametrelerini yok sayıp bugünün tarihine
düşüyor; parametre geçirmek postback ister, o da yazma sınıfına girdiği için yapılmadı.

Yerine **daha güçlü bir oracle** çıktı: canlının hesap mantığı istemci JS'inde açıkça duruyor.
14 ekranda 26 para/gün fonksiyonu bulundu. Aşağıdakiler kaynağından okundu.

---

## 1. Gün sayısı — FARK BULUNDU, DÜZELTİLDİ

**Canlı** (`kiralama.aspx` → `Hizmet_Gun_Bul`, 19 çağrı):

```js
fark_gun  = Math.floor(hours / 24);   if (fark_gun < 1) fark_gun = 1;
Fark_Saat = calculateTimeDifference(...);        // bitH + bitM/60 − basH − basM/60  → DAKİKA duyarlı
if (Fark_Saat >= Saat_Farki_Hesap) fark_gun += 1;   // canlıda Saat_Farki_Hesap = 3
```

> **Tuzak:** aynı sayfada bir de `Gun_Hesapla` var (yalnız 2 çağrı) ve o **dakikaları atıyor**
> (`parseInt(saat.split(':')[0])`). Eski sürüm. Ona bakıp karar veren yanılır — bu koşuda bir kez
> yanıldık, `Hizmet_Gun_Bul`'un asıl fonksiyon olduğu çağrı sayısıyla doğrulandı.

**Bizde:** eşik `2.9` idi. Kodun yorumu bunu "3 + ~0.1sa grace" diye açıklıyordu; o `+0.1` gerçekten
var **ama yalnız aynı-gün dalında**, ve o dal sonucu zaten 1 güne sabitliyor.

**Sonuç:** 2.9'un tek pratik etkisi, kalan süre **2sa54dk–2sa59dk** aralığındayken (6 dakikalık
pencere) **müşteriye bir gün fazla faturalamaktı**.

**Durum:** ✅ Düzeltildi (PR #141) — eşik `3.0`, 10 test, beklenen değerler canlının kodundan.

## 2. Fatura toplamı — UYUMLU

**Canlı** (`fatura.aspx` → `Genel_Toplam_Islem`): grid satırlarının **kendi** `Tutar` / `Kdv` /
`Genel_Toplam` alanlarını topluyor; toplam üzerinden yeniden KDV hesaplamıyor.

**Bizde:** satır bazlı yuvarlama zaten kural. **Fark yok.**

> Planda "bunu ancak karma oranlı fatura örneği ayırt eder" demiştik; kaynak okuması örneğe gerek
> bırakmadı.

## 3. ÖTV — BİZDE YOK

**Canlı:** ÖTV KDV'si **sabit %20** gömülü (`Otv_tutar_Hesap * 0.2`), tenant KDV oranından bağımsız.
ÖTV alanları **3 ondalık** biçimli (`formatMoney(3, …)`), diğer tutarlar 2 ondalık.

**Bizde:** `Otv` domain'de var ama fatura formunda hiç sorulmuyor → karşılıksız.
**Durum:** açık iş (bkz. yol haritası, D5).

## 4. Çok-dövizli tahsilat — YAPISAL OLARAK UYUMLU

**Canlı** (`Hesapla_Form`, 8 ekranda ortak):

```js
Cari_Tutar     = (Kasa_Tutar   * Kasa_Kur) / Cari_Kur;
Onerilen_Tutar = (Islem_Bakiye * Cari_Kur) / Kasa_Kur;
formatMoney(2, ',', '.')
```

Yani **baz-para pivotu** üzerinden çapraz çevrim, 2 ondalık.

**Bizde:** `Money(Amount, Currency, Rate)` + `AmountInBase = Amount * Rate` — aynı baz-pivot mantığı.
**Yapısal fark yok.** (Kuruş düzeyi doğrulama, canlıda dövizli tahsilat kaydı olmadığı için
yapılamadı; model uyumlu olduğu için düşük riskli sayıldı.)

## 5. Tarife km kademesi — BİZDE YOK

**Canlı** (`tarifeler.aspx`): gün kademesi alanları `Gun1..Gun6`'nın **yanında** her kademe için
`Km1..Km6` (km limiti) **ve** `Km1_Ucret..Km6_Ucret` (o kademenin km-aşım ücreti) var.

**Bizde:** km limiti araç grubunda **tek ve global** (`VehicleGroup.GunlukKmLimiti` / `AsimKmUcreti`);
hiçbir tarife varlığında kademe-başına km yok.

> **İki kez düzeltildi — ikisi de kayda değer:**
> 1. İlk hipotez "canlıda kademe sınırları kullanıcı tanımlı, bizde sabit" idi → **yanlış**:
>    `RateCard.MinGun/MaxGun` zaten esnek. Gerçek fark km boyutunda.
> 2. Sonra bu maddeyi `RateCard` üzerinden yazdık → **yanlış hedef**: `RateCard` **DEPRECATED**
>    (`PricingService.cs:15,19` — "yeni tarifeler RateMatrix'e", yalnız geriye-uyum fallback'i).
>    Birincil motor `RentalQuoteEngine` → **`RateMatrix`**, ve `RateMatrix`'te km alanı **hiç yok**.
>    Km kademesi işi `RateMatrix`'e yapılmalı; deprecated varlığa alan eklemek ölü kod üretirdi.

**Sonuç:** "1-3 gün 200 km/gün, 4-10 gün 300 km/gün" gibi kademeye bağlı km politikası bizde
kurulamıyor. **Durum:** açık iş — `docs/roadmap/FAZ-71-*` (hedef: `RateMatrix`).

---

## Kalibrasyonun sınırları — dürüstlük notu

- **Kuruş düzeyinde uçtan uca doğrulama YAPILAMADI**: canlı hesapta karşılaştırılacak kapanmış
  sözleşme/fatura yok. Yukarıdakiler **formül düzeyinde** kalibrasyondur.
- Kalibre edilen: gün sayısı, fatura toplama yöntemi, ÖTV kuralı, döviz çevrim yönü, km kademesi.
- **Kalibre edilemeyen** (canlıda veri ya da erişilebilir hesap yolu yok): iskonto/kampanya
  bileşimi, uzatma bedeli (`Uzatma_Bedel_Hesapla` sunucuya AJAX ile gidiyor — mantık sunucuda),
  ceza/HGS yansıtma oranları, komisyon hesabı.
- Bunlar için canlıda gerçek kayıt oluştuğunda ya da kullanıcı örnek bir sözleşme/fatura ekranının
  çıktısını paylaştığında ölçüm tekrarlanmalı.

## Kalıcılık

Gün kalibrasyonu `tests/RentACar.IntegrationTests/CanliGunKalibrasyonTests.cs` içinde **kalıcı test**
olarak duruyor: beklenen değerler canlının kodundan türetildi, bizim implementasyonumuzdan değil
(CLAUDE.md §3 bağımsız oracle). Canlı eşiği değişirse test kırmızıya döner ve kalibrasyonun
yenilenmesi gerektiğini söyler. **Rapor bayatlar, test bayatlamaz.**
