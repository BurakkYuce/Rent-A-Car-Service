# F8.3 — Parite ve e2e doğrulaması (Finans fazı)

> Kalıp adım 3 ("Doğrulama: parite + e2e"). Kaynak: `F8.md` envanterindeki 19 Blazor sayfası (`Components/Pages/…`)
> ile SPA bileşenleri (`src/RentACar.Frontend/src/app/features/finance` — #299, `features/finance-documents` — #300),
> 2026-09-24 `main`. Yöntem: razor dosyası okundu (form alan adları, `[SupplyParameterFromQuery]` sorgu adları, eylem
> formları, `[Authorize]`); SPA form modeli, liste tanımı (`listeTanimi`), rota kapısı ve şablonla karşılaştırıldı. Eksik
> bulunan eklendi, bilinçli farklar gerekçeli. Uçlar #284 (F8.1a) ve #286 (F8.1b); bu PR'da backend koduna dokunulmadı
> (yalnız yönlendirme haritası ve menü kaydı).

## Özet

| Sayfa (Blazor → SPA) | Alan | Süzgeç | Eylem | İzin (SPA rota kapısı) | Sonuç |
| --- | --- | --- | --- | --- | --- |
| `/kasa` → `kasa` | virman formu 10/10 (kaynak/hedef hesap türü + hesap, tutar, döviz, kur, makbuz, şube, açıklama) | 6/6 (ara, tip, hesap, kanal, başlangıç, bitiş) | virman (onaylı, anahtarlı), özet kartlar, makbuz PDF, Excel/CSV/PDF | FinanceWrite | tam (bkz. bilinçli farklar) |
| `/finans/nakit-islem` → `finans/nakit-islem` | 8/8 (tutar, tarih, hesap türü + hesap, kanal, döviz, kur, açıklama) | cari arama → aranabilir cari seçici | tahsilat, ödeme (anahtarlı) | FinanceWrite | tam (bkz. bilinçli farklar) |
| `/finans/bakiye-duzeltme` → `finans/bakiye-duzeltme` | 8/8 (yön, tutar, döviz, kur, tarih, vade, makbuz, açıklama) | cari arama → cari seçici | düzeltme (onaylı, anahtarlı) | FinanceWrite | tam |
| `/cari-virman` → `cari-virman` | 10/10 (kaynak/hedef cari, tutar, döviz, kur, tarih, vade, makbuz, şube, açıklama) | 4/4 (cari, ara, başlangıç, bitiş) | virman (anahtarlı), geçmiş listesi | FinanceWrite | tam (TL toplam satırı yok) |
| `/depozito` → `depozito` | 4/4 (cari, tutar, hesap türü + hesap) | — | al, iade, mahsup, irat (irat onaylı) | FinanceWrite | tam |
| `/tek-cari-toplu` → `tek-cari-toplu` | 6/6 + satır başına seçim/tutar | cari (`?cariId=`) | toplu kapatma (onaylı) | FinanceWrite | tam |
| `/toplu-tahsilat` → `toplu-tahsilat` | hesap, kanal + satırlar | — | toplu tahsilat (anahtarlı) | FinanceWrite | tam (satır düzenleyici) |
| `/toplu-gider` → `toplu-gider` | 7/7 + satırlar (net, açıklama, araç) | — | toplu gider (anahtarlı) | FinanceWrite | tam (satır düzenleyici) |
| `/otomatik-tahsilat` → `otomatik-tahsilat` | tahsilat bayrağı, hesap + satır seçimi | 4/4 (sözleşme no, vade min/max, bakiyeli) | elle çalıştır (onaylı) | FinanceWrite | tam |
| `/donem-kapanis` → `donem-kapanis` | kapanış tarihi | — | kilitle, kilidi kaldır (ikisi onaylı) | FinanceWrite | tam |
| `/kurlar` → `kurlar` | çevirici 3/3, sabit kur 5/5 | — | TCMB yenile, sabit kur ekle/güncelle (`surum`)/sil | OperationsWrite ∨ FinanceWrite ∨ ViewReports (yazma FinanceWrite) | tam |
| `/cariler/{id}/ekstre` → `cariler/:id/ekstre` | tahsilat formu 6/6 | 6/6 (başlangıç, bitiş, döviz, kaynak, kira durumu, mod) | tahsilat, ters kayıt (FinanceReverse, onaylı), Excel/CSV/PDF | FinanceWrite ∨ ViewReports (#295 M1) | tam (izin daraltıldı, bkz. KVKK) |
| `/faturalar` → `faturalar` | manuel fatura, toplu fatura | 7/7 (ara, cari, durum, döviz, ofis, başlangıç, bitiş) | manuel, toplu, iade (FinanceReverse), PDF | FinanceWrite ∨ ViewReports | tam (bkz. bilinçli farklar) |
| `/faturalar/detay-listesi` → `faturalar/detay-listesi` | — | 7/7 | — | ViewReports | tam (menü izni farkı, bkz. riskler) |
| `/faturalar/{id}/yazdir` → `faturalar/:id/yazdir` | — | — | sunucu PDF'ine tam sayfa | ViewReports | tam |
| `/cezalar` → `cezalar` | yeni ceza + kalem ödemesi | 8/8 | kayıt (OperationsWrite), yansıt/öde (FinanceWrite), iptal (OperationsDelete) | OperationsWrite ∨ FinanceWrite ∨ ViewReports | tam (bkz. bilinçli farklar) |
| `/giderler` → `giderler` | gider formu (sözleşme hariç) + ödeme | 7/7 | kayıt, ödeme takibi | FinanceWrite ∨ ViewReports | kısmi: sözleşme alanı yok (eksik uç) |
| `/gelen-efatura` → `gelen-efatura` | bağla (`surum`), giderleştir | 8/8 | eşitle, kaydet, onayla, reddet, işle, bağla, giderleştir | FinanceWrite | tam |
| `/satislar` → `satislar` | satış formu | 7/7 | satış (FinanceWrite) | FinanceWrite ∨ ViewReports ∨ OperationsWrite | tam (bkz. bilinçli farklar) |

## Bulunan eksik (bu PR'da eklendi): SPA'dan Blazor'a tam sayfa giden bağlantılar

Kira formunun sabit finans paneli (F4.4, `features/kira-formu/finans-paneli/`) üç bağlantıyı `href` ile Blazor finans
sayfalarına TAM SAYFA gönderiyordu (F4.4 yer tutucusu; F8 ekranları o gün yoktu). Artık `routerLink`:

1. Nakit sekmesi "Depozito ekranı" → `/app/depozito` (`finans-depozito.ts`).
2. Faturalar sekmesindeki fatura no → `/app/faturalar` (`finans-faturalar.ts`; Blazor paneli de listeye gidiyordu).
3. Ceza/HGS sekmesi "Ceza ekle / yansıt" → `/app/cezalar` (`finans-listeler.ts`).

Uygulamadaki diğer bağlantılar zaten router'daydı: kasa ve depozito ekranındaki cari adı → SPA ekstre, kira formu ve
cari detayındaki ekstre bağlantıları (F7.3). Genel arama sonuçları bilinçli olarak tam sayfa kalır (F11.2b: taşınan
ekranlar sunucuda yönlenir).

Çit: `e2e/kesis-f8.spec.ts` (bağlantı hedefleri, tıklamada tam sayfa yüklemesi olmadığı — pencere işareti korunur —
ve F8 ekranlarının hiçbir bağlantısının Blazor finans sayfasına düşmediği).

## Para kararları (#299, #300 — değişmedi, kesiş bunları taşır)

- **UI formül taşımaz:** KDV, genel toplam, kalan, baz tutar, bakiye önerisi sunucudan. Gövdeler yalnız net + oran
  (+ isteğe bağlı açık kur); KDV oranı sabit seçim (%0/%1/%10/%20), sunucunun beklediği kesir metniyle.
- **Idempotency:** işlem başına `Idempotency-Key`, 2xx ya da kesin 409 sonrası yeni. Kayıp yanıt → donmuş kopya: AYNI
  yol + anahtar + gövde (toplu işlemlerde satırlar dahil); form ve cari seçici kilitli, sayfa/sekme terki sorulur.
  409'da otomatik tekrar yok; `mukerrer` metni ikinci işleme yönlendirmez (`mevcut.ayniIcerik` ayrımı).
- **Kur:** boş = sunucu çözer (`KurCozucu`); döviz değişince kur temizlenir (#291 M2); TRY + kur ≠ 1 sunucuda red.
- **İyimser eşzamanlılık:** sabit kur PUT'u ve gelen e-fatura bağlama `surum` taşır; 409 `cakisma` formu silmez, kirli
  forma birleşir (#291 M1).
- **Satır/kayıt başına form** `@for … track id` ile yeniden kurulur (ceza kalemi, gider ödemesi, tek cari satırları);
  kalem değişince tutar temizlenir. Boş tutar = kalan, SUNUCUDA.
- **Yapısal uçlar** (iade, yansıt, iptal, toplu fatura, satış, dönem kilidi, otomatik tahsilat): onay + tek istek
  kilidi; ikinci deneme sunucuda 400 ya da `mukerrer`. Giderleştirme başlık göndermez (sunucu deterministik anahtar).
- Dönem kilidi hataları alana yazılır.

## KVKK kararları

- Müşteri adları sunucunun `MusteriGorunumu` kuralından; TC hiçbir ekranda ve yanıtta yok; ceza detayındaki telefon
  sunucu anonimleştirmesine tabi. Tarayıcı deposuna kişisel veri yazılmaz (etiket önbelleği yalnız bellekte).
- **Cari ekstresi (#295 M1):** SPA rotası FinanceWrite ∨ ViewReports ile kapılı. Blazor sayfası yalnız `[Authorize]`
  idi; kesişten sonra pilot firmada eski adresten gelen operatör SPA izin kapısına düşer ("Bu sayfayı görüntüleme
  yetkiniz yok.") ve firma geneli cari defterini görmez. Bu bilinçli bir davranış değişikliğidir. Uç
  (`/api/ui/v1/finans/cariler/{id}/ekstre`) şimdilik daha geniş; daraltma kararı kuyrukta.

## Bilinçli farklar (gerekçeli)

| Fark | Gerekçe |
| ---- | ------- |
| Nakit İşlem / Bakiye Düzeltme: arama sonuç listesi yerine aranabilir cari seçici + seçilen carinin bakiyesi | Aynı seçim; `?cariId=` iki sayfada da okunur ve izlenir (r299 LOW-3). Blazor'un `?q=` araması seçicinin içinde. |
| Toplu Tahsilat / Toplu Gider: `cariId;tutar` metin kutusu yerine satır düzenleyici | Aynı alanlar; satır kimliği donmuş kopyada korunur, sunucu satır hatası doğru satıra yazılır (`satirlar[i].*`). |
| Cari Virman geçmişinde TL toplam satırı yok | SPA para toplamaz; toplam ucu yok. |
| Kasa işlem listesi sunucu sayfalamalı | Blazor tüm listeyi çiziyordu. |
| Fatura listesinde cari kodu, vergi dairesi/no ve ülke sütunları yok | Uç KVKK gereği dönmüyor. |
| Fatura listesinde döviz bazında Σ toplam satırı yok | İstemci toplamaz; özet ucu yok (aşağıda eksik uç 3). |
| Fatura listesinde satır bazında "İade edilmiş" bilgisi yok | Uç satırda vermiyor; iade düğmesi ve rozeti detayda (`iadeFaturaId`). |
| Fatura döviz/ofis süzgeç seçenekleri veriden türetilmiyor | Döviz TRY/USD/EUR, ofis serbest metin. |
| Manuel fatura tarihi gün seçici | Blazor `datetime-local` idi; gün İstanbul gece yarısı. |
| KDV oranı serbest sayı değil, %0/%1/%10/%20 seçimi | Sunucunun beklediği kesir metni; istemci çevirmez. |
| Ceza listesinde e-posta ve rezervasyon kaynağı sütunları yok; ödeme şekli detayda | Uç KVKK gereği dönmüyor. |
| Araç satışında "satılabilir araç" süzgeci yok; geçen süre / kredi firması / poliçe sütunları yok | Genel `secim/arac` kullanılıyor, satılmış araç sunucuda 400 (aşağıda eksik uç 4); uç bu sütunları dönmüyor. |
| Gelen e-faturada gider kategorisi seçimi yalnız OperationsWrite'lı rolde | Seçim ucu `/gider-turleri` OperationsWrite ister (aşağıda eksik uç 2). |
| Yönlendirmede Blazor süzgeç adlarının bir kısmı tanınmaz | Sorgu AYNEN taşınır. Liste ekranlarında aynı adlı olanlar süzer (`cariId`, `doviz`, `ofis`, `plaka`, `tip`, `sube`, `durum`, `bas`, `bit`, `musteri`, `firma`, `ettnBas`, `ettnBit`). SPA adı farklı olanlar bozuk parametre sayılır → varsayılan liste: fatura `ara` (→ `q`) ve `durum` (→ `iptal`), detay listesi `ara`/`iptal`, gider `ara`, ceza `makbuz`/`odeme`/`sube` (→ `makbuzNo`/`odemeDurumu`/`islemSube`), gelen e-fatura `gider` (→ `giderlestirildi`), satış `plakaF`/`cariF`/`durumF`/`devirF`/`ofisF`. Kasa, cari virman, otomatik tahsilat, kurlar ve ekstre süzgeçleri URL'de değil formda tutulur; bu sayfalarda Blazor sorgusu yok sayılır. Uygulama içinde bu parametrelerle F8 sayfasına giden bağlantı yok (Blazor tarafında yalnız Nakit İşlem / Bakiye Düzeltme kendi `?q=&cariId=` bağlantılarını üretiyordu; `cariId` SPA'da okunur). Yalnız yer imleri etkilenir. |

## Eksik uçlar (backend'e dokunulmadı; #300'den devralındı)

1. FinanceWrite ile kullanılabilen **kira seçim ucu** (`secim/kira`) — gider formundaki "Sözleşme" alanı için. Bu uç
   gelene kadar giderler ekranında sözleşme sütunu ve alanı yok; bu tek kısmi parite maddesi.
2. FinanceWrite ile okunabilen **gider kategorisi seçimi** (`/gider-turleri` OperationsWrite ister).
3. Fatura listesi için **döviz bazında özet** ucu.
4. Araç satışında **satılabilir araç** seçimi (satılmamış araçlar).

## Riskler ve açık notlar

- **Menü izni ile rota kapısı farkı (Fatura Detay Listesi):** menü öğesi Blazor sayfasının izninden türer
  (FinanceWrite; `MenuKaydiTests` kilitler), SPA rotası ve ucu ise ViewReports ister. Varsayılan rollerde fark yok
  (FinanceWrite'lı Admin/Yönetici/Muhasebe ViewReports'a da sahip). Kullanıcı bazlı istisnayla FinanceWrite'ı olup
  ViewReports'u yasaklanan kullanıcı öğeyi görür, açınca izin bandı alır. Blazor sayfası silinince menü izni grup
  kapısına düşer; o PR'da öğe ViewReports'a çekilmeli.
- **Cari bakiye raporu (F10) ekstre bağlantısı:** Blazor raporunda satır başına "Ekstre →" vardı; SPA rapor ekranında
  yok. F8 sayfasına giden bir bağlantı olduğu için burada not edildi; rapor motoruna satır bağlantısı F10 kapsamında.
- Blazor finans sayfaları ve 36 hedefsiz Blazor POST ucu **silinmedi** (F4.6b deseni: pilot sonrası ayrı PR).
  Exit'in "`@page` = 0" maddesi o PR'a kalıyor.

## e2e

**Sahte API (CI'da koşar):** `finance.spec.ts` (#299) ve `finance-documents.spec.ts` (#300): üç zorunlu senaryo, para
senaryoları, axe iki tema, 320/390/768/1440 taşma. Bu PR: `kesis-f8.spec.ts`:
- 18 eski adres → SPA; sorgu ve fragment korunur, doğru başlık açılır.
- Eski fatura yazdır adresi → SPA yazdır rotası → sunucunun PDF ucu; tek istek, döngü yok.
- İzinsiz operatör eski ekstre adresinden SPA izin kapısına düşer.
- F8 ekranlarındaki hiçbir bağlantı Blazor finans sayfasına düşmez.
- Kira formu finans panelindeki depozito, fatura ve ceza bağlantıları router'la gider (tam sayfa yok).

**Gerçek backend:** bu PR yeni ekran ya da uç getirmiyor. Finans uçlarının gerçek PostgreSQL üzerindeki izin, kapsam,
idempotency ve defter testleri F8.1 (`UiFinanceHubApiTests`, fatura/ceza/gider/satış uç testleri) içinde.
Yönlendirmenin gerçek boru hattı `IlkKesisHostTests`: pilot firmada 302 (admin ve operatör, ekstre dahil), pilot
olmayan firmada 200, POST yönlenmez, döngü yok, giriş sonrası dönüş.

## Devredilen testler

- `IlkKesisTests`: F8 pozitif/negatif harita; envanter testi F4–F8 + F10; F8 hedeflerinin Angular rota dosyalarında
  tanımlı olduğu ve ekstre rotasının FinanceWrite ∨ ViewReports kapısı (hedefler kaynakla seçilir). Host: F8 302,
  pilot olmayan firmada 200, F8 POST'ları yönlenmez, oturumsuz zincir döngüsüz. "Taşınmamış modül" örneği artık
  `/bildirimler`.
- `MenuKaydiTests` / `UiSecimMenuTests`: spa kümesi ve sayısı (F9.3 sonrası 71 → 88; Finans +17); Finans öğeleri FinanceWrite'a
  bağlı (operatöre ek izinle görünür, Muhasebe'ye yasakla gizlenir).
- `UcIzinKapsamaTests`: F8 envanterindeki 36 silinecek uç sayılmaz; kalıcı dar uç tabanı (F9.3 sonrası) 5 → 2;
  OperationsDelete ve FinanceReverse türü uçlar yaşadıkça silinecek kümede aranır.
- `MenuKapsamaTests`: F8 SPA rotalarının parametresiz 17'si menüde spa öğesi.
