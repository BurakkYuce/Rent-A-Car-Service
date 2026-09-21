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
npm run lint           # ESLint 9 flat config, uyarı da hata (--max-warnings 0)
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
