import { expect, test, type Page, type Route } from '@playwright/test';

import { INVOICE_1, documentEndpoints } from './finance-document-fakes';
import { CARI_1, financeHubEndpoints } from './finance-fakes';
import { KIRA_ID, sahteKiraApi } from './kira-sahte';
import { BEN, oturumAc, problem } from './ortak';

/**
 * F8.3 finans kesişi (sahte `/api/ui/v1`, üretim derlemesi + CSP). Harness yalnız statik SPA sunar; Blazor sunucusunun
 * 302'si (`IlkKesisMiddleware`, backend `IlkKesisTests` birebir Location'ı kilitler) Playwright ile taklit edilir:
 * yol haritadan, sorgu AYNEN, fragment YOK (tarayıcı özgününü korur). F8 şablonlarının SPA adı Blazor adıyla aynıdır
 * (`/kasa` → `/app/kasa`, `/cariler/{id}/ekstre` → `/app/cariler/{id}/ekstre` …).
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

const ALL_PERMISSIONS = { ...BEN, izinler: [...BEN.izinler, 'OperationsDelete', 'FinanceReverse'] };

async function hubFakes(page: Page): Promise<void> {
  await remainingEndpoints(page);
  await oturumAc(page, ALL_PERMISSIONS);
  await financeHubEndpoints(page);
}

async function documentFakes(page: Page): Promise<void> {
  await remainingEndpoints(page);
  await oturumAc(page, ALL_PERMISSIONS);
  await documentEndpoints(page);
}

interface Screen {
  readonly blazor: string;
  readonly heading: string;
  readonly fakes: (page: Page) => Promise<void>;
}

/** 18 ekran (fatura yazdır ayrı: sunucunun PDF ucuna tam sayfa gider). SPA adresi = `/app` + Blazor adresi. */
const SCREENS: readonly Screen[] = [
  { blazor: '/kasa', heading: 'Kasa / Banka', fakes: hubFakes },
  { blazor: '/finans/nakit-islem', heading: 'Nakit İşlem (Tahsilat / Ödeme)', fakes: hubFakes },
  { blazor: '/finans/bakiye-duzeltme', heading: 'Bakiye Düzeltme', fakes: hubFakes },
  { blazor: '/cari-virman', heading: 'Cari ↔ Cari Virman', fakes: hubFakes },
  { blazor: '/depozito', heading: 'Depozito (Emanet) İşlemleri', fakes: hubFakes },
  { blazor: '/tek-cari-toplu', heading: 'Tek Cari — Toplu Kapatma', fakes: hubFakes },
  { blazor: '/toplu-tahsilat', heading: 'Toplu Tahsilat', fakes: hubFakes },
  { blazor: '/toplu-gider', heading: 'Toplu Gider', fakes: hubFakes },
  { blazor: '/otomatik-tahsilat', heading: 'Otomatik Tahsilat — Elle Çalıştır', fakes: hubFakes },
  { blazor: '/donem-kapanis', heading: 'Dönem Kapanışı', fakes: hubFakes },
  { blazor: '/kurlar', heading: 'Döviz Kurları (TCMB)', fakes: hubFakes },
  { blazor: `/cariler/${CARI_1}/ekstre`, heading: 'Ekstre — Ayşe Yılmaz', fakes: hubFakes },
  { blazor: '/faturalar', heading: 'Faturalar', fakes: documentFakes },
  { blazor: '/faturalar/detay-listesi', heading: 'Fatura Detay Listesi', fakes: documentFakes },
  { blazor: '/cezalar', heading: 'Trafik Cezaları', fakes: documentFakes },
  { blazor: '/giderler', heading: 'Giderler', fakes: documentFakes },
  { blazor: '/gelen-efatura', heading: 'Gelen e-Fatura', fakes: documentFakes },
  { blazor: '/satislar', heading: 'Araç Satışları', fakes: documentFakes },
];

for (const s of SCREENS) {
  test(`pilot: eski ${s.blazor} adresi → /app${s.blazor} (sorgu AYNEN, fragment korunur, SPA sayfası açılır)`, async ({
    page,
  }) => {
    await s.fakes(page);
    const spa = `/app${s.blazor}`;
    await serverRedirect(page, s.blazor, spa);

    await page.goto(`${s.blazor}?bilgi=x#iz`);
    await expect(page).toHaveURL(
      (url) => url.pathname === spa && url.search === '?bilgi=x' && url.hash === '#iz',
    );
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(s.heading);
  });
}

test('pilot: eski fatura yazdırma adresi → /app yazdırma rotası → sunucunun PDF ucu (döngü yok)', async ({
  page,
}) => {
  await documentFakes(page);
  await serverRedirect(
    page,
    `/faturalar/${INVOICE_1}/yazdir`,
    `/app/faturalar/${INVOICE_1}/yazdir`,
  );
  const pdfRequests: string[] = [];
  await page.route(`**/faturalar/${INVOICE_1}/pdf`, (route) => {
    pdfRequests.push(route.request().url());
    return route.fulfill({ contentType: 'text/plain', body: 'PDF' });
  });

  await page.goto(`/faturalar/${INVOICE_1}/yazdir`);
  await expect(page).toHaveURL((url) => url.pathname === `/faturalar/${INVOICE_1}/pdf`);
  // PDF ucu haritada yok: sunucu yönlendirmez, SPA'ya dönülmez; tek istek.
  expect(pdfRequests).toHaveLength(1);
});

test('#295 M1: FinanceWrite/ViewReports olmayan operatör eski ekstre adresinden SPA izin kapısına düşer', async ({
  page,
}) => {
  await remainingEndpoints(page);
  await oturumAc(page, { ...BEN, rol: 'Operator', izinler: ['OperationsWrite'] });
  await financeHubEndpoints(page);
  await serverRedirect(page, `/cariler/${CARI_1}/ekstre`, `/app/cariler/${CARI_1}/ekstre`);

  await page.goto(`/cariler/${CARI_1}/ekstre`);
  await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();
  await expect(page).not.toHaveURL((url) => url.pathname.endsWith('/ekstre'));
  await expect(page.getByRole('heading', { name: 'Ekstre — Ayşe Yılmaz' })).toHaveCount(0);
});

/** Blazor finans sayfa yolları (SPA'nın `/app/...` bağlantıları buna uymaz; uyan bağlantı eski arayüze düşer). */
const BLAZOR_F8 =
  /^\/(kasa|finans\/(nakit-islem|bakiye-duzeltme)|cari-virman|depozito|tek-cari-toplu|toplu-(tahsilat|gider)|otomatik-tahsilat|donem-kapanis|kurlar|cariler\/[0-9a-f-]{36}\/ekstre|faturalar(\/(detay-listesi|[0-9a-f-]{36}\/yazdir))?|cezalar|giderler|gelen-efatura|satislar)$/i;

async function fallenLinks(page: Page, scope: string): Promise<string[]> {
  return page.locator(`${scope} a[href]`).evaluateAll(
    (links, pattern) =>
      links
        .map((a) => new URL((a as HTMLAnchorElement).href, location.href))
        .filter((u) => u.origin === location.origin)
        .map((u) => u.pathname.replace(/\/$/, ''))
        .filter((path) => new RegExp(pattern, 'i').test(path)),
    BLAZOR_F8.source,
  );
}

for (const [group, fakes] of [
  ['kasa/banka', hubFakes],
  ['belge', documentFakes],
] as const) {
  test(`pilot: F8 ${group} ekranlarının sayfa içeriğindeki hiçbir bağlantı Blazor finans sayfasına düşmez`, async ({
    page,
  }) => {
    await fakes(page);
    for (const s of SCREENS.filter((x) => x.fakes === fakes)) {
      await page.goto(`/app${s.blazor}`);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(s.heading);
      expect(await fallenLinks(page, 'main'), s.blazor).toEqual([]);
    }
  });
}

/** Tam sayfa yüklemesi olursa pencere nesnesi yenilenir ve işaret kaybolur. */
async function markWindow(page: Page): Promise<void> {
  await page.evaluate(() => {
    (window as unknown as { rcKesisIzi?: number }).rcKesisIzi = 1;
  });
}

async function windowMarked(page: Page): Promise<boolean> {
  return page.evaluate(() => (window as unknown as { rcKesisIzi?: number }).rcKesisIzi === 1);
}

test('kira formu finans paneli: depozito, fatura ve ceza bağlantıları SPA rotasına router ile gider (tam sayfa yok)', async ({
  page,
}) => {
  await remainingEndpoints(page);
  await oturumAc(page);
  await sahteKiraApi(page);
  await page.route(`**/api/ui/v1/kiralar/${KIRA_ID}/faturalar`, (route) =>
    route.fulfill({
      json: [
        {
          id: INVOICE_1,
          no: 'RNT2026000000001',
          tarih: '2026-09-22T06:00:00+00:00',
          genelToplam: 3600,
          currency: 'TRY',
          tur: 'Kira',
          durum: 'Kesildi',
        },
      ],
    }),
  );
  await page.route(`**/api/ui/v1/kiralar/${KIRA_ID}/cezalar`, (route) =>
    route.fulfill({ json: { cezalar: [], hgsGecisleri: [] } }),
  );
  await page.goto(`/app/kiralar/${KIRA_ID}`);
  const panel = page.getByTestId('finans-paneli');

  await expect(panel.getByRole('link', { name: 'Depozito ekranı' })).toHaveAttribute(
    'href',
    '/app/depozito',
  );
  await panel.getByRole('tab', { name: 'Ceza/HGS', exact: true }).click();
  await expect(panel.getByRole('link', { name: 'Ceza ekle / yansıt' })).toHaveAttribute(
    'href',
    '/app/cezalar',
  );
  await panel.getByRole('tab', { name: 'Faturalar', exact: true }).click();
  const invoiceLink = panel.getByRole('link', { name: 'RNT2026000000001' });
  await expect(invoiceLink).toHaveAttribute('href', '/app/faturalar');
  expect(await fallenLinks(page, 'main')).toEqual([]);

  await markWindow(page);
  await invoiceLink.click();
  await expect(page).toHaveURL((url) => url.pathname === '/app/faturalar');
  expect(await windowMarked(page)).toBe(true);
});
