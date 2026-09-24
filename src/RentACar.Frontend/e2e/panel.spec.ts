import { expect, test, type Page } from '@playwright/test';

import { BEN, ciddiIhlaller, hatalariTopla, oturumAc, problem } from './ortak';
import { tasmaOlc } from './vitrin-sayfalari';

/**
 * F4.5 Panel (sahte `/api/ui/v1`, üretim derlemesi + CSP): gecikme varsayılanlı sekme, finans kapısı
 * (yanıtta yoksa çizilmez), "Tahsil Et" para kuralları (anahtar aynen, kilit, 409'da yeniden gönderim yok),
 * iki temada axe, 390 px taşma.
 */
const PANEL = '/app/panel';
const ANAHTAR = '0f9b2c1e-6a1d-5b7e-9c3a-2d4e6f8a0b1c';
const BAYAT =
  'Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti ya da tahsilat anahtarı bu kiraya ait değil; kaydı yeniden yükleyip tekrar deneyin.';
const AG_HATASI = [/Failed to load resource: the server responded with a status of 4\d\d/];

const kademe = { yediGun: 1, otuzGun: 2, gecmis: 0 };

function donus(no: string, tahsilat: boolean) {
  return {
    rentalId: `kira-${no}`,
    sozlesmeNo: no,
    tarih: '2026-09-21T09:00:00Z',
    musteriAd: 'Ayşe Yılmaz',
    plaka: '34 ABC 123',
    ofis: 'Merkez Ofis',
    bakiye: 1250.5,
    doviz: 'TRY',
    tahsilat: tahsilat
      ? {
          anahtar: ANAHTAR,
          cariId: 'cari-1',
          rentalId: `kira-${no}`,
          doviz: 'TRY',
          varsayilanTutar: 1250.5,
        }
      : null,
  };
}

function ozet(finans: boolean, tahsilat = true) {
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
      trafik: kademe,
      kasko: kademe,
      muayene: { yediGun: 0, otuzGun: 0, gecmis: 1 },
      gecmisUyari: 1,
      yaklasanUyari: 3,
      acikSikayet: 1,
    },
    donusler: {
      gecikmis: [donus('2026200901001', tahsilat)],
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
    finans: finans
      ? {
          kasaBakiye: 15250.75,
          bankaBakiye: 128400,
          acikBakiye: 9320.4,
          bugunTahsilatTutar: 4200,
          bugunTahsilatAdet: 3,
          filoDolulukYuzde: 64.2,
          revPacd: 820,
          adr: 1450,
          gelirTrendi: [4, 5, 6, 7, 8, 9].map((ay, i) => ({
            ayBas: `2026-0${ay}-01T00:00:00+03:00`,
            gelir: 90000 + i * 12500,
          })),
        }
      : null,
  };
}

/** Panel ucu sahtesi; çağrı sayısını döner. */
async function paneliSahtele(page: Page, yanit: () => object): Promise<{ sayi: () => number }> {
  let sayi = 0;
  await page.route('**/api/ui/v1/panel/ozet', (route) => {
    sayi++;
    return route.fulfill({ json: yanit() });
  });
  await page.route('**/api/ui/v1/finans/hesaplar', (route) =>
    route.fulfill({
      json: [
        { id: 'h-1', etiket: 'Kasa · Merkez', kod: 'K1', ad: 'Merkez', tur: 'Kasa', doviz: null },
      ],
    }),
  );
  return { sayi: () => sayi };
}

async function hazir(page: Page): Promise<void> {
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Panel');
  await expect(page.getByRole('heading', { name: 'Dönüşler' })).toBeVisible();
  await page.waitForFunction(() => document.fonts.status === 'loaded');
}

test('gecikmiş dönüş varsa Gecikmiş açılır; satır kira formuna bağlanır; iki temada axe temiz', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await oturumAc(page);
  await paneliSahtele(page, () => ozet(true));
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto(PANEL);
  await hazir(page);

  const donusler = page.getByRole('region', { name: 'Dönüşler' });
  await expect(donusler.getByRole('button', { name: /Gecikmiş/ })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(donusler.getByRole('link', { name: /2026200901001/ })).toHaveAttribute(
    'href',
    '/app/kiralar/kira-2026200901001#sekme=donus',
  );
  const cikislar = page.getByRole('region', { name: 'Çıkışlar' });
  await expect(cikislar.getByRole('button', { name: /Bugün/ })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(cikislar.getByRole('cell', { name: 'Mehmet Işık' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Finans özeti' })).toBeVisible();
  expect(await ciddiIhlaller(page), 'açık tema').toEqual([]);

  await page.emulateMedia({ colorScheme: 'dark' });
  await expect
    .poll(() => page.evaluate(() => getComputedStyle(document.body).backgroundColor))
    .toBe('rgb(13, 19, 28)');
  expect(await ciddiIhlaller(page), 'koyu tema').toEqual([]);

  // Sekme seçimi sorguya yazılır (Blazor ?df= sözleşmesi; yenilemede korunur).
  await donusler.getByRole('button', { name: /Bugün/ }).click();
  await expect(page).toHaveURL(/\/app\/panel\?df=bugun$/);
  await expect(donusler.getByRole('link', { name: /2026220901002/ })).toBeVisible();
  await page.reload();
  await hazir(page);
  await expect(
    page.getByRole('region', { name: 'Dönüşler' }).getByRole('button', { name: /Bugün/ }),
  ).toHaveAttribute('aria-pressed', 'true');
  expect(hatalar).toEqual([]);
});

test('finans yetkisi yoksa (yanıtta finans/tahsilat yok) finans bloğu ve Tahsil Et çizilmez', async ({
  page,
}) => {
  await oturumAc(page, { ...BEN, izinler: ['OperationsWrite'] });
  await paneliSahtele(page, () => ozet(false, false));
  await page.goto(PANEL);
  await hazir(page);
  await expect(page.getByRole('cell', { name: 'Ayşe Yılmaz' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Finans özeti' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /tahsil et/i })).toHaveCount(0);
});

test('F5.4 kesiş: panelin rezervasyon/müsaitlik bağlantıları SPA rotası (Blazor sayfasına düşmez)', async ({
  page,
}) => {
  await oturumAc(page);
  await paneliSahtele(page, () => ozet(true));
  await page.goto(PANEL);
  await hazir(page);

  // Sayfa içeriğinde kesiş haritasındaki bir Blazor sayfasına (F4 + F5 + F11 gelen talepler) giden bağlantı kalmadı.
  const blazora = await page.locator('main a[href]').evaluateAll((ogeler) =>
    ogeler
      .map((o) => new URL((o as HTMLAnchorElement).href, location.href))
      .filter((u) => u.origin === location.origin)
      .map((u) => u.pathname.replace(/\/$/, '') || '/')
      .filter((yol) =>
        /^\/(|kiralar(\/.*)?|rezervasyonlar|teklifler|takvim|musaitlik|rez-sartlari|filo-kiralama|gelen-talepler)$/i.test(
          yol,
        ),
      ),
  );
  expect(blazora).toEqual([]);

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
  const hatalar = hatalariTopla(page);
  await oturumAc(page);
  const panel = await paneliSahtele(page, () => ozet(true));
  const govdeler: unknown[] = [];
  const basliklar: (string | undefined)[] = [];
  let birak: () => void = () => undefined;
  const bekle = new Promise<void>((r) => (birak = r));
  await page.route('**/api/ui/v1/finans/tahsilat', async (route) => {
    govdeler.push(route.request().postDataJSON());
    basliklar.push(route.request().headers()['idempotency-key']);
    await bekle;
    return route.fulfill({ json: { id: 'islem-1' } });
  });
  await page.goto(PANEL);
  await hazir(page);
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
  birak();

  await expect(page.getByRole('status').filter({ hasText: 'Tahsilat kaydedildi' })).toBeVisible();
  await expect.poll(() => panel.sayi()).toBe(2);
  await expect(form).toHaveCount(0);
  expect(govdeler).toEqual([
    {
      cariId: 'cari-1',
      kiraId: 'kira-2026200901001',
      tutar: '1000.00',
      hesap: 'Kasa',
      hesapId: null,
      doviz: 'TRY',
      kanal: 'Masaüstü',
      aciklama: 'Hızlı tahsilat (pano) — 34 ABC 123',
      tahsilatAnahtar: ANAHTAR,
    },
  ]);
  expect(basliklar).toEqual([undefined]);
  expect(hatalar).toEqual([]);
});

test('Tahsil Et 409 mukerrer (bayat anahtar): yeniden gönderilmez, panel yeniden yüklenir, sunucunun detail’ı gösterilir', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await oturumAc(page);
  const panel = await paneliSahtele(page, () => ozet(true));
  let gonderim = 0;
  await page.route('**/api/ui/v1/finans/tahsilat', (route) => {
    gonderim++;
    return problem(route, 409, 'mukerrer', BAYAT);
  });
  await page.goto(PANEL);
  await hazir(page);
  await page.getByRole('button', { name: '34 ABC 123 için tahsil et' }).click();
  await page
    .getByRole('form', { name: /Tahsilat/ })
    .getByRole('button', { name: 'Tahsil et' })
    .click();

  const uyari = page.getByRole('alert').filter({ hasText: 'Kira kaydı değişmiş' });
  await expect(uyari).toContainText(BAYAT);
  await expect(page.getByText('Mükerrer işlem')).toHaveCount(0);
  await expect.poll(() => panel.sayi()).toBe(2);
  await expect(page.getByRole('form', { name: /Tahsilat/ })).toHaveCount(0);
  expect(gonderim).toBe(1);
  expect(hatalar).toEqual([]);
});

test.describe('mobil (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  test('320/390 px: gövde yatay taşması yok (tahsilat formu açıkken de)', async ({ page }) => {
    await oturumAc(page);
    await paneliSahtele(page, () => ozet(true));
    for (const genislik of [320, 390]) {
      await page.setViewportSize({ width: genislik, height: 844 });
      await page.goto(PANEL);
      await hazir(page);
      expect(await tasmaOlc(page), `${genislik}px`).toEqual({ tasma: 0, suclular: [] });
      await page.getByRole('button', { name: '34 ABC 123 için tahsil et' }).click();
      await expect(page.getByRole('form', { name: /Tahsilat/ })).toBeVisible();
      expect(await tasmaOlc(page), `${genislik}px form`).toEqual({ tasma: 0, suclular: [] });
    }
  });
});
