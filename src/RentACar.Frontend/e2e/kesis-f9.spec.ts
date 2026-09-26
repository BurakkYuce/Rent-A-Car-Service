import { expect, test, type Page, type Route } from '@playwright/test';

import { BEN, logIn, problem } from './ortak';
import {
  INSPECTION_1,
  MTV_1,
  POLICY_1,
  SERVICE_1,
  serviceInsuranceEndpoints,
} from './service-insurance-fakes';
import { vehicleEndpoints } from './vehicle-fakes';

/**
 * F9.3 servis / sigorta / vade + fiyat / tarife kesişi (sahte `/api/ui/v1`, üretim derlemesi + CSP). Harness yalnız
 * statik SPA sunar; Blazor sunucusunun 302'si (`IlkKesisMiddleware`, backend `IlkKesisTests` birebir Location'ı
 * kilitler) Playwright ile taklit edilir: yol haritadan, sorgu AYNEN, fragment YOK (tarayıcı özgününü korur). 15 sayfa
 * AYNI adla `/app` altına.
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

const ADMIN = { ...BEN, izinler: [...BEN.izinler, 'ManageUsers', 'OperationsDelete'] };

async function fakes(page: Page): Promise<void> {
  await remainingEndpoints(page);
  await logIn(page, ADMIN);
  await serviceInsuranceEndpoints(page);
}

const PAGES: readonly (readonly [path: string, heading: string])[] = [
  ['servisler', 'Servis / Bakım'],
  ['regulasyon', 'Sigorta'],
  ['vade', 'Vade Uyarıları'],
  ['servis-tanimlari', 'Periyodik Bakım Tanım Tablosu'],
  ['tarifeler', 'Tarife Yönetimi'],
  ['tarife-matris', 'Tarife Matrisi'],
  ['tarife-gruplari', 'Tarife (Fiyat) Grupları'],
  ['tarife-aktar', 'Tarife İçe Aktar (Toplu Fiyat)'],
  ['sigorta-urunleri', 'Sigorta & Ek Hizmet Ürün Kataloğu'],
  ['kira-kurallari', 'Kiralama Kuralları (Promosyon / Şart)'],
  ['broker-yasaklari', 'Broker / Kaynak Satış Yasakları'],
  ['fiyat-hesapla', 'Fiyat Motoru — Kira Teklifi Hesapla'],
  ['maliyet-hesapla', 'Filo / Uzun Dönem Maliyet Hesaplayıcı'],
  ['maliyet-teklifleri', 'Kayıtlı Maliyet Teklifleri'],
  ['ek-hizmetler', 'Ek Hizmet Tanımları'],
];

test('envanter: 15 sayfa (F9.md tablosu)', () => {
  expect(PAGES).toHaveLength(15);
});

for (const [path, heading] of PAGES) {
  test(`pilot: eski /${path} adresi → /app/${path} (sorgu AYNEN, fragment korunur, SPA sayfası açılır)`, async ({
    page,
  }) => {
    await fakes(page);
    await serverRedirect(page, `/${path}`, `/app/${path}`);

    await page.goto(`/${path}?bilgi=x#iz`);
    await expect(page).toHaveURL(
      (url) => url.pathname === `/app/${path}` && url.search === '?bilgi=x' && url.hash === '#iz',
    );
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(heading);
  });
}

/** Blazor F9 sayfa yolları (SPA'nın `/app/...` bağlantıları buna uymaz; uyan bağlantı eski arayüze düşer). */
const BLAZOR_F9 = new RegExp(`^/(${PAGES.map(([p]) => p).join('|')})$`, 'i');

async function fallenLinks(page: Page): Promise<string[]> {
  return page.locator('main a[href]').evaluateAll(
    (links, pattern) =>
      links
        .map((a) => new URL((a as HTMLAnchorElement).href, location.href))
        .filter((u) => u.origin === location.origin)
        .map((u) => u.pathname.replace(/\/$/, ''))
        .filter((path) => new RegExp(pattern, 'i').test(path)),
    BLAZOR_F9.source,
  );
}

test('pilot: F9 ekranlarının (liste + kayıt) sayfa içeriğindeki hiçbir bağlantı Blazor sayfasına düşmez', async ({
  page,
}) => {
  test.setTimeout(60_000);
  await fakes(page);
  const records = [
    `/app/servisler/${SERVICE_1}`,
    `/app/regulasyon/sigortalar/${POLICY_1}`,
    '/app/regulasyon/mtv',
    `/app/regulasyon/mtv/${MTV_1}`,
    '/app/regulasyon/muayene',
    `/app/regulasyon/muayeneler/${INSPECTION_1}`,
  ];
  for (const path of [...PAGES.map(([p]) => `/app/${p}`), ...records]) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    expect(await fallenLinks(page), path).toEqual([]);
  }
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

const tier = { yediGun: 1, otuzGun: 2, gecmis: 1 };

/** Panel özeti — yalnız vade kutuları ve vade uyarısı için gereken alanlar dolu. */
const PANEL_SUMMARY = {
  bugun: '2026-09-22',
  kpi: {
    toplamArac: 10,
    kirada: 4,
    musait: 5,
    serviste: 1,
    acikRezervasyon: 2,
    kmGecenBakim: 0,
    gorulmeyenRezervasyon: 0,
  },
  vade: {
    trafik: tier,
    kasko: tier,
    muayene: tier,
    gecmisUyari: 1,
    yaklasanUyari: 2,
    acikSikayet: 0,
  },
  donusler: { gecikmis: [], bugun: [], yarin: [], varsayilanSekme: 'bugun' },
  cikislar: { gecikmis: [], bugun: [], yarin: [], varsayilanSekme: 'bugun' },
  finans: null,
};

test('panel: vade kutuları ve "Vade panosu" bağlantısı SPA vade ekranına router ile gider (tam sayfa yok)', async ({
  page,
}) => {
  await fakes(page);
  await page.route('**/api/ui/v1/panel/ozet', (route) => route.fulfill({ json: PANEL_SUMMARY }));
  await page.goto('/app/panel');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Panel');

  const board = page.getByRole('link', { name: 'Vade panosu →' });
  await expect(board).toHaveAttribute('href', '/app/vade');
  // Hatırlatmalar'daki vade kademe satırları (trafik / kasko / muayene) da SPA rotasına gider.
  await expect(
    page.locator('main rc-hatirlatma-listesi a[href="/app/vade"]').first(),
  ).toBeVisible();
  expect(await page.locator('main rc-hatirlatma-listesi a[href="/vade"]').count()).toBe(0);
  expect(await fallenLinks(page)).toEqual([]);

  await markWindow(page);
  await board.click();
  await expect(page).toHaveURL((url) => url.pathname === '/app/vade');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Vade Uyarıları');
  expect(await windowMarked(page)).toBe(true);
});

test('araç durum: "Servis" bağlantısı SPA servis ekranına router ile gider (tam sayfa yok)', async ({
  page,
}) => {
  await remainingEndpoints(page);
  await logIn(page, ADMIN);
  await vehicleEndpoints(page);
  await serviceInsuranceEndpoints(page); // son kaydedilen önce eşleşir: seçim uçları servis sahtesinden
  await page.goto('/app/arac-durum');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  const service = page.locator('main').getByRole('link', { name: 'Servis', exact: true }).first();
  await expect(service).toHaveAttribute('href', '/app/servisler');
  expect(await fallenLinks(page)).toEqual([]);

  await markWindow(page);
  await service.click();
  await expect(page).toHaveURL((url) => url.pathname === '/app/servisler');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Servis / Bakım');
  expect(await windowMarked(page)).toBe(true);
});
