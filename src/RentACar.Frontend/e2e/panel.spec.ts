import { expect, test, type Page } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, logIn, problem } from './ortak';
import { measureOverflow } from './vitrin-sayfalari';

/**
 * F4.5 Panel (sahte `/api/ui/v1`, üretim derlemesi + CSP): gecikme varsayılanlı sekme, finans kapısı
 * (yanıtta yoksa çizilmez), "Tahsil Et" para kuralları (anahtar aynen, kilit, 409'da yeniden gönderim yok),
 * iki temada axe, 390 px taşma.
 */
const PANEL = '/app/panel';
const KEY = '0f9b2c1e-6a1d-5b7e-9c3a-2d4e6f8a0b1c';
const STALE =
  'Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti ya da tahsilat anahtarı bu kiraya ait değil; kaydı yeniden yükleyip tekrar deneyin.';
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];

const tier = { yediGun: 1, otuzGun: 2, gecmis: 0 };

function donus(no: string, collection: boolean) {
  return {
    rentalId: `kira-${no}`,
    sozlesmeNo: no,
    tarih: '2026-09-21T09:00:00Z',
    musteriAd: 'Ayşe Yılmaz',
    plaka: '34 ABC 123',
    ofis: 'Merkez Ofis',
    bakiye: 1250.5,
    doviz: 'TRY',
    tahsilat: collection
      ? {
          anahtar: KEY,
          cariId: 'cari-1',
          rentalId: `kira-${no}`,
          doviz: 'TRY',
          varsayilanTutar: 1250.5,
        }
      : null,
  };
}

function ozet(finance: boolean, collection = true) {
  return {
    bugun: '2026-09-22',
    kpi: {
      toplamArac: 24,
      kirada: 12,
      musait: 9,
      serviste: 3,
      acikRezervasyon: 7,
      kmGecenBakim: 1,
      gorulmeyenRezervasyon: 1,
      siteTalebi: { yeni: 2, enEskiGun: 4 },
    },
    vade: {
      trafik: tier,
      kasko: tier,
      muayene: { yediGun: 0, otuzGun: 0, gecmis: 1 },
      gecmisUyari: 1,
      yaklasanUyari: 3,
      acikSikayet: 1,
    },
    donusler: {
      gecikmis: [donus('2026200901001', collection)],
      bugun: [donus('2026220901002', false)],
      yarin: [],
      varsayilanSekme: 'gec',
    },
    cikislar: {
      gecikmis: [
        {
          reservationId: 'r-1',
          reservationNo: '2026180902001',
          tarih: '2026-09-18T08:00:00Z',
          musteriAd: 'Gelmeyen Müşteri',
          plaka: '06 XY 42',
          ofis: null,
        },
      ],
      bugun: [
        {
          reservationId: 'r-2',
          reservationNo: '2026220902002',
          tarih: '2026-09-22T13:30:00Z',
          musteriAd: 'Mehmet Işık',
          plaka: '35 KL 789',
          ofis: 'Havalimanı',
        },
      ],
      yarin: [],
      varsayilanSekme: 'bugun',
    },
    finans: finance
      ? {
          kasaBakiye: 15250.75,
          bankaBakiye: 128400,
          acikBakiye: 9320.4,
          bugunTahsilatTutar: 4200,
          bugunTahsilatAdet: 3,
          filoDolulukYuzde: 64.2,
          revPacd: 820,
          adr: 1450,
          gelirTrendi: [4, 5, 6, 7, 8, 9].map((month, i) => ({
            ayBas: `2026-0${month}-01T00:00:00+03:00`,
            gelir: 90000 + i * 12500,
          })),
        }
      : null,
  };
}

/** Panel ucu sahtesi; çağrı sayısını döner. */
async function fakePanel(page: Page, response: () => object): Promise<{ sayi: () => number }> {
  let count = 0;
  await page.route('**/api/ui/v1/panel/ozet', (route) => {
    count++;
    return route.fulfill({ json: response() });
  });
  await page.route('**/api/ui/v1/finans/hesaplar', (route) =>
    route.fulfill({
      json: [
        { id: 'h-1', etiket: 'Kasa · Merkez', kod: 'K1', ad: 'Merkez', tur: 'Kasa', doviz: null },
      ],
    }),
  );
  return { sayi: () => count };
}

async function ready(page: Page): Promise<void> {
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Panel');
  await expect(page.getByRole('heading', { name: 'Dönüşler' })).toBeVisible();
  await page.waitForFunction(() => document.fonts.status === 'loaded');
}

test('gecikmiş dönüş varsa Gecikmiş açılır; satır kira formuna bağlanır; iki temada axe temiz', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await logIn(page);
  await fakePanel(page, () => ozet(true));
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto(PANEL);
  await ready(page);

  const returns = page.getByRole('region', { name: 'Dönüşler' });
  await expect(returns.getByRole('button', { name: /Gecikmiş/ })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(returns.getByRole('link', { name: /2026200901001/ })).toHaveAttribute(
    'href',
    '/app/kiralar/kira-2026200901001#sekme=donus',
  );
  const pickups = page.getByRole('region', { name: 'Çıkışlar' });
  await expect(pickups.getByRole('button', { name: /Bugün/ })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(pickups.getByRole('cell', { name: 'Mehmet Işık' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Finans özeti' })).toBeVisible();
  expect(await seriousViolations(page), 'açık tema').toEqual([]);

  await page.emulateMedia({ colorScheme: 'dark' });
  await expect
    .poll(() => page.evaluate(() => getComputedStyle(document.body).backgroundColor))
    .toBe('rgb(20, 19, 16)');
  expect(await seriousViolations(page), 'koyu tema').toEqual([]);

  // Sekme seçimi sorguya yazılır (Blazor ?df= sözleşmesi; yenilemede korunur).
  await returns.getByRole('button', { name: /Bugün/ }).click();
  await expect(page).toHaveURL(/\/app\/panel\?df=bugun$/);
  await expect(returns.getByRole('link', { name: /2026220901002/ })).toBeVisible();
  await page.reload();
  await ready(page);
  await expect(
    page.getByRole('region', { name: 'Dönüşler' }).getByRole('button', { name: /Bugün/ }),
  ).toHaveAttribute('aria-pressed', 'true');
  expect(errors).toEqual([]);
});

test('finans yetkisi yoksa (yanıtta finans/tahsilat yok) finans bloğu ve Tahsil Et çizilmez', async ({
  page,
}) => {
  await logIn(page, { ...BEN, izinler: ['OperationsWrite'] });
  await fakePanel(page, () => ozet(false, false));
  await page.goto(PANEL);
  await ready(page);
  await expect(page.getByRole('cell', { name: 'Ayşe Yılmaz' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Finans özeti' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /tahsil et/i })).toHaveCount(0);
});

test('F5.4 kesiş: panelin rezervasyon/müsaitlik bağlantıları SPA rotası (Blazor sayfasına düşmez)', async ({
  page,
}) => {
  await logIn(page);
  await fakePanel(page, () => ozet(true));
  await page.goto(PANEL);
  await ready(page);

  // Sayfa içeriğinde kesiş haritasındaki bir Blazor sayfasına (F4 + F5 + F11 gelen talepler) giden bağlantı kalmadı.
  const toBlazor = await page.locator('main a[href]').evaluateAll((items) =>
    items
      .map((o) => new URL((o as HTMLAnchorElement).href, location.href))
      .filter((u) => u.origin === location.origin)
      .map((u) => u.pathname.replace(/\/$/, '') || '/')
      .filter((path) =>
        /^\/(|kiralar(\/.*)?|rezervasyonlar|teklifler|takvim|musaitlik|rez-sartlari|filo-kiralama|gelen-talepler)$/i.test(
          path,
        ),
      ),
  );
  expect(toBlazor).toEqual([]);

  const main = page.locator('main');
  // F11.3: site talebi kutusu SPA gelen talepler ekranına "Yeni" süzgeciyle gider.
  await expect(main.getByRole('link', { name: /Site talebi/ })).toHaveAttribute(
    'href',
    '/app/gelen-talepler?durum=Yeni',
  );
  await expect(main.getByRole('link', { name: /Görülmeyen rez\./ })).toHaveAttribute(
    'href',
    '/app/rezervasyonlar',
  );
  await expect(main.getByRole('link', { name: 'Müsaitlik', exact: true })).toHaveAttribute(
    'href',
    '/app/musaitlik',
  );
  await main.getByRole('link', { name: '+ Rezervasyon', exact: true }).click();
  await expect(page).toHaveURL((url) => url.pathname === '/app/rezervasyonlar/yeni');
});

test('Tahsil Et: sunucu anahtarı aynen gider, gönderim kilitli, 2xx sonrası panel tazelenir', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await logIn(page);
  const panel = await fakePanel(page, () => ozet(true));
  const bodies: unknown[] = [];
  const headers: (string | undefined)[] = [];
  let release: () => void = () => undefined;
  const wait = new Promise<void>((r) => (release = r));
  await page.route('**/api/ui/v1/finans/tahsilat', async (route) => {
    bodies.push(route.request().postDataJSON());
    headers.push(route.request().headers()['idempotency-key']);
    await wait;
    return route.fulfill({ json: { id: 'islem-1' } });
  });
  await page.goto(PANEL);
  await ready(page);
  expect(panel.sayi()).toBe(1);

  await page.getByRole('button', { name: '34 ABC 123 için tahsil et' }).click();
  const form = page.getByRole('form', { name: /Tahsilat — 34 ABC 123/ });
  // Odakta düzenleme yazımı (gruplamasız); bakiye ön dolu.
  await expect(form.getByLabel('Tutar')).toBeFocused();
  await expect(form.getByLabel('Tutar')).toHaveValue('1250,50');
  await form.getByLabel('Tutar').fill('1000');
  const gonder = form.getByRole('button', { name: /Tahsil et|Gönderiliyor/ });
  await gonder.click();
  await expect(gonder).toBeDisabled();
  await gonder.click({ force: true }); // kilitliyken ikinci tık istek üretmez
  release();

  await expect(page.getByRole('status').filter({ hasText: 'Tahsilat kaydedildi' })).toBeVisible();
  await expect.poll(() => panel.sayi()).toBe(2);
  await expect(form).toHaveCount(0);
  expect(bodies).toEqual([
    {
      cariId: 'cari-1',
      kiraId: 'kira-2026200901001',
      tutar: '1000.00',
      hesap: 'Kasa',
      hesapId: null,
      doviz: 'TRY',
      kanal: 'Masaüstü',
      aciklama: 'Hızlı tahsilat (pano) — 34 ABC 123',
      tahsilatAnahtar: KEY,
    },
  ]);
  expect(headers).toEqual([undefined]);
  expect(errors).toEqual([]);
});

test('Tahsil Et 409 mukerrer (bayat anahtar): yeniden gönderilmez, panel yeniden yüklenir, sunucunun detail’ı gösterilir', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await logIn(page);
  const panel = await fakePanel(page, () => ozet(true));
  let submission = 0;
  await page.route('**/api/ui/v1/finans/tahsilat', (route) => {
    submission++;
    return problem(route, 409, 'mukerrer', STALE);
  });
  await page.goto(PANEL);
  await ready(page);
  await page.getByRole('button', { name: '34 ABC 123 için tahsil et' }).click();
  await page
    .getByRole('form', { name: /Tahsilat/ })
    .getByRole('button', { name: 'Tahsil et' })
    .click();

  const warning = page.getByRole('alert').filter({ hasText: 'Kira kaydı değişmiş' });
  await expect(warning).toContainText(STALE);
  await expect(page.getByText('Mükerrer işlem')).toHaveCount(0);
  await expect.poll(() => panel.sayi()).toBe(2);
  await expect(page.getByRole('form', { name: /Tahsilat/ })).toHaveCount(0);
  expect(submission).toBe(1);
  expect(errors).toEqual([]);
});

test.describe('mobil (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  test('320/390 px: gövde yatay taşması yok (tahsilat formu açıkken de)', async ({ page }) => {
    await logIn(page);
    await fakePanel(page, () => ozet(true));
    for (const width of [320, 390]) {
      await page.setViewportSize({ width: width, height: 844 });
      await page.goto(PANEL);
      await ready(page);
      expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      await page.getByRole('button', { name: '34 ABC 123 için tahsil et' }).click();
      await expect(page.getByRole('form', { name: /Tahsilat/ })).toBeVisible();
      expect(await measureOverflow(page), `${width}px form`).toEqual({ tasma: 0, suclular: [] });
    }
  });
});
