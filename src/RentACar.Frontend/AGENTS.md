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
  bağlam `OTURUM_BAGLAMI` (F3.3'e kadar yer tutucu).
- **Liste sorgusu:** `listeTanimi({ filtreler, siralanabilir, varsayilanSirala })` + sayfada
  `listeSorgusuUrlSenkronu(tanim)`. URL tek doğruluk kaynağı; bozuk parametre varsayılana düşer,
  `boyut` 1..200, `sirala` beyaz listeden. Yazarken `{ yaziyor: true }` (replaceUrl).
- Katlanır filtre: `<rc-katlanir-filtre>` (`@shared/katlanir-filtre`), stilsiz.

## Kapılar

Node **22** zorunlu (`.nvmrc`, `engines`). Başka sürümde: `npx -y -p node@22 npm run <betik>`.

```bash
npm ci
npm run format:check   # Prettier
npm run lint           # ESLint (uyarı da hata) + üretilen dosyalar güncel mi + kontrast tablosu
npm run typecheck      # ngc (strictTemplates) + spec + e2e tsc
npm test               # Vitest, @angular/build:unit-test, tek koşu
npm run build          # production, dist/rentacar-frontend/browser, bütçeler 300kB uyarı / 600kB hata
npm run e2e            # build + Playwright duman testi /app/ altında, üretim CSP'siyle + axe
```

CI (`.github/workflows/ci.yml` → `frontend` işi) format:check → lint → typecheck → test → build koşar.
e2e yalnız yerelde (ilk seferde `npx playwright install chromium`); CI e2e işi F2.2'de.

## Bilinçli kararlar

- **Sürümler sabit** (`^`/`~` yok). Güncelleme ayrı PR, `package-lock.json` ile birlikte.
- **Tek tolere edilen deneysel araç:** `@angular/build:unit-test` (Vitest). Sürümü sabit. Signal Forms,
  `resource()`, `@angular/aria` gibi deneysel çalışma zamanı API'leri kullanılmaz.
- **Husky yok, `core.hooksPath` değiştirilmez:** repodaki yerel `.git/hooks` graphify'ı tazeler;
  `hooksPath` onları devre dışı bırakır. Kapılar CI'da zorlanır, yerelde betiklerle koşulur.
- Frontend `RentACar.slnx`'e eklenmez; `dotnet build/test` onu görmez.
