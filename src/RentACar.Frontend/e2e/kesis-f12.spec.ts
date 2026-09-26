import { expect, test, type Page, type Route } from '@playwright/test';

import { fakePlatformApi, TENANT_A } from './platform-fakes';

/**
 * F12 platform konsolu kesişi (sahte `/api/ui/v1/platform`, üretim derlemesi + CSP). Harness yalnız statik SPA sunar;
 * Blazor sunucusunun 302'si (`CutoverMiddleware`, backend `IlkKesisTests` birebir Location'ı kilitler) Playwright ile
 * taklit edilir: yol haritadan, sorgu AYNEN, fragment YOK (tarayıcı özgününü korur). Platform bir kiracı değildir;
 * yönlendirme pilotsuz, her oturumda.
 */
async function serverRedirect(page: Page, source: string, target: string): Promise<void> {
  await page.route(
    (url) => url.pathname === source,
    (route: Route) => {
      const url = new URL(route.request().url());
      return route.fulfill({ status: 302, headers: { Location: target + url.search } });
    },
  );
}

const PAGES: readonly (readonly [source: string, target: string, heading: string | RegExp])[] = [
  ['/platform/tenants', '/app/platform/kiracilar', 'Firma Konsolu'],
  [`/platform/tenants/${TENANT_A}`, `/app/platform/kiracilar/${TENANT_A}`, /Yüce Rent A Car/],
  ['/platform/belgeler', '/app/platform/belgeler', 'Belge Merkezi'],
];

test('envanter: 4 sayfa (F12.md tablosu; giriş dahil)', () => {
  expect(PAGES.length + 1).toBe(4);
});

for (const [source, target, heading] of PAGES) {
  test(`oturumlu operatör: eski ${source} → ${target} (sorgu AYNEN, fragment korunur)`, async ({
    page,
  }) => {
    await fakePlatformApi(page, { signedIn: true });
    await serverRedirect(page, source, target);

    await page.goto(`${source}?ok=1#iz`);
    await expect(page).toHaveURL(
      (url) => url.pathname === target && url.search === '?ok=1' && url.hash === '#iz',
    );
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(heading);
  });
}

test('oturumsuz: eski /platform/login → /app/platform/giris; giriş sonrası özet açılır', async ({
  page,
}) => {
  await fakePlatformApi(page);
  await serverRedirect(page, '/platform/login', '/app/platform/giris');

  await page.goto('/platform/login');
  await expect(page).toHaveURL((url) => url.pathname === '/app/platform/giris');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Platform Girişi');
});

/** Blazor platform sayfa yolları (SPA'nın `/app/platform/...` bağlantıları buna uymaz). */
const BLAZOR_F12 = /^\/platform(\/(login|tenants(\/[0-9a-f-]{36})?|belgeler))?$/i;

test('konsol ekranlarındaki hiçbir bağlantı Blazor platform sayfasına düşmez', async ({ page }) => {
  await fakePlatformApi(page, { signedIn: true });
  for (const path of [
    '/app/platform',
    '/app/platform/kiracilar',
    `/app/platform/kiracilar/${TENANT_A}`,
    '/app/platform/belgeler',
  ]) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    const fallen = await page.locator('a[href]').evaluateAll(
      (links, pattern) =>
        links
          .map((a) => new URL((a as HTMLAnchorElement).href, location.href))
          .filter((u) => u.origin === location.origin)
          .map((u) => u.pathname.replace(/\/$/, ''))
          .filter((p) => new RegExp(pattern, 'i').test(p)),
      BLAZOR_F12.source,
    );
    expect(fallen, path).toEqual([]);
  }
});
