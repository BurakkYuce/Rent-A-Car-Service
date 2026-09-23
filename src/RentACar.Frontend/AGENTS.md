# AGENTS.md — RentACar.Frontend

Angular 21.2 SPA, `/app/` altında servis edilir. Bu dosya bu klasörde çalışan her ajan/geliştirici için
kısa kurallardır; genel bağlam kökteki `CLAUDE.md` ve `docs/roadmap/` altında.

## Kurallar (lint/tip kapısı zorlar)

- **Katmanlar:** `src/app/core/` (altyapı, saf yardımcılar) ve `src/app/shared/` (ortak bileşenler)
  `src/app/features/`'ı içe aktaramaz. Özellikler core/shared'i kullanır, tersi yok. Takma adlar:
  `@core/*`, `@shared/*`, `@features/*`.
- **OnPush:** her bileşen `ChangeDetectionStrategy.OnPush`. Uygulama zoneless
  (`provideZonelessChangeDetection`); `zone.js` eklenmez, durum signal ile taşınır.
- **`any` yasak.** Bilinmeyen veri `unknown` + daraltma.
- **Türkçe güvenli metin:** ham `toLowerCase()`/`toUpperCase()`, argümansız `toLocale*Case()` ve
  `lowercase`/`uppercase`/`titlecase` pipe'ları yasak ("I/ı", "İ/i" bozulur, arama kaçar).
  `@core/metin/tr-normalize` (`trKucukHarf`, `trBuyukHarf`, `trNormalize`) kullanın.
- **Yalnız göreli URL:** `src/` içinde `http(s)://` ile başlayan metin yasak. XSRF header'ı yalnız
  göreli URL'lere eklenir; CSP `connect-src 'self'`. **Tek istisna — harici paylaşım (F4.3b):**
  `src/app/shared/dis-baglantilar.ts` içinde YALNIZ `https://wa.me/` ve Gmail taslak kökü
  (`https://mail.google.com/mail/?view=cm&fs=1`) yazılabilir (lint seçicisi bu iki kökü birebir tanır; başka
  mutlak adres o dosyada da hata). Gerekçe: bunlar API çağrısı değil, kullanıcının yeni sekmede açtığı gezinme
  bağlantısıdır (`window.open`, `noopener`) — XSRF/`connect-src` kapsamına girmez; dosya `@angular/common/http`
  ve `@core/api/*` içe aktaramaz (lint). Yeni dış bağlantı gerekiyorsa kök bu dosyaya + lint seçicisine eklenir.
- **CSP `script-src 'self'`:** inline script/handler yok. Bu yüzden production'da
  `inlineCritical: false` ve `fonts.inline: false`; fontlar self-host.
- **Metinler Türkçe**, `lang="tr"`. Sınıf adları İngilizce olabilir, alan adları Türkçe.

## Tasarım sistemi (F3.1)

- **Token'lar** `src/styles/_tokenlar.scss`, önek `--rc-*`. Yoğun varsayılan: gövde 13 px, kontrol
  32 px (28/36), tablo satırı 32 px. Yazı boyutu yalnız ölçekten (`--rc-yazi-2xs…2xl`), boşluk 4 px
  ızgarasından (`--rc-bosluk-N` = N × 4 px). Bileşen stilinde ham hex/px renk-boyut yazılmaz.
- **Tema:** `:root` açık; `prefers-color-scheme: dark` + `:root:not([data-theme=light])` koyu;
  `[data-theme=dark]` her durumda koyu. `TemaServisi` (`@core/tema`) modu yazar/saklar
  (`localStorage` `rc.tema`, yalnız mod — kişisel veri değil) ve kiracı rengini `--rc-kiraci-*`
  değişkenlerine uygular; üstündeki metin ve okunur ton otomatik seçilir.
- **Kontrast kapısı:** `npm run kontrast` iki temada metin ≥ 4.5, kontrol kenarı ve odak ≥ 3 denetler
  ve `TEMA_ZEMINLERI` kopyasının SCSS'le aynı olduğunu doğrular. Renk değiştiren PR tabloyu yeşil
  tutmak zorunda (`npm run lint` bunu da koşar).
- **Font:** Inter değişken, self-host (`src/styles/fonts/`, latin + latin-ext; ğ/ş/İ/₺ latin-ext'te).
- **Biçim:** `@core/bicim/bicim` (`paraBicimle` → `1.234,56 ₺`, yarım kuruş sıfırdan uzağa;
  `tarihBicimle` → `dd.MM.yyyy`; `tarihSaatBicimle` İstanbul saatiyle) ve pipe'ları `para`, `sayi`,
  `tarih`, `tarihSaat` (`@shared/bicim/bicim-pipe`). `LOCALE_ID = 'tr'`. Kendi `toFixed`/`Intl`
  biçimi yazılmaz.
- **i18n:** Transloco, tek dil tr. Şablonda `'anahtar' | transloco`, TS'te tipli `ceviriFonksiyonu()`
  (anahtar birliği TÜM dosyalardan). Sözlük ikiye bölünür (çekirdek eki "rota bazlı tembel çeviri"):
  - **Çekirdek** `src/i18n/tr.json` — ilk pakete gömülü; YALNIZ kabuk/core/shared ya da birden çok
    özelliğin ortak metni (`form`, `tablo`, `kabuk`, `oturum`, `geriBildirim`…).
  - **Özellik bloğu** `src/i18n/bloklar/<blok>.json` — ayrı tembel parça, rota `canActivate`'inde bileşenden
    ÖNCE birleşir (`@core/i18n/ceviri-blogu`; anahtar yanıp sönmez, hata → parça hatası yolu). Üst düzey
    anahtar → blok eşlemesi TEK yerde: `scripts/i18n-tipleri.mjs` `BLOK_HARITASI`.
  - **Yeni faz/özellik:** (1) `BLOK_HARITASI`'na `<ustAnahtar>: '<blok>'`; (2) metinleri
    `src/i18n/bloklar/<blok>.json`'a (üst düzey anahtar altında) yaz — `tr.json`'a yazılırsa `npm run i18n:tipler`
    taşır; (3) rotaya `canActivate: [ceviriBlogu('<blok>')]` ya da rota dizisine `ceviriBloguyla('<blok>', [...])`
    (ör. `kira-formu.routes.ts`); başka özelliğin bloğunu kullanan sayfa onu da listeler; (4) `npm run i18n:tipler`.
    Bloğu yüklemeyen rotada anahtar eksik anahtar gibi görünür (geliştirmede `EKSİK: …`) — e2e yakalar.
  - Birim testleri rotadan geçmez: `src/test-saglayicilari.ts` (üretilir, `angular.json` `test.providersFile`)
    tüm blokları önyükler. Rota yükleme davranışı `ceviri-blogu.spec.ts`'te.
  - `npm run i18n:tipler` üretir/taşır (`ceviri-anahtarlari.ts`, `ceviri-bloklari.ts`, `test-saglayicilari.ts`, blok
    sırası); `lint` (`--kontrol`) bayatlığı ve `tr.json`'da kalmış blok anahtarını reddeder.
- **İkonlar:** `<rc-ikon ad="…" />`, Tabler alt kümesi. Yeni ikon: `ikon-listesi.json`'a ekle,
  `npm run ikonlar`. Üretilen `ikon-kaydi.ts` ve `ceviri-anahtarlari.ts` elle düzenlenmez; lint
  bayatlığı yakalar.
- **Primitifler:** sınıf tabanlı `rc-dugme` (`--birincil`, `--hayalet`, `--kucuk`, `--buyuk`,
  `--ikon`, `rc-dugme-grubu`), `rc-rozet` (`--basari|uyari|hata|bilgi`), `rc-iskelet`; bileşen
  `rc-bos-durum`. Yerel `<button>`/`<a>` üstünde kullanılır.

## Veri katmanı (F3.4)

- **HTTP yalnız `ApiIstemcisi`** (`@core/api/api-istemcisi`): yol tip düzeyinde `/api/ui/v1/...`,
  `withCredentials`, XSRF (`XSRF-TOKEN` → `X-XSRF-TOKEN`), her hata tipli `ApiHatasi { status, kod,
detay, alanlar? }`. `features/**` içinde `HttpClient` içe aktarımı lint'le yasak. Otomatik yeniden
  deneme yok (`mukerrer`'de kayıt yeniden yüklenir, yeni anahtarla tekrar gönderilmez).
- **Store verisi yalnız `TemelStore<T, P>`** (`@core/veri/temel-store`): dört durum
  `bos | yukleniyor | hazir | hata`; `switchMap` ile son istek kazanır; hata ASLA boş liste değildir
  ("Kayıt bulunamadı" için `kayitYok(durum)`). `features/**/store/**` ve `*.store.ts` içinde ham
  `.subscribe(` lint'le yasak.
- **Ne zaman yüklenir:** sayfanın `providers`'ında `FetchPolicy` (`ilk | sorgu | baglam | elle`);
  bağlam `OTURUM_BAGLAMI` (F3.3: `OturumServisi.baglam`).
- **Liste sorgusu:** `listeTanimi({ filtreler, siralanabilir, varsayilanSirala })` + sayfada
  `listeSorgusuUrlSenkronu(tanim)`. URL tek doğruluk kaynağı; bozuk parametre varsayılana düşer,
  `boyut` 1..200, `sirala` beyaz listeden. Yazarken `{ yaziyor: true }` (replaceUrl).
- Katlanır filtre: `<rc-katlanir-filtre>` (`@shared/katlanir-filtre`); kap stilsiz, aç/kapa düğmesi `rc-dugme`.
- İlk gerçek liste ekranı: `features/kiralar/kira-listesi` (F4.2) — `listeTanimi` + URL senkronu + üç
  `FetchPolicy` bağı (liste, özet, öneriler) + `TahsilatAnahtar`'lı "Tahsil Et" (`tahsil-paneli.ts`). Yeni liste
  ekranı bu kablolamayı örnek alır.

## Oturum ve geri bildirim (F3.3)

- **Oturum:** `OturumServisi` (`@core/oturum/oturum-servisi`) — `ben()` (üretilen `BenYaniti`), `girisYapildi()`,
  `izinVar('FinanceWrite')`, `baglam()` (= `OTURUM_BAGLAMI`), `girisYap`, `cikisYap` (tam temizlik: kayıtlı
  temizleyiciler + `rc.*` localStorage/sessionStorage, `rc.tema` hariç), `temizlikKaydet(fn)` (sekmeler, önbellekler
  çıkışta silinsin diye buraya kaydolur). Rotalar: `canMatch: [oturumGuard]` ya da `[izinGuard('FinanceWrite')]`
  (`@core/oturum/oturum-guard`); `/giris` `misafirGuard`. Dönüş adresi yalnız uygulama içi (`guvenliDonusAdresi`).
- **Hatalar `kod`'a göre** (`oturumInterceptor`, `provideApiIstemcisi(oturumInterceptor)`): `oturum_yok` → yerinde
  yeniden giriş diyaloğu + AYNI isteğin taze XSRF ile tekrarı (sayfadan gidilmez); `xsrf_gecersiz` → belirteç
  yenilenip BİR kez tekrar; `yetki_yok`/`pilot_degil`/alansız `cakisma` → uyarı bandı; `mukerrer` → tekrar YOK,
  `MUKERRERDE_YENILE` çağrılır + bilgi toast'u; `cok_istek` → uyarı toast'u; 5xx/ağ → hata toast'u;
  `kiraci_kapali` → giriş sayfası. İstek bayrakları: `istekBaglami({ sessiz, yenidenGirisYok, mukerrerdeYenile })`
  (`@core/oturum/istek-baglami`) → `ApiIstemcisi` `context`.
- **Form hatası:** F3.6 `formGonderimi` alanları kontrollere yazar; interceptor'ın sayfa düzeyinde gösterdiği
  kodlar (`genelGosterilir`: `yetki_yok`, `pilot_degil`, `mukerrer`, `cok_istek`, 5xx, ağ, `kiraci_kapali`) forma ikinci kez yazılmaz. `ONAY_ISTEMI` (kaydedilmemiş değişiklik) = CDK onay diyaloğu
  (`cdkOnayIstemi`). Form değerlerine hiçbir dalda dokunulmaz.
- **Geri bildirim:** `ToastServisi` (6 durum: `basari|bilgi|uyari|hata|notr|bekleme`; hata/uyarı `role="alert"`,
  diğerleri `role="status"`; `bekleme` → `bitir`), `UyariBandiServisi.goster({ tur, mesaj, kod?, kalici? })`,
  `await OnayServisi.sor({ baslik, mesaj, tehlikeli? })` → `boolean` (CDK, odak kilidi, Esc = vazgeç).
  CDK diyalog tembel yüklenir. Genel sınıflar (`src/styles/_geri-bildirim.scss`): `rc-diyalog*`,
  `rc-dugme--tehlike`, `rc-form-mesaji(--hata|--uyari)`; alan/girdi sınıfları F3.6 `_form.scss`'te.
- `?bilgi=` → başarı toast'u, `?hata=` → hata bandı; bir kez gösterilip URL'den silinir. Yeni sürüm: `index.html`'deki
  ana paket adı 5 dk'da bir (ve sekme görünür olunca) karşılaştırılır → "Yenile" bildirimi; ChunkLoadError → hedef
  adrese tek kontrollü yenileme (`rc.parcaYenileme`, 60 sn döngü koruması). Yakalanmamış hata oturum açıkken
  `POST /api/ui/v1/istemci-hata`'ya raporlanır (yalnız yol, sorgu dizesi yok; sayfa başına 10).
- e2e: `e2e/oturum.spec.ts` `/api/ui/v1`'i Playwright ile sahteler (`e2e/ortak.ts`); vitrin formu
  `/app/vitrin/geri-bildirim` bu sözleşmeyi gösterir (canlıda uç yok).

## Form seti (F3.6)

- **Typed Reactive Forms** (`FormGroup`/`FormControl<T | null>`); Signal Forms yok (deneysel).
- **Kontroller** (`@shared/form/...`, hepsi CVA): `rc-metin-girdisi`, `rc-metin-alani`, `rc-sayi-girdisi`
  (`number`), `rc-para-girdisi` (değer invariant METİN `"1234.56"`, görüntü `1.234,56`; JSON sayısı da
  yazılabilir; programatik değer yarım kuruş sıfırdan uzağa yuvarlanır), `rc-secim` (yerel select), `rc-arama-secim` (CDK
  overlay + listbox; kaynak `sunucuSecimKaynagi('musteri')` → `/api/ui/v1/secim/*`, `limit ≤ 20`,
  gecikmeli, değer seçilen öğe `{ id, etiket, … }`), `rc-onay-kutusu`, `rc-anahtar`, `rc-radyo-grubu`,
  `rc-tarih-secici` (değer takvim günü `"2026-09-22"`, `aralik` ile `{ baslangic, bitis }` + hazır
  aralıklar), `rc-tarih-saat-secici` (değer UTC anı; İstanbul saatiyle gösterilir). Tarih günü `Date`
  / `toISOString` yoluna SOKULMAZ; an yerel saate çevrilip UTC sayılmaz.
- **Para girdisi kuralı (F4.2 adversarial):** KULLANICININ yazdığı tutarda 2'den (`kesir`) fazla anlamlı ondalık
  YUVARLANMAZ → `paraFazlaHane` ("En fazla 2 ondalık hane girilebilir."). Odakta (Tab, programatik, otomatik
  doldurma) metnin TAMAMI seçilir ve düzenleme yazımı DOM'a eşzamanlı yazılır — HER para girdisinde varsayılan
  (F4.4; eski `odaktaSec` seçeneği kalktı): yazılan önceden dolu tutarın sonuna eklenmez, yerine geçer.
- **Alan:** her kontrol `<rc-alan etiket="…" ipucu="…">` içinde: etiket `for`, zorunlu `*` (doğrulayıcıdan),
  hata yuvası, `aria-invalid`/`aria-describedby`/`aria-required`. Radyo grubunda `grup`.
- **Gönderim yalnız `formGonderimi()`** (`@shared/form/form-gonderimi`): çift tık tek istek, istemci
  doğrulaması geçmezse istek gitmez, `Idempotency-Key` kuralı `GonderimKilidi`'nde (mantıksal gönderim
  başına anahtar, yeniden denemede aynı, her 2xx ve `mukerrer` sonrası yeni; deterministik sunucu
  anahtarı `deterministikAnahtar` ile dokunulmadan önce gelir), hata alanlara (`alanlar`), değerler
  korunur, 2xx'te form `pristine`.
- **Seç veya yaz:** `<rc-metin-girdisi liste="dl-kimlik">` + sayfada `<datalist id="dl-kimlik">` (serbest
  metin de kabul; Blazor ComboBox konvansiyonu).
- **Kaydedilmemiş değişiklik:** sayfa `KaydedilmemisDegisiklikSahibi` uygular, rotaya
  `canDeactivate: [kaydedilmemisDegisiklikGuard]`, kurucuda `sayfaTerkKorumasi(() => form.dirty)`.
- **Yerleşim:** `rc-sekmeli-form` + `rcSekmePaneli` (derin bağlantı `#sekme=…`, gizli sekmedeki hatalı
  alana geçip odaklanır: `ilkGecersizeGit()`) + `rcYanPanel` (sabit yan panel); `rc-tanim-crud`
  (`TanimAlani[]` + `TanimKaynagi`, REST için `restTanimKaynagi('/api/ui/v1/…')`); form ızgarası
  `.rc-form-izgara`. Yazdırma: `_yazdir.scss` (gizli sekmeler başlığıyla basılır, `.rc-yazdirma-gizle`).
- Vitrin: `/app/vitrin/form`, `/app/vitrin/tanim` (e2e bunların üstünde).

## Kira formu (F4.3)

- `features/kira-formu/`: TEK bileşen iki rotada (`/kiralar/yeni`, `/kiralar/:id`) + `/kiralar/:id/yazdir`
  (sunucu PDF ucuna tam sayfa). Durum/eylemler `KiraFormuDurumu`'nda (sayfa `providers`), saf kurallar
  `kira-formu-modeli.ts`'te (sorgu sözleşmesi `?varac&vfrom&vto&vgrup&musteriId`, `#sekme=…&alt=…`,
  gövdeler: POST whitelist, PUT 58 alanın HEPSİ). Tutar formülü YOK — `hesapla` / `donus-hesapla`.
- Hızlı Giriş alanları AYNA: aynı `FormControl` iki girdiye bağlanamaz → `form.ayna` + `aynalariBagla`.
  Formu sıfırlarken YALNIZ `formuSifirla` (ayna değerleriyle birlikte; düz `reset` aynayı null'layıp
  kanoniği siler — e2e yakaladı).
- **İyimser eşzamanlılık:** detaydaki `kira.surum` PUT'a zorunlu gider; bayatsa 409 `cakisma` → güncel kayıt
  okunur ve KİRLİ forma birleştirilir (`sunucuDegerleriniBirlestir`: dokunulmayan alan sunucu değerine çekilir,
  dokunulan korunur, ikisi de değiştiyse alan işaretlenir). İşlem sonrası ve sekmeye dönüşte de aynı yol.
  Dokunulmayan provizyon tarihi sunucunun orijinal anıyla geri gider (gün yuvarlaması yok).
- Sayfada `<form>` yok (iç içe form + Enter'la yanlış gönderim olmasın); mini işlemler düğmeyle.
- Sabit yan paneldeki finans paneli (F4.4) `finans-paneli/`: tembel parça (`rc-kira-finans-yuvasi` dinamik
  `import()`; `@defer` ilk pakete ~7 kB defer çalışma zamanı ekliyordu), durum/eylemler `KiraFinansDurumu`'nda.
  Para kuralları `docs/api/idempotency-envanteri.md` "SPA uygulaması" (tahsilat satır kopyası, 409 `mukerrerBasligi`).
- **F4.3b parite ekleri:**
  - Müşteri sekmesi cari özeti `GET /kiralar/{id}/musteri-ozet`. Müşteri PII'sinin TEK sunucu kuralı
    `MusteriGorunumu` (özet + detay taraf adı + paylaşım barı + hazır mesaj): TC kimlik HİÇ dönmez (Blazor gibi
    "şifreli — cari kartında"); ehliyet/pasaport numarası yalnız sunucuda maskeli (≥ 8 → son 4, 5–7 → son 2, ≤ 4
    tamamen yıldız; maske istemcide YAPILMAZ); KVKK `Anonim*` bayrakları grubu boşaltır (`AnonimAd` → özette
    `ad: null`, detayda "Anonim müşteri", mesajda "Sayın müşterimiz"; tel/e-posta paylaşım ön-doldurmasında da `null`).
  - Hızlı Giriş "Ceza" rozeti ve kayıtlı kiradaki "Ek hizmet tutarı" detayın `toplamlar` alanından (sunucu toplar;
    SPA toplamaz).
  - `?musteriId=` / penceresiz `?varac=` etiketi `GET /secim/musteri/{id}` / `/secim/arac/{id}` ile çözülür (hata →
    geçici etiket kalır; kayıt yalnız kimlikle).
  - Ek hizmet matrisi `GET /kiralar/ek-hizmet-katalogu` (birim net + KDV yalnız gösterim; satır tutarı `hesapla`'dan).
    Katalog kesikse (`toplam > ogeler`) listede olmayan tanım sunucu aramasıyla (`q`) eklenir.
  - Kaynak / özel kod datalist önerileri yazılanla `q` ile sunucuda aranır (`oneriAramasi`, 250 ms).
  - Paylaş barı (`rc-kf-paylas-bari`): hazır metin sunucudan (`paylasim.mesaj`), link varsa kendi kökünden eklenir;
    bağlantılar `@shared/dis-baglantilar`.

## Tablo motoru (F3.5)

- **`<rc-tablo>`** (`@shared/tablo/tablo`), TanStack `table-core` + `virtual-core` doğrudan (bağdaştırıcı
  yok, sürümler sabit). Rota ile TEMBEL yüklenen sayfada kullanılır (ilk pakete girmez). Örnek kablolama:
  `features/vitrin/tablo-vitrini` (`/app/vitrin/tablo`, 49 sütun × 5.000 kayıt).
- Girdiler: `etiket`, `sutunlar: TabloSutunu<T>[]` (`kod` kalıcı; `tur: para|sayi|tarih|tarihSaat|metin`,
  `sirala: true | 'apiAlani'`, `sabit`, `gizli`, `gizlenemez`, `genislik`), `kaynak` = `store.liste.durum()`
  (TemelStore; `Sayfa<T>` → sunucu sayfalaması), `satirKimligi`, `tabloKodu` (kullanıcı düzeni),
  `sirala`/`varsayilanSirala` (F3.4 sorgusu), `secilebilir` + `[(secim)]`, `disaAktarma`.
  Çıktılar: `siralaDegisti`/`sayfaDegisti`/`boyutDegisti` → `liste.degistir({...})`, `satirAc`, `yenidenDene`.
- Özel hücre: `<ng-template rcTabloHucre="kod" [rcTabloHucreSutunlar]="sutunlar" let-satir>`. Hücredeki
  bağlantı/düğme/form denetiminde Enter ve çift tıklama satırı AÇMAZ (denetimin kendi işi; F4.2). Hücrede
  `rc-gorunmez` (position: absolute) kullanan şablon kabına `position: relative` verir — yoksa görünmez metin
  kaydırıcının dışına konumlanıp sayfayı yatay taşırır.
- Dört durum ayrı: `bos` mesajsız, `yukleniyor` iskelet/soluk önceki veri + `aria-busy`, `hata` bandı +
  yeniden dene (ASLA "kayıt yok" değil), "Kayıt bulunamadı" yalnız başarılı sıfır kayıtta.
- Kullanıcı düzeni (sıra/görünürlük/genişlik/sıralama) `GET/PUT/DELETE /api/ui/v1/tablo-duzenleri/{kod}`;
  bayat/bozuk kayıt tanımla uzlaşır (bilinmeyen kod düşer, yeni sütun tanımdaki yerine girer).
- Klavye (APG Data Grid): oklar, Home/End, Ctrl+Home/End, PageUp/Down; satırda Enter açar, Boşluk
  seçer; başlıkta Enter sıralar, Alt+←/→ genişlik, Alt+Shift+←/→ sıra. Roving tabindex.
- Dışa aktarma yalnız sunucu uçlarıyla (`/listeler/export/*`, `/raporlar/export/*`); istemcide dosya üretilmez.

## Kabuk ve sekmeli çalışma alanı (F3.2)

- **Rotalar:** `/giris` kabuk DIŞINDA; geri kalan her şey `canMatch: [oturumGuard]` olan kabuk rotasının
  (`kabuk/kabuk.routes.ts`, tembel) çocuğu. **Sayfa eklemek = `src/app/sayfalar.ts`'e satır:** `title`
  zorunlu (`'<Ad> — RentACar'` → belge başlığı + sekme etiketi), `loadComponent`, kirli form varsa
  `canDeactivate: [kaydedilmemisDegisiklikGuard]`, izin varsa `canMatch: [izinGuard(...)]`. Kayıt sayfası
  yol parametresi olarak YALNIZ `:id` kullanır (`kiralar/:id`); başka parametreli sayfa yenilemede geri gelmez.
- **Menü:** `GET /api/ui/v1/menu`'den (`MenuStore` + `FetchPolicy`, 5 dk'da bir ve bağlam değişince tazelenir).
  İstemci SÜZMEZ (izin/modül sunucuda). `sahip: 'spa'` → router (`rota` router yolu; `/app` öneki de kabul),
  diğer her sahip → **tam sayfa** (`/app` dışı Blazor adresi). Blazor'a geçmeden önce TÜM sekmelerdeki
  kaydedilmemiş değişiklik tek soruyla sorulur (`SekmeServisi.ayrilmaOnayi`), onaydan sonra `beforeunload`
  ikinci kez sormaz (`SayfaTerki`). Faz kesişinde (F5+) menü kaydında `sahip` `spa` olunca öğe kendiliğinden
  SPA'ya döner — kabukta kod değişmez. Etkin sayfa: en uzun `/`-sınırlı önek, `aria-current="page"`, grubu açılır.
- **Ctrl+K / ⌘K:** komut paleti (tembel parça, CDK diyaloğu). Arama `trAramaAnahtari` (`@core/metin/tr-normalize`:
  `İş` = `iş` = `is`, `Işık` = `isik`). **CDK'yı dinamik `import('@angular/cdk/overlay')` ile ALMAYIN**: dinamik
  içe aktarılan modülün tüm dışa aktarımları canlı sayılır, `core.mjs` ve rxjs'in paylaşılan kısmı ilk pakete
  şişer (+12 kB ölçüldü). CDK tembel modülün içinde STATİK içe aktarılır, kabuk o modülü dinamik yükler.
- **Mobil (≤ 900 px):** yan menü çekmece; açıkken içerik sütunu `inert`, Esc/perde kapatır, odak menü düğmesine döner.
- **Sekmeler** (`@core/sekme` ilk pakette küçük çekirdek + `kabuk/sekmeler` tembel): her kabuk sayfası (desen +
  yol parametreleri; sorgu/fragment HARİÇ) bir sekme. Başka sekmeye geçince bileşen YOK EDİLMEZ
  (`SekmeRotaStratejisi`, `RouteReuseStrategy`): form, kaydırma, sayfa store'u yaşar. Bu yüzden
  `kaydedilmemisDegisiklikGuard` sekme DEĞİŞİMİNDE sormaz, sekme KAPATILIRKEN sorar (arka plandaki kirli sekme
  için de). En fazla 10 sekme: doluyken en uzun süredir kullanılmayan TEMİZ sekme kapanır (bilgi toast'u),
  hepsi kirliyse gezinme durur (uyarı). Tek seferlik akış `data: { sekme: false }` ile sekme dışı kalır.
- **KVKK:** `localStorage['rc.sekmeler']` = yalnız `[{ rota: '/kiralar/:id', id: '…' }]`. Sorgu, filtre, form
  değeri, müşteri adı ASLA yazılmaz; sayfanın verdiği etiket (`sekmeBaglami().etiketAyarla('Kira 2026…')`)
  yalnız bellekte. Çıkışta `OturumServisi.temizlikKaydet` ile sekmeler + arka plandaki bileşenler silinir.
- **FetchPolicy + sekme:** `aktif` verilmezse sayfanın sekmesi görünür mü (`sekmeBaglami().aktif`); arka plandaki
  sekme yüklemez, değişiklik biriktirir, öne gelince tek yükleme. `sekmeyeDonunce: 'yenile'` her dönüşte
  yeniden yükler (canlı pano, rozetli liste); form sayfası varsayılanda (`degisirse`) kalır.
- Sayfa kendi `<main>`'ini açmaz (kabukta var); sayfa başlığı `h1`. Sayfa yüksekliği `100vh` değil (kabuk üst
  çubuğu + sekme çubuğu ≈ 5 rem).
- e2e: `oturumAc` menüyü de sahteler (`e2e/ortak.ts` `MENU`); Blazor ekranı `page.route('**/<yol>')` ile sahte
  HTML. `e2e/kabuk.spec.ts` menü/etkin sayfa/Ctrl+K/390 px çekmece/sekmeler.

## Vitrin ve kontroller (F3.7)

- **Vitrin** `/app/vitrin` (dizin) → `tokenlar`, `primitifler`, `form`, `tanim`, `tablo`, `geri-bildirim`, `kabuk`.
  Yeni çekirdek parçası = vitrinde sayfa + `sayfalar.ts` satırı + dizine bağlantı + `e2e/vitrin-sayfalari.ts`
  satırı. Liste uygulamadan türetilmez; `e2e/vitrin.spec.ts` dizinin bu listeyle birebir olduğunu denetler.
- **Her vitrin sayfası (CI `e2e` işi):** açık + koyu temada axe ciddi/kritik 0 ve konsol hatası yok; 320/390/768
  (dokunmatik öykünme) ve 1440 px'te gövde yatay taşması 0 (`scripts/mobil-tasma.mjs`'in SPA karşılığı, suçlu
  elemanı raporlar); görsel regresyon açık/koyu × 320/390/768/1440, tam sayfa (`e2e/gorsel.spec.ts`).
- **Görsel tabanlar** `e2e/gorsel-tabanlari/` YALNIZ `mcr.microsoft.com/playwright:v<@playwright/test>-noble`
  imajında, linux/amd64 üretilir (macOS çizimi farklı; `gorsel` projesi Linux dışında atlanır). CI `e2e` işi
  bu imajın içinde koşar. Doğrula `npm run e2e:gorsel`, güncelle `npm run e2e:gorsel:guncelle` (ikisi de
  Docker; önce derler) → PNG farkını gözle incele → commit'le. Docker yoksa: Actions → "Görsel tabanlar"
  (elle) → artifact. Eşik `maxDiffPixelRatio` 0,005; animasyon kapalı, imleç gizli, fontlar self-host.
  Kasıtlı görünüm değişikliği yapan PR tabanları AYNI PR'da günceller. `@playwright/test` yükseltmesi =
  `ci.yml` + `gorsel-taban.yml` imaj etiketi + tabanların yeniden üretimi (etiket uyuşmazsa CI tarayıcıyı
  bulamayıp kırılır).
- **Faz çıkışı e2e:** "doğrulama hatasında form korunur" (`form.spec.ts`, `oturum.spec.ts` (a)), "oturum
  düşünce form kaybolmaz" (`oturum.spec.ts` (b)/(b2)), "`cakisma` formu silmez" (`oturum.spec.ts` (c)) —
  hepsi `npm run e2e` ile CI'da.

## Paket bütçesi

- İlk paket (production) ≈ 372,5 kB ham / 110 kB aktarım (tembel çeviriden önce 395 / 117): Angular çatısı
  ~302 kB (core 138, router 73, common+http 42, rxjs 22, transloco 14, platform-browser 13) ve uygulama çekirdeği
  ile CSS. Bütçe `angular.json`'da uyarı **392 kB** (≈ ölçüm + 20), hata **430 kB** (gerekçe dosyada yorum
  olarak). Uyarı aşılırsa eşiği yükseltmeden önce ilk pakete neyin girdiğini ölçün.
- İlk pakete yalnız her ekranda gereken çekirdek girer. Sayfa/özellik, CDK, diyaloglar, komut paleti,
  sekme makinesi, tablo motoru tembel. **İkon kaydı** (`ikon-kaydi.ts`, ~10 kB) de tembel: `<rc-ikon>` ilk
  çizimde yükler (kutu boyutu korunur), `PendingTasks`'e kayıtlı. Çekirdek `tr.json` bilinçli gömülü (~8 kB;
  ilk çizimde anahtar yanıp sönmez); özellik metinleri rota parçasıyla gelen bloklarda (yukarıdaki "i18n").
- Ölçüm: `npx ng build --stats-json` → `dist/rentacar-frontend/stats.json` (esbuild metafile).

## Kapılar

Node **22** zorunlu (`.nvmrc`, `engines`). Başka sürümde: `npx -y -p node@22 npm run <betik>`.

```bash
npm ci
npm run tipler:kontrol # API tiplerini yeniden üret; commit'lenenden farklıysa hata (kayma)
npm run format:check   # Prettier
npm run lint           # ESLint (uyarı da hata) + üretilen dosyalar güncel mi + kontrast tablosu
npm run typecheck      # ngc (strictTemplates) + spec + e2e tsc
npm test               # Vitest, @angular/build:unit-test, tek koşu
npm run build          # (önce tipler) production, dist/rentacar-frontend/browser, bütçeler 300kB / 600kB
npm run e2e            # build + Playwright (chromium projesi) /app/ altında, üretim CSP'siyle + axe + taşma
npm run e2e:gorsel     # build + görsel regresyon Docker'da (CI imajı); güncelleme: e2e:gorsel:guncelle
```

CI (`.github/workflows/ci.yml`): `frontend` işi tipler:kontrol → format:check → lint → typecheck → test →
build; `e2e` işi Playwright Linux imajının İÇİNDE `npm run e2e` + `npm run e2e:gorsel:ci` koşar (hata
durumunda `test-results/` — iz, gerçek/fark PNG'leri — artifact). Yerelde ilk seferde
`npx playwright install --only-shell chromium`.

## API tipleri (`/api/ui/v1`)

- Kaynak: `docs/api/ui-v1.json` (.NET testi `UiApiOpenApiTests` canlı OpenAPI ile birebir kilitler).
- `npm run tipler` → `src/app/core/api/uretilen/ui-v1.ts` (openapi-typescript, sabit sürüm; Prettier'dan
  geçer). **Üretilen dosya commit'lenir ve elle düzenlenmez**; ESLint onu yok sayar. `prebuild`/`prewatch`
  her derlemede yeniden üretir.
- Kod tipleri **doğrudan üretilen dosyadan değil** `@core/api/ui-tipleri`'nden alır (`BenYaniti`,
  `MenuYaniti`…). Özellik kendi takma adlarını kendi klasöründe `Sema<'KiraDetayYaniti'>` ile kurar
  (ör. `features/kira-formu/kira-tipleri.ts`). API'de alan adı/tipi değişirse: .NET testi JSON'u güncelletir → `npm run tipler` →
  kullanan kod `typecheck`'te kırılır. JSON değişip tipler üretilmezse CI `tipler:kontrol` kırmızı.
- API değiştiğinde akış: `RACAR_OPENAPI_GUNCELLE=1 dotnet test --filter UiApiOpenApiTests` →
  `npm run tipler` → ikisini birlikte commit'le.

## Geliştirme akışı (Web uygulaması içinde, `:5220`)

```bash
# 1. terminal (src/RentACar.Frontend): development derlemesi, değişiklikte yeniden yazar
npm run watch
# 2. terminal (repo kökü): Development ortamı Spa:Dizin'i bu derlemeye çevirir
ASPNETCORE_URLS=http://localhost:5220 dotnet run --project src/RentACar.Web
# → http://localhost:5220/app/  (gerçek CSP, gerçek cookie/XSRF, /api/ui aynı origin)
```

- Dizin ayarı `src/RentACar.Web/appsettings.Development.json` içinde (content root'a göreli):
  `"Spa": { "Dizin": "../RentACar.Frontend/dist/rentacar-frontend/browser" }`. Üretimde bu ayar yok;
  varsayılan `../app/browser` (release'in kendi SPA'sı).
- Değişiklikten sonra tarayıcıyı yenile (canlı yeniden yükleme yok; kabuk `no-cache`, dev çıktısı
  hash'siz). `npm start` (`:4200`) yalnız saf arayüz işi içindir: `/api/ui` orada yok.
- Web açılışta `Yeni arayüz /app altında sunuluyor: …` loglar; dizin yoksa `/app` 404 döner (önce
  `npm run watch`).

## Dağıtım

Sunucuda Node yok. `main`'e her push'ta (tüm CI kapıları yeşilse) `spa-surum` işi production derlemesini
`spa-<sha>.tar.gz` + `.sha256` + `chunks.txt` olarak `spa-<sha>` GitHub release'ine yükler;
`deploy/yayinla.sh` checkout edilen SHA'nınkini salt-okur token ile indirip doğrular. Ayrıntı:
`docs/ops/deploy-checklist.md` §10, sunucu adımları `docs/ops/f2-2-sunucu-adimlari.md`.

## Bilinçli kararlar

- **Sürümler sabit** (`^`/`~` yok). Güncelleme ayrı PR, `package-lock.json` ile birlikte.
- **Tek tolere edilen deneysel araç:** `@angular/build:unit-test` (Vitest). Sürümü sabit. Signal Forms,
  `resource()`, `@angular/aria` gibi deneysel çalışma zamanı API'leri kullanılmaz.
- **Husky yok, `core.hooksPath` değiştirilmez:** repodaki yerel `.git/hooks` graphify'ı tazeler;
  `hooksPath` onları devre dışı bırakır. Kapılar CI'da zorlanır, yerelde betiklerle koşulur.
- Frontend `RentACar.slnx`'e eklenmez; `dotnet build/test` onu görmez.
