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
  göreli URL'lere eklenir; CSP `connect-src 'self'`.
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
- **i18n:** Transloco, yalnız `src/i18n/tr.json` (pakete gömülü). Şablonda `'anahtar' | transloco`,
  TS'te tipli `ceviriFonksiyonu()`. `tr.json` değişince `npm run i18n:tipler`.
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
- Katlanır filtre: `<rc-katlanir-filtre>` (`@shared/katlanir-filtre`), stilsiz.

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
  (`number`), `rc-para-girdisi` (değer invariant METİN `"1234.56"`, görüntü `1.234,56`, yarım kuruş
  sıfırdan uzağa; JSON sayısı da yazılabilir), `rc-secim` (yerel select), `rc-arama-secim` (CDK
  overlay + listbox; kaynak `sunucuSecimKaynagi('musteri')` → `/api/ui/v1/secim/*`, `limit ≤ 20`,
  gecikmeli, değer seçilen öğe `{ id, etiket, … }`), `rc-onay-kutusu`, `rc-anahtar`, `rc-radyo-grubu`,
  `rc-tarih-secici` (değer takvim günü `"2026-09-22"`, `aralik` ile `{ baslangic, bitis }` + hazır
  aralıklar), `rc-tarih-saat-secici` (değer UTC anı; İstanbul saatiyle gösterilir). Tarih günü `Date`
  / `toISOString` yoluna SOKULMAZ; an yerel saate çevrilip UTC sayılmaz.
- **Alan:** her kontrol `<rc-alan etiket="…" ipucu="…">` içinde: etiket `for`, zorunlu `*` (doğrulayıcıdan),
  hata yuvası, `aria-invalid`/`aria-describedby`/`aria-required`. Radyo grubunda `grup`.
- **Gönderim yalnız `formGonderimi()`** (`@shared/form/form-gonderimi`): çift tık tek istek, istemci
  doğrulaması geçmezse istek gitmez, `Idempotency-Key` kuralı `GonderimKilidi`'nde (mantıksal gönderim
  başına anahtar, yeniden denemede aynı, her 2xx ve `mukerrer` sonrası yeni; deterministik sunucu
  anahtarı `deterministikAnahtar` ile dokunulmadan önce gelir), hata alanlara (`alanlar`), değerler
  korunur, 2xx'te form `pristine`.
- **Kaydedilmemiş değişiklik:** sayfa `KaydedilmemisDegisiklikSahibi` uygular, rotaya
  `canDeactivate: [kaydedilmemisDegisiklikGuard]`, kurucuda `sayfaTerkKorumasi(() => form.dirty)`.
- **Yerleşim:** `rc-sekmeli-form` + `rcSekmePaneli` (derin bağlantı `#sekme=…`, gizli sekmedeki hatalı
  alana geçip odaklanır: `ilkGecersizeGit()`) + `rcYanPanel` (sabit yan panel); `rc-tanim-crud`
  (`TanimAlani[]` + `TanimKaynagi`, REST için `restTanimKaynagi('/api/ui/v1/…')`); form ızgarası
  `.rc-form-izgara`. Yazdırma: `_yazdir.scss` (gizli sekmeler başlığıyla basılır, `.rc-yazdirma-gizle`).
- Vitrin: `/app/vitrin/form`, `/app/vitrin/tanim` (e2e bunların üstünde).

## Tablo motoru (F3.5)

- **`<rc-tablo>`** (`@shared/tablo/tablo`), TanStack `table-core` + `virtual-core` doğrudan (bağdaştırıcı
  yok, sürümler sabit). Rota ile TEMBEL yüklenen sayfada kullanılır (ilk pakete girmez). Örnek kablolama:
  `features/vitrin/tablo-vitrini` (`/app/vitrin/tablo`, 49 sütun × 5.000 kayıt).
- Girdiler: `etiket`, `sutunlar: TabloSutunu<T>[]` (`kod` kalıcı; `tur: para|sayi|tarih|tarihSaat|metin`,
  `sirala: true | 'apiAlani'`, `sabit`, `gizli`, `gizlenemez`, `genislik`), `kaynak` = `store.liste.durum()`
  (TemelStore; `Sayfa<T>` → sunucu sayfalaması), `satirKimligi`, `tabloKodu` (kullanıcı düzeni),
  `sirala`/`varsayilanSirala` (F3.4 sorgusu), `secilebilir` + `[(secim)]`, `disaAktarma`.
  Çıktılar: `siralaDegisti`/`sayfaDegisti`/`boyutDegisti` → `liste.degistir({...})`, `satirAc`, `yenidenDene`.
- Özel hücre: `<ng-template rcTabloHucre="kod" [rcTabloHucreSutunlar]="sutunlar" let-satir>`.
- Dört durum ayrı: `bos` mesajsız, `yukleniyor` iskelet/soluk önceki veri + `aria-busy`, `hata` bandı +
  yeniden dene (ASLA "kayıt yok" değil), "Kayıt bulunamadı" yalnız başarılı sıfır kayıtta.
- Kullanıcı düzeni (sıra/görünürlük/genişlik/sıralama) `GET/PUT/DELETE /api/ui/v1/tablo-duzenleri/{kod}`;
  bayat/bozuk kayıt tanımla uzlaşır (bilinmeyen kod düşer, yeni sütun tanımdaki yerine girer).
- Klavye (APG Data Grid): oklar, Home/End, Ctrl+Home/End, PageUp/Down; satırda Enter açar, Boşluk
  seçer; başlıkta Enter sıralar, Alt+←/→ genişlik, Alt+Shift+←/→ sıra. Roving tabindex.
- Dışa aktarma yalnız sunucu uçlarıyla (`/listeler/export/*`, `/raporlar/export/*`); istemcide dosya üretilmez.

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
npm run e2e            # build + Playwright duman testi /app/ altında, üretim CSP'siyle + axe
```

CI (`.github/workflows/ci.yml`): `frontend` işi tipler:kontrol → format:check → lint → typecheck → test →
build; `e2e` işi Playwright duman testini koşar (Chromium önbellekli). Yerelde ilk seferde
`npx playwright install --only-shell chromium`.

## API tipleri (`/api/ui/v1`)

- Kaynak: `docs/api/ui-v1.json` (.NET testi `UiApiOpenApiTests` canlı OpenAPI ile birebir kilitler).
- `npm run tipler` → `src/app/core/api/uretilen/ui-v1.ts` (openapi-typescript, sabit sürüm; Prettier'dan
  geçer). **Üretilen dosya commit'lenir ve elle düzenlenmez**; ESLint onu yok sayar. `prebuild`/`prewatch`
  her derlemede yeniden üretir.
- Kod tipleri **doğrudan üretilen dosyadan değil** `@core/api/ui-tipleri`'nden alır (`BenYaniti`,
  `MenuYaniti`…). API'de alan adı/tipi değişirse: .NET testi JSON'u güncelletir → `npm run tipler` →
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
