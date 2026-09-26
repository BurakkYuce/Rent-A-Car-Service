import { expect, test, type Page, type Route } from '@playwright/test';

import { CARI_1, customerCrmEndpoints } from './customers-crm-fakes';
import { KIRA_ID, MUSTERI_ID, sahteKiraApi } from './kira-sahte';
import { BEN, oturumAc, problem } from './ortak';

/**
 * F7.3 cari/CRM kesişi (sahte `/api/ui/v1`, üretim derlemesi + CSP). Harness yalnız statik SPA sunar; Blazor
 * sunucusunun 302'si (`IlkKesisMiddleware`, backend `IlkKesisTests` birebir Location'ı kilitler) Playwright ile
 * taklit edilir: yol haritadan, sorgu AYNEN, fragment YOK (tarayıcı özgününü korur). F7 şablonlarının SPA adı
 * Blazor adıyla aynıdır (`/cariler/{id}/detay` → `/app/cariler/{id}/detay` …).
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
  await customerCrmEndpoints(page);
}

const PAGES = [
  { blazor: '/cariler', spa: '/app/cariler', heading: 'Cariler' },
  { blazor: `/cariler/${CARI_1}`, spa: `/app/cariler/${CARI_1}`, heading: 'Cari: Ayşe Yılmaz' },
  {
    blazor: `/cariler/${CARI_1}/detay`,
    spa: `/app/cariler/${CARI_1}/detay`,
    heading: 'Ayşe Yılmaz',
  },
  { blazor: '/anketler', spa: '/app/anketler', heading: 'Müşteri Anketleri' },
  { blazor: '/sikayetler', spa: '/app/sikayetler', heading: 'Müşteri Şikayetleri' },
  { blazor: '/assistans', spa: '/app/assistans', heading: 'Assistans (Yol Yardım) Talepleri' },
  { blazor: '/hukuk', spa: '/app/hukuk', heading: 'Hukuk Dosyaları' },
  { blazor: '/crm', spa: '/app/crm', heading: 'CRM — Müşteri Segment & Personel Çalışma' },
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

/**
 * Blazor cari/CRM sayfa yolları (SPA'nın `/app/...` bağlantıları buna uymaz; uyan bağlantı eski arayüze düşer).
 * `/cariler/{id}/ekstre` F8 envanterindedir ama Blazor sayfasıdır — SPA'daki bağlantısı da router'la gitmeli.
 */
const BLAZOR_F7 =
  /^\/(cariler|cariler\/[0-9a-f-]{36}|cariler\/[0-9a-f-]{36}\/(detay|ekstre)|anketler|sikayetler|assistans|hukuk|crm)$/i;

async function fallenLinks(page: Page, scope: string): Promise<string[]> {
  return page.locator(`${scope} a[href]`).evaluateAll(
    (links, pattern) =>
      links
        .map((a) => new URL((a as HTMLAnchorElement).href, location.href))
        .filter((u) => u.origin === location.origin)
        .map((u) => u.pathname.replace(/\/$/, ''))
        .filter((path) => new RegExp(pattern, 'i').test(path)),
    BLAZOR_F7.source,
  );
}

test('pilot: F7 ekranlarının sayfa içeriğindeki hiçbir bağlantı Blazor cari/CRM sayfasına düşmez', async ({
  page,
}) => {
  await fakes(page);

  for (const p of PAGES) {
    await page.goto(p.spa);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(p.heading);
    expect(await fallenLinks(page, 'main'), p.spa).toEqual([]);
  }
  // CRM analizindeki personel çalışma raporu bağlantısı SPA rapor rotasına gider (tam sayfa değil).
  await page.goto('/app/crm');
  await expect(
    page.getByRole('link', { name: 'Personel Çalışma (Vardiya) raporuna bakın.' }),
  ).toHaveAttribute('href', '/app/raporlar/personel-calisma');
  // Detayın ekstre sekmesindeki "Tam ekstre" bağlantısı SPA ekstre rotasına gider.
  await page.goto(`/app/cariler/${CARI_1}/detay#sekme=ekstre`);
  await expect(
    page.getByRole('link', { name: 'Tam ekstre (yazdır / dışa aktar)' }),
  ).toHaveAttribute('href', `/app/cariler/${CARI_1}/ekstre`);
});

/** Tam sayfa yüklemesi olursa pencere nesnesi yenilenir ve işaret kaybolur. */
async function markWindow(page: Page): Promise<void> {
  await page.evaluate(() => {
    (window as unknown as { rcKesisIzi?: number }).rcKesisIzi = 1;
  });
}

async function windowMarked(page: Page): Promise<boolean> {
  return page.evaluate(() => (window as unknown as { rcKesisIzi?: number }).rcKesisIzi === 1);
}

test('kira formu: cari kartı ve ekstre bağlantıları SPA rotasına router ile gider (tam sayfa yok)', async ({
  page,
}) => {
  await remainingEndpoints(page);
  await oturumAc(page);
  await sahteKiraApi(page);
  await page.goto(`/app/kiralar/${KIRA_ID}#sekme=musteri`);
  const panel = page.getByRole('tabpanel', { name: 'Müşteri' });
  await expect(panel.getByTestId('musteri-ozeti')).toBeVisible();

  const statementHref = `/app/cariler/${MUSTERI_ID}/ekstre`;
  await expect(panel.getByRole('link', { name: 'Cari kartını aç' })).toHaveAttribute(
    'href',
    `/app/cariler/${MUSTERI_ID}`,
  );
  await expect(panel.getByRole('link', { name: 'Cari ekstre' })).toHaveAttribute(
    'href',
    statementHref,
  );
  // Sözleşme bağlantıları sayfa bandının ikincil eylemlerinde (Yol v2 §5.4).
  const header = page.locator('rc-sayfa-bandi');
  await expect(header.getByRole('link', { name: 'Cari ekstre' })).toHaveAttribute(
    'href',
    statementHref,
  );
  expect(await fallenLinks(page, 'main')).toEqual([]);

  await markWindow(page);
  await panel.getByRole('link', { name: 'Cari ekstre' }).click();
  await expect(page).toHaveURL((url) => url.pathname === statementHref);
  expect(await windowMarked(page)).toBe(true);
});

test('#295 M1: FinanceWrite/ViewReports yoksa kira formunda ekstre bağlantısı görünmez', async ({
  page,
}) => {
  await remainingEndpoints(page);
  await oturumAc(page, { ...BEN, izinler: ['OperationsWrite'] });
  await sahteKiraApi(page);
  await page.goto(`/app/kiralar/${KIRA_ID}#sekme=musteri`);
  const panel = page.getByRole('tabpanel', { name: 'Müşteri' });
  await expect(panel.getByRole('link', { name: 'Cari kartını aç' })).toHaveAttribute(
    'href',
    `/app/cariler/${MUSTERI_ID}`,
  );
  await expect(page.getByRole('link', { name: 'Cari ekstre' })).toHaveCount(0);
});
