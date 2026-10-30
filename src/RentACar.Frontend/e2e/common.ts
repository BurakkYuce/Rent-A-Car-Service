import AxeBuilder from '@axe-core/playwright';
import type { Page, Request, Route } from '@playwright/test';

/** Konsol hatalarını (CSP ihlali dahil) ve çalışma zamanı hatalarını toplar. */
export function collectErrors(page: Page, expected: RegExp[] = []): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() !== 'error') return;
    const text = message.text();
    if (!expected.some((k) => k.test(text))) errors.push(text);
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

/**
 * axe DURAN hâli ölçer: süren sonlu animasyon/geçişler (ör. düğmenin pasif→etkin 150 ms renk geçişi) bitmeden
 * koşarsa ara kareyi ölçüp yanlış color-contrast verir (ölçüldü: `.rc-dugme--tehlike` onay kutusu işaretlenince
 * 3,14:1 ara renk). Sonsuz animasyonlar (iskelet, döner simge) beklenmez.
 */
async function settleTransitions(page: Page): Promise<void> {
  const settled = page
    .evaluate(
      (limitMs) =>
        Promise.race([
          Promise.all(
            document
              .getAnimations()
              // Yalnız ŞU AN koşan ve bitebilecek olanlar; duraklatılmış/sonsuz/yeniden başlayan beklenmez.
              .filter(
                (a) =>
                  a.playState === 'running' &&
                  Number.isFinite(a.effect?.getComputedTiming().endTime ?? Infinity),
              )
              .map((a) => a.finished.catch(() => undefined)),
          ).then(() => true),
          new Promise<boolean>((r) => setTimeout(() => r(false), limitMs)),
        ]),
      SETTLE_LIMIT_MS,
    )
    .catch(() => true);
  // SINIR iki yanlı: sayfa zamanlayıcısı/zaman çizelgesi ilerlemese de (CI #382: platform ve tarife-matris
  // `page.evaluate` 30 sn asılı kaldı) test tarafı en çok sınır kadar bekler, sonra axe yine koşar.
  const done = await Promise.race([
    settled,
    new Promise<boolean>((r) => setTimeout(() => r(false), SETTLE_LIMIT_MS + 500)),
  ]);
  if (!done) console.warn('[axe] geçişler sınırda bitmedi:', await runningAnimations(page));
}

/** Tanı: hâlâ koşan animasyonların kimliği (CI'da takılanı görmek için; sayfa yanıt vermezse boş). */
async function runningAnimations(page: Page): Promise<string[]> {
  const read = page
    .evaluate(() =>
      document
        .getAnimations()
        .filter((a) => a.playState === 'running')
        .slice(0, 10)
        .map((a) => {
          const target = (a.effect as KeyframeEffect | null)?.target;
          const name =
            a instanceof CSSTransition
              ? `transition:${a.transitionProperty}`
              : a instanceof CSSAnimation
                ? `animation:${a.animationName}`
                : a.constructor.name;
          return `${name} t=${Math.round(Number(a.currentTime))} ${target?.tagName ?? ''}.${target?.className ?? ''}`;
        }),
    )
    .catch(() => []);
  return Promise.race([read, new Promise<string[]>((r) => setTimeout(() => r([]), 500))]);
}

/** Geçiş bekleme üst sınırı (uygulamadaki en uzun sonlu geçiş 150 ms). */
const SETTLE_LIMIT_MS = 1000;

export async function seriousViolations(page: Page, scope?: string): Promise<string[]> {
  await settleTransitions(page);
  const axe = new AxeBuilder({ page });
  if (scope) axe.include(scope);
  const result = await axe.analyze();
  return result.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map(
      (violation) =>
        `${violation.id}: ${violation.help} → ` +
        violation.nodes.map((n) => `${n.target.join(' ')} (${n.any[0]?.message ?? ''})`).join('; '),
    );
}

/**
 * Sahte `/api/ui/v1` (e2e harness'i yalnız statik SPA sunar; F2.2 e2e işi backend kaldırmaz).
 * Yanıtlar backend `UiHata` sözleşmesinin birebir biçimi: `application/problem+json` + `kod`.
 */
export const BEN = {
  kullanici: {
    id: '6f1c2c8e-0000-4000-8000-000000000001',
    kullaniciAdi: 'ayse',
    adSoyad: 'Ayşe Yılmaz',
  },
  kiraci: { id: '6f1c2c8e-0000-4000-8000-0000000000aa', kod: 'pilot', ad: 'Pilot Firma' },
  rol: 'Admin',
  izinler: ['OperationsWrite', 'FinanceWrite', 'ViewReports'],
  subeKapsami: { tumSubeler: true, subeId: null, subeAd: null },
  moduller: { webSitesi: false },
  renkler: {},
  pilot: true,
};

export function problem(
  route: Route,
  status: number,
  code: string,
  detail: string,
  extra: object = {},
) {
  return route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({
      type: 'about:blank',
      title: 'Hata',
      status,
      detail,
      kod: code,
      ...extra,
    }),
  });
}

/** XSRF çerezini sunucu gibi yazar (SPA `document.cookie`'den okur, başlığa koyar). */
export async function writeXsrf(page: Page, value: string): Promise<void> {
  const url = new URL(page.url() === 'about:blank' ? 'http://127.0.0.1' : page.url());
  await page
    .context()
    .addCookies([{ name: 'XSRF-TOKEN', value: value, domain: url.hostname, path: '/' }]);
}

/**
 * Sahte `GET /api/ui/v1/menu` (sunucu izne göre süzmüş biçimde; Finans yok). Vitrin öğeleri `spa`,
 * diğerleri Blazor (tam sayfa). Etiketler sekme başlıklarından farklı: e2e seçicileri karışmasın.
 */
export const MENU = {
  ogeler: [
    {
      rota: '/rezervasyonlar',
      etiket: 'Yeni Rezervasyon',
      grup: 'Kısa Yollar',
      sira: 10,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: true,
    },
    {
      rota: '/',
      etiket: 'Panel',
      grup: '',
      sira: 20,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/vitrin/geri-bildirim',
      etiket: 'Geri bildirim',
      grup: 'Vitrin',
      sira: 30,
      sahip: 'spa',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/vitrin/form',
      etiket: 'Form seti',
      grup: 'Vitrin',
      sira: 40,
      sahip: 'spa',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/vitrin/tanim',
      etiket: 'Tanım CRUD',
      grup: 'Vitrin',
      sira: 50,
      sahip: 'spa',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/kiralar',
      etiket: 'Kiralar',
      grup: 'Kira',
      sira: 60,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/is-emirleri',
      etiket: 'İş Emirleri',
      grup: 'Servis',
      sira: 70,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/bildirimler',
      etiket: 'Bildirimler',
      grup: '',
      sira: 80,
      sahip: 'blazor',
      rozetKodu: 'okunmamis-bildirim',
      hizliBaglanti: false,
    },
  ],
  rozetler: { 'okunmamis-bildirim': 4 },
};

export async function fakeMenu(page: Page, menu: object = MENU): Promise<void> {
  await page.route('**/api/ui/v1/menu', (route) => route.fulfill({ json: menu }));
}

/** Oturum açık: `ben` 200 döner, XSRF çerezi yazılı, kabuk menüsü sahte. */
export async function logIn(page: Page, ben: object = BEN): Promise<void> {
  await writeXsrf(page, 'eski-belirtec');
  await page.route('**/api/ui/v1/oturum/ben', (route) => route.fulfill({ json: ben }));
  await fakeMenu(page);
}

export interface KayitliIstek {
  readonly govde: string | null;
  readonly xsrf: string | undefined;
  readonly anahtar: string | undefined;
}

export function kaydet(request: Request): KayitliIstek {
  const h = request.headers();
  return { govde: request.postData(), xsrf: h['x-xsrf-token'], anahtar: h['idempotency-key'] };
}
