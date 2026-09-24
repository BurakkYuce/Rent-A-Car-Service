import { expect, test, type Page, type Route } from '@playwright/test';

import { BEN, oturumAc, problem } from './ortak';
import { financeEndpoints } from './vehicle-finance-fakes';
import { VEHICLE_1, vehicleEndpoints } from './vehicle-fakes';

/**
 * F6.4 araç kesişi (sahte `/api/ui/v1`, üretim derlemesi + CSP). Harness yalnız statik SPA sunar; Blazor
 * sunucusunun 302'si (`IlkKesisMiddleware`, backend `IlkKesisTests` birebir Location'ı kilitler) Playwright ile
 * taklit edilir: yol haritadan, sorgu AYNEN, fragment YOK (tarayıcı özgününü korur). Dört şablonun SPA adı
 * farklıdır (`/vehicles` → `/app/araclar`, `/araclar/{id}` → `/app/araclar/{id}/detay` …).
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

/** Sahtesi olmayan okuma uçları 404 problem döner (Playwright'ta SON kaydedilen önce eşleşir). */
async function remainingEndpoints(page: Page): Promise<void> {
  await page.route('**/api/ui/v1/**', (route) => problem(route, 404, 'bulunamadi', 'Yok.'));
}

async function fakes(page: Page): Promise<void> {
  await remainingEndpoints(page);
  await oturumAc(page, { ...BEN, izinler: [...BEN.izinler, 'OperationsDelete'] });
  await financeEndpoints(page);
  await vehicleEndpoints(page);
}

const PAGES = [
  { blazor: '/vehicles', spa: '/app/araclar', heading: 'Araç Listesi' },
  { blazor: '/vehicles/detayli', spa: '/app/araclar/detayli', heading: 'Detaylı Araç Listesi' },
  {
    blazor: `/vehicles/${VEHICLE_1}`,
    spa: `/app/araclar/${VEHICLE_1}`,
    heading: '34ABC123 — Araç Düzenle',
  },
  {
    blazor: `/araclar/${VEHICLE_1}`,
    spa: `/app/araclar/${VEHICLE_1}/detay`,
    heading: '34ABC123 Fiat',
  },
  { blazor: '/arac-durum', spa: '/app/arac-durum', heading: 'Araç Güncel Durum' },
  { blazor: '/arac-sahipleri', spa: '/app/arac-sahipleri', heading: 'Araç Sahip Tanımları' },
  { blazor: '/segmentler', spa: '/app/segmentler', heading: 'Araç Segment Tanımları' },
  { blazor: '/arac-tipleri', spa: '/app/arac-tipleri', heading: 'Araç Tip Tanımları' },
  { blazor: '/arac-kredi', spa: '/app/arac-kredi', heading: 'Araç Kredisi Takip' },
  { blazor: '/musteri-taksit', spa: '/app/musteri-taksit', heading: 'Müşteri Taksit Takibi' },
  { blazor: '/arac-siparis', spa: '/app/arac-siparis', heading: 'Araç Sipariş / Tedarik' },
  { blazor: '/baf', spa: '/app/baf', heading: 'BAF — Personel Araç Tahsis' },
  { blazor: '/hasar', spa: '/app/hasar', heading: 'Hasar Dosyaları' },
  { blazor: '/filo-plan', spa: '/app/filo-plan', heading: 'Filo Plan Yönetimi' },
] as const;

for (const p of PAGES) {
  test(`pilot: eski ${p.blazor} adresi → ${p.spa} (sorgu AYNEN, fragment korunur, SPA sayfası açılır)`, async ({
    page,
  }) => {
    await fakes(page);
    await serverRedirect(page, p.blazor, p.spa);

    await page.goto(`${p.blazor}?bilgi=x#iz`);
    await expect(page).toHaveURL(
      (url) => url.pathname === p.spa && url.search === '?bilgi=x' && url.hash === '#iz',
    );
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(p.heading);
  });
}

/** Blazor F6 sayfa yolları (SPA'nın `/app/...` bağlantıları buna uymaz; uyan bağlantı eski arayüze düşer). */
const BLAZOR_F6 =
  /^\/(vehicles|vehicles\/detayli|vehicles\/[0-9a-f-]{36}|araclar\/[0-9a-f-]{36}|arac-durum|arac-sahipleri|segmentler|arac-tipleri|arac-kredi|musteri-taksit|arac-siparis|baf|hasar|filo-plan)$/i;

test('pilot: F6 ekranlarının sayfa içeriğindeki hiçbir bağlantı Blazor F6 sayfasına düşmez', async ({
  page,
}) => {
  await fakes(page);

  for (const p of PAGES) {
    await page.goto(p.spa);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(p.heading);
    const fallen = await page.locator('main a[href]').evaluateAll(
      (links, pattern) =>
        links
          .map((a) => new URL((a as HTMLAnchorElement).href, location.href))
          .filter((u) => u.origin === location.origin)
          .map((u) => u.pathname.replace(/\/$/, ''))
          .filter((path) => new RegExp(pattern, 'i').test(path)),
      BLAZOR_F6.source,
    );
    expect(fallen, p.spa).toEqual([]);
  }
});
