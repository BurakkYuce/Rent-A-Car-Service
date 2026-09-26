import { expect, type Page } from '@playwright/test';

/**
 * F3.7 kapısının sayfa listesi: vitrin + kabuk ana sayfası. Liste uygulama kodundan TÜRETİLMEZ
 * (bağımsız oracle): dizin sayfası bu listedeki her vitrine bağlanmak zorunda (`vitrin.spec.ts`).
 * Yeni çekirdek parçası = vitrinde sayfa + bu listeye satır → axe (iki tema), taşma (320–1440) ve
 * görsel regresyon (`gorsel.spec.ts`) kendiliğinden kapsar.
 */
export interface VitrinSayfasi {
  /** Görsel taban dosya adı öneki (kalıcı: değişirse tabanlar yeniden üretilir). */
  readonly ad: string;
  readonly yol: string;
  readonly baslik: string;
  /** Sayfanın açılışta çağırdığı uçların sahtesi (gerekirse). */
  readonly hazirla?: (page: Page) => Promise<unknown>;
  /** Veri geldi mi (varsayılan: h1 + ortak bekleme yeter). */
  readonly hazir?: (page: Page) => Promise<void>;
}

const LAYOUT_ENDPOINT = '**/api/ui/v1/tablo-duzenleri/**';

export const SHOWCASE_PAGES: readonly VitrinSayfasi[] = [
  { ad: 'ana-sayfa', yol: '/app/', baslik: 'Yeni arayüz yapım aşamasında' },
  { ad: 'dizin', yol: '/app/vitrin', baslik: 'Vitrin' },
  { ad: 'tokenlar', yol: '/app/vitrin/tokenlar', baslik: 'Token’lar' },
  { ad: 'primitifler', yol: '/app/vitrin/primitifler', baslik: 'Primitifler' },
  { ad: 'form', yol: '/app/vitrin/form', baslik: 'Form vitrini' },
  {
    ad: 'tanim',
    yol: '/app/vitrin/tanim',
    baslik: 'Tanım vitrini',
    hazir: (page) => expect(page.getByRole('cell', { name: 'Beyaz' })).toBeVisible(),
  },
  {
    ad: 'tablo',
    yol: '/app/vitrin/tablo',
    baslik: 'Tablo vitrini',
    hazirla: (page) =>
      page.route(LAYOUT_ENDPOINT, (route) =>
        route.fulfill({
          json: { tabloKodu: 'vitrin.araclar', duzen: null, guncellemeUtc: null },
        }),
      ),
    hazir: async (page) => {
      const grid = page.getByRole('grid', { name: 'Araç listesi' });
      await expect(grid).not.toHaveAttribute('aria-busy', 'true');
      await expect(grid.locator('[data-hucre="1:0"]')).toBeVisible();
    },
  },
  { ad: 'geri-bildirim', yol: '/app/vitrin/geri-bildirim', baslik: 'Geri bildirim vitrini' },
  { ad: 'kabuk', yol: '/app/vitrin/kabuk', baslik: 'Kabuk' },
];

/** Sayfa çizimi bitti: başlık, sayfa verisi, fontlar, (tembel) ikon kaydı; yükleniyor işareti yok. */
export async function waitReady(page: Page, pageRef: VitrinSayfasi): Promise<void> {
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(pageRef.baslik);
  await pageRef.hazir?.(page);
  await page.waitForFunction(
    () =>
      [...document.querySelectorAll('rc-ikon')].every((i) => i.querySelector('svg') !== null) &&
      document.fonts.status === 'loaded',
  );
  await page.evaluate(() => document.fonts.ready.then(() => undefined));
  // İki kare: sinyal güncellemesi + düzen (viewport değişiminden sonra kabuk ≤ 900 px çekmeceye döner).
  await page.evaluate(
    () => new Promise<void>((r) => requestAnimationFrame(() => requestAnimationFrame(() => r()))),
  );
}

/**
 * Yatay taşma (scripts/mobil-tasma.mjs'in SPA karşılığı): `scrollWidth > clientWidth`. Suçlu, SAĞ
 * KENARI viewport'u aşan en dıştaki elemanlar (genişliği değil konumu: dar eleman da taşırabilir).
 */
export async function measureOverflow(page: Page): Promise<{ tasma: number; suclular: string[] }> {
  return page.evaluate(() => {
    const de = document.documentElement;
    const width = de.clientWidth;
    const overflow = de.scrollWidth - width;
    if (overflow <= 0) return { tasma: 0, suclular: [] };
    const identity = (e: Element) => {
      const cssClass =
        typeof e.className === 'string' && e.className.trim()
          ? '.' + e.className.trim().split(/\s+/).slice(0, 2).join('.')
          : '';
      return e.tagName.toLocaleLowerCase('en') + cssClass;
    };
    const culprits = [...document.querySelectorAll('body *')]
      .map((e) => ({ e, sag: Math.round(e.getBoundingClientRect().right) }))
      .filter((x) => x.sag > width + 1)
      .sort((a, b) => b.sag - a.sag)
      .slice(0, 3)
      .map((x) => `${identity(x.e)} (sağ kenar ${x.sag}px)`);
    return { tasma: overflow, suclular: culprits };
  });
}
