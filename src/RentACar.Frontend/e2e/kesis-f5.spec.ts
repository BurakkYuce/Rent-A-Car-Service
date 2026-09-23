import { expect, test, type Page, type Route } from '@playwright/test';

import { oturumAc, problem } from './ortak';
import { ortakUclar } from './planlama-sahte';
import { sahteRezervasyonApi } from './rezervasyon-sahte';

/**
 * F5.4 rezervasyon kesişi (sahte `/api/ui/v1`, üretim derlemesi + CSP). Harness yalnız statik SPA sunar;
 * Blazor sunucusunun 302'si (`IlkKesisMiddleware`, backend `IlkKesisTests` birebir Location'ı kilitler)
 * Playwright ile taklit edilir: yol haritadan, sorgu AYNEN, fragment YOK (tarayıcı özgününü korur).
 */
async function sunucuYonlendirmesi(page: Page, kaynak: string, hedef: string): Promise<void> {
  await page.route(
    (url) => url.pathname === kaynak,
    (route: Route) => {
      const url = new URL(route.request().url());
      return route.fulfill({ status: 302, headers: { Location: hedef + url.search } });
    },
  );
}

/** Sahtesi olmayan okuma uçları 404 problem döner (sayfa başlığı veriden bağımsız çizilir). */
async function kalanUclar(page: Page): Promise<void> {
  await page.route('**/api/ui/v1/**', (route) => problem(route, 404, 'bulunamadi', 'Yok.'));
}

const SAYFALAR = [
  { blazor: '/rezervasyonlar', spa: '/app/rezervasyonlar', baslik: 'Rezervasyonlar' },
  { blazor: '/teklifler', spa: '/app/teklifler', baslik: 'Teklifler' },
  { blazor: '/takvim', spa: '/app/takvim', baslik: 'Rezervasyon Takvimi' },
  { blazor: '/musaitlik', spa: '/app/musaitlik', baslik: 'Müsait Araç Ara' },
  {
    blazor: '/rez-sartlari',
    spa: '/app/rez-sartlari',
    baslik: 'Rez Şartları (Müşteri Özel Talepleri)',
  },
  { blazor: '/filo-kiralama', spa: '/app/filo-kiralama', baslik: 'Filo / Uzun Dönem Kiralama' },
] as const;

for (const s of SAYFALAR) {
  test(`pilot: eski ${s.blazor} adresi → ${s.spa} (sorgu AYNEN, fragment korunur, SPA sayfası açılır)`, async ({
    page,
  }) => {
    await kalanUclar(page); // Playwright'ta SON kaydedilen önce eşleşir: oturum + özel sahteler bunu ezer
    await oturumAc(page);
    await sahteRezervasyonApi(page);
    await ortakUclar(page);
    await sunucuYonlendirmesi(page, s.blazor, s.spa);

    await page.goto(`${s.blazor}?bilgi=x#iz`);
    await expect(page).toHaveURL(
      (url) => url.pathname === s.spa && url.search === '?bilgi=x' && url.hash === '#iz',
    );
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(s.baslik);
  });
}

test('pilot: F5 ekranlarının sayfa içeriğindeki hiçbir bağlantı Blazor F5 sayfasına düşmez', async ({
  page,
}) => {
  await kalanUclar(page);
  await oturumAc(page);
  await sahteRezervasyonApi(page);
  await ortakUclar(page);

  for (const s of SAYFALAR) {
    await page.goto(s.spa);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(s.baslik);
    const dusen = await page.locator('main a[href]').evaluateAll((ogeler) =>
      ogeler
        .map((o) => new URL((o as HTMLAnchorElement).href, location.href))
        .filter((u) => u.origin === location.origin)
        .map((u) => u.pathname.replace(/\/$/, ''))
        .filter((yol) =>
          /^\/(rezervasyonlar|teklifler|takvim|musaitlik|rez-sartlari|filo-kiralama)$/i.test(yol),
        ),
    );
    expect(dusen, s.spa).toEqual([]);
  }
});
