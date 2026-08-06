# FAZ-29 — Toplu Gider + Toplu Tahsilat (Tek-Cari Modu)

| | |
|---|---|
| **Desen** | D5 — defter yazıyor |
| **Efor** | 1,5 gün (yapısal); tutar/KDV/idempotency doğruluğu **PARA — Opus'a devredilir** |
| **Bağımlılık** | yok |
| **Kapsanan canlı ekran** | `toplu_gider.aspx`, `toplu_tahsilat.aspx` |
| **Risk** | yüksek (para/model) — defter yazan iki formun genişletilmesi |
| **Zorunlu** | adversarial inceleme — Critical/High/Medium bulgu kalmadan commit yok. |

## Amaç
Toplu Gider satır formatına cari/vade/hesap bağlama eklenip, Toplu Tahsilat'a canlının "bir cari, çok
açık kalem seç" modelini KORUYARAK yanına yeni bir "tek cari, ekstresinden seç, toplu kapat" modu
eklenerek kullanıcı hem çoklu-cari (mevcut) hem tekil-cari (yeni) toplu işlem yapabilir hale gelir.

## Neden (kanıt)
- `src/RentACar.Web/Components/Pages/Finance/TopluGider.razor` satır formatı şu an
  `netTutar;açıklama` (satır 26-29 doğrulandı) — Cari Bilgisi, Vade, Hesap No (hangi
  `FinancialAccount`'tan ödeneceği) yok; "Plaka Ekle" yok.
- `src/RentACar.Web/Components/Pages/Finance/TopluTahsilat.razor` satır formatı
  `cariId;tutar;açıklama` (satır 26-28 doğrulandı) — ÇOK-CARİ modeli zaten var ve KORUNACAK.
  `src/RentACar.Application/Finance/CashService.cs`'te `GetStatementAsync(cariId)` (satır 40) **zaten
  var** — "Carinin İşlem Listesini Getir" için bu metod kullanılabilir (yeni sorgu yazmaya gerek
  yok, mevcut ekstre metodunu tüketicidir).

## Yapılacaklar
1. `src/RentACar.Web/Components/Pages/Finance/TopluGider.razor` — satır formatını
   `netTutar;açıklama;plaka(ops)` şeklinde genişlet (birden çok araca aynı gider dağıtımı — her
   plaka için AYRI satır, tekil gider paylaştırma DEĞİL); forma Cari Bilgisi (opsiyonel, ComboBox —
   `CustomerService.ListAsync`), Vade (tarih), Hesap No (`FinancialAccountService.ListActiveAsync`
   dropdown) alanları ekle.
2. `src/RentACar.Application/Expenses/ExpenseService.cs` — toplu gider giriş noktasına
   cariId/vade/hesapId/plaka parametrelerini ekle; her satır için `Expense` kaydına bu alanları
   map et; plaka verilen satırlarda ilgili `Vehicle`'a `AccountRef` atanarak deftere yazılsın
   (mevcut atıf zincirine uygun — araç ön muhasebe düzeltmesiyle aynı desen).
3. Yeni `src/RentACar.Web/Components/Pages/Finance/TekCariToplu.razor` — "tek-cari-çok-kalem" modu:
   Cari arama (No/Ad/Soyad, TEK cari — `CustomerService.ListAsync`), Hesap No (IBAN) seçimi,
   "Carinin İşlem Listesini Getir" butonu (`CashService.GetStatementAsync(cariId)` çağırır — o
   carinin açık/bakiyeli kalemlerini listeler), Seçili Toplam gösterimi, tek tıkla TOPLU kapatma
   (seçili kalemleri kapatan tahsilat kaydı).
4. `src/RentACar.Application/Finance/CashService.cs` — `TekCariTopluKapatAsync(cariId, seçili
   kalem ID'leri, hesapId, …)` benzeri yeni bir metod ekle; **idempotency anahtarı** deterministik +
   MONOTON bileşen taşımalı (ör. işlem token + seçili kalem sayısı+toplamı — idempotency anahtar
   tasarımı dersi: değer-snapshot'ı tek başına yetmez).
5. Mevcut `/toplu-tahsilat` (çok-cari modu) **bozulmadan** kalır — `TekCariToplu.razor` ayrı bir
   sayfa/link olarak eklenir, mevcut sayfaya bir "Tek Cari Modu" linki eklenebilir.
6. **Zorunlu adversarial inceleme**: ayrı bir ajan, canlı DB'ye karşı — işaret hatası (gider mi
   tahsilat mı Debit/Credit yönü), çok-döviz (plaka bazlı gider farklı dövizdeyse), idempotency
   (çift-submit + tek-cari-toplu-kapatmada aynı kalemin iki kez kapatılması), yetki (Muhasebe rolü
   dışında erişim), RLS sızıntısı (başka tenant'ın cari ekstresi görünmemeli) açılarından çürütmeye
   çalışır. Critical/High/Medium bulgu varsa düzeltilmeden commit yok.

## Dokunulacak dosyalar
- `src/RentACar.Web/Components/Pages/Finance/TopluGider.razor`
- `src/RentACar.Application/Expenses/ExpenseService.cs`
- (yeni) `src/RentACar.Web/Components/Pages/Finance/TekCariToplu.razor`
- `src/RentACar.Application/Finance/CashService.cs` — yeni `TekCariTopluKapatAsync`
- `src/RentACar.Web/Components/Pages/Finance/TopluTahsilat.razor` — link eklenir (mevcut mod dokunulmaz)

## Migration
Muhtemelen yok (mevcut `Expense`/`AccountLedgerEntry`/`FinancialAccount` tablolarına yazılıyor;
`Expense`'e cariId/vade/hesapId kolonu gerekiyorsa **var mı önce kontrol et** — yoksa additive
nullable kolon eklenir, RLS zaten aktif tabloda, yeni tablo yok → RLS bloğu gerekmez).

## Test
- `ExpenseTests`/`TopluGiderTests`: plaka-bazlı çoklu satır → her plakanın `AccountRef`'i doğru
  araca yazılıyor mu (bağımsız oracle — elle kurulan 3 plaka/3 tutar, deftere düşen `AccountRef`
  ve tutarlar elle hesaplanan değerle karşılaştırılır). **Σ Borç(base) == Σ Alacak(base)** her satır
  için doğrulanır.
- Cari+Vade+Hesap alanlarının `Expense`/ledger kaydına doğru yazıldığı testi.
- `TekCariTopluTests` (yeni): 1 cari + 4 açık kalem elle kurulur (toplam elle hesaplanır); "Getir"
  3 kalemi seçip kapatınca defterde **Σ Borç == Σ Alacak**, cari bakiyesi elle hesaplanan farkla
  eşleşiyor mu.
- İdempotency testi: aynı token ile iki kez POST edilirse (çift-submit simülasyonu) tahsilat/gider
  yalnız BİR KEZ postlanıyor (ikinci çağrı no-op ya da güvenli engelleme).
- Çok-döviz testi: plaka bazlı gider satırlarından biri farklı dövizde girilirse `KurCozucu`
  üzerinden doğru çevrilip deftere base tutarda yazıldığı doğrulanır.

## Exit
- [ ] Toplu Gider'de plaka+cari+vade+hesap alanları çalışıyor, defter dengeli
- [ ] Tek-cari-toplu-kapatma modu çalışıyor, mevcut çok-cari modu regresyonsuz
- [ ] İdempotency + çok-döviz + yetki + RLS adversarial bulguları düzeltildi (Critical/High/Medium yok)
- [ ] Tam suite yeşil

## Notlar
Bu faz **finans modülünün temel işiyle (`CashService`/`KasaHub`) aynı dosyaları** genişletiyor ama
`toplu_gider`/`toplu_tahsilat` ekranlarının kendi kapsamıdır — `hesap_para_islem`/`genel_kasa`
kalemleriyle KARIŞTIRILMAZ, onlar bu modülün fazları arasında AÇILMADI (bkz.
`docs/roadmap/_toplama-02-tanim-master.md` — FAZ-50 serisinde çözülüyor).
