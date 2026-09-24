import { expect, test, type Page, type Route } from '@playwright/test';

import { BEN, oturumAc, problem } from './ortak';
import { VEHICLE_1 as REPORT_VEHICLE, reportEndpoints } from './report-fakes';
import { financeEndpoints } from './vehicle-finance-fakes';
import { VEHICLE_1, vehicleEndpoints } from './vehicle-fakes';

/**
 * F10.3 rapor kesişi (sahte `/api/ui/v1`, üretim derlemesi + CSP). Harness yalnız statik SPA sunar; Blazor
 * sunucusunun 302'si (`IlkKesisMiddleware`, backend `IlkKesisTests` birebir Location'ı kilitler) Playwright ile
 * taklit edilir: yol haritadan, sorgu AYNEN, fragment YOK (tarayıcı özgününü korur). 26 rapor sayfası AYNI adla
 * `/app` altına (tek ortak rapor ekranı; araç karnesi kimlikli). Sahtesi olmayan rapor ucu 404 döner — başlık ve
 * yönlendirme yine doğrulanır (ekran hata bandını gösterir).
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
  await reportEndpoints(page);
}

const REPORTS: readonly (readonly [code: string, heading: string])[] = [
  ['arac-durum-takip', 'Araç Durum Takip'],
  ['arac-gunluk-durum', 'Araç Günlük Durum'],
  ['cari-bakiye', 'Cari Bakiye Raporu'],
  ['doluluk', 'Doluluk Raporu'],
  ['ek-hizmet', 'Ek Hizmet Raporu'],
  ['extre-ozeti', 'Ekstre Özeti'],
  ['fatura-donem', 'Dönem Faturaları'],
  ['filo', 'Filo Durumu'],
  ['filo-analiz', 'Filo Analiz'],
  ['finans-analiz', 'Finans Analiz'],
  ['gelir-gider', 'Gelir-Gider Raporu'],
  ['gunluk', 'Günlük Faaliyet'],
  ['karlilik', 'Araç Kârlılığı'],
  ['karsilastirmali-analiz', 'Karşılaştırmalı Analiz'],
  ['kasa-banka', 'Kasa/Banka Defteri'],
  ['kdv-listesi', 'KDV Listesi'],
  ['km-detay', 'Km Detay'],
  ['otomatik-servisler', 'Otomatik Servisler'],
  ['periyodik-servis', 'Periyodik Servis'],
  ['personel-calisma', 'Personel Çalışma Tablosu'],
  ['rezervasyon-kaynak', 'Rezervasyon Kaynakları'],
  ['servis-ozet', 'Servis Maliyet Özeti'],
  ['sigorta-muayene', 'Sigorta / Muayene'],
  ['tahsilat-fatura', 'Tahsilat–Fatura'],
  ['virman-gecmisi', 'Virman Geçmişi'],
];

const PAGES = [
  ...REPORTS.map(([code, heading]) => ({
    blazor: `/raporlar/${code}`,
    spa: `/app/raporlar/${code}`,
    heading,
  })),
  {
    blazor: `/raporlar/arac-karne/${REPORT_VEHICLE}`,
    spa: `/app/raporlar/arac-karne/${REPORT_VEHICLE}`,
    heading: 'Araç Karnesi',
  },
] as const;

test('envanter: 26 rapor sayfası (F10.md tablosu)', () => {
  expect(PAGES).toHaveLength(26);
});

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

/** Blazor F10 sayfa yolları (SPA'nın `/app/...` bağlantıları buna uymaz; uyan bağlantı eski arayüze düşer). */
const BLAZOR_F10 = new RegExp(
  `^/raporlar/(${REPORTS.map(([c]) => c).join('|')}|arac-karne/[0-9a-f-]{36})$`,
  'i',
);

async function fallenLinks(page: Page): Promise<string[]> {
  return page.locator('main a[href]').evaluateAll(
    (links, pattern) =>
      links
        .map((a) => new URL((a as HTMLAnchorElement).href, location.href))
        .filter((u) => u.origin === location.origin)
        .map((u) => u.pathname.replace(/\/$/, ''))
        .filter((path) => new RegExp(pattern, 'i').test(path)),
    BLAZOR_F10.source,
  );
}

test('pilot: rapor ekranlarının sayfa içeriğindeki hiçbir bağlantı Blazor rapor sayfasına düşmez', async ({
  page,
}) => {
  await fakes(page);
  for (const p of PAGES) {
    await page.goto(p.spa);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(p.heading);
    expect(await fallenLinks(page), p.spa).toEqual([]);
  }
});

test('pilot: araç ekranlarındaki "Araç Karnesi" bağlantısı SPA içinde açılır (Blazor karnesine düşmez)', async ({
  page,
}) => {
  await fakes(page);
  for (const path of [
    '/app/araclar',
    `/app/araclar/${VEHICLE_1}`,
    `/app/araclar/${VEHICLE_1}/detay`,
  ]) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    expect(await fallenLinks(page), path).toEqual([]);
  }
  await page.goto(`/app/araclar/${VEHICLE_1}/detay`);
  const scorecard = page.locator(`main a[href="/app/raporlar/arac-karne/${VEHICLE_1}"]`);
  await expect(scorecard).toBeVisible();
  await scorecard.click();
  await expect(page).toHaveURL((url) => url.pathname === `/app/raporlar/arac-karne/${VEHICLE_1}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Araç Karnesi');
});
