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

## Kapılar

Node **22** zorunlu (`.nvmrc`, `engines`). Başka sürümde: `npx -y -p node@22 npm run <betik>`.

```bash
npm ci
npm run tipler:kontrol # API tiplerini yeniden üret; commit'lenenden farklıysa hata (kayma)
npm run format:check   # Prettier
npm run lint           # ESLint 9 flat config, uyarı da hata (--max-warnings 0)
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
