import { expect, test } from '@playwright/test';

import { BEN, ciddiIhlaller, hatalariTopla, oturumAc } from './ortak';
import { VEHICLE_1, reportEndpoints } from './report-fakes';
import { hazirBekle, tasmaOlc, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F10.2 rapor ekranları: TEK ortak rapor ekranı + rapor tanımları. Ortak akış (dönem süzgeci → istek, görünüm
 * değişimi, sıralama, export bağlantısı yalnız sunucu verdiyse), temsili raporlar, 403 mesajı, axe iki tema,
 * 320/390/768/1440 taşma. Sahte API; tutarlar elle kurulmuş (ekran hesap yapmaz).
 */
const AG_HATASI = [
  /Failed to load resource: the server responded with a status of 4\d\d/,
  /Failed to load resource: net::ERR_FAILED/,
];

const INCOME: VitrinSayfasi = {
  ad: 'rapor-gelir-gider',
  yol: '/app/raporlar/gelir-gider',
  baslik: 'Gelir-Gider Raporu',
  hazir: async (page) => {
    await expect(page.getByText('8.299,50 ₺')).toBeVisible();
  },
};
const CASH: VitrinSayfasi = {
  ad: 'rapor-kasa-banka',
  yol: '/app/raporlar/kasa-banka',
  baslik: 'Kasa/Banka Defteri',
  hazir: async (page) => {
    await expect(page.getByRole('gridcell', { name: 'TH-2' })).toBeVisible();
  },
};
const BALANCE: VitrinSayfasi = {
  ad: 'rapor-cari-bakiye',
  yol: '/app/raporlar/cari-bakiye',
  baslik: 'Cari Bakiye Raporu',
  hazir: async (page) => {
    await expect(page.getByRole('gridcell', { name: 'Deniz Ltd' })).toBeVisible();
  },
};
const PROFIT: VitrinSayfasi = {
  ad: 'rapor-karlilik',
  yol: '/app/raporlar/karlilik',
  baslik: 'Araç Kârlılığı',
  hazir: async (page) => {
    await expect(page.getByRole('link', { name: '34 ABC 123' })).toBeVisible();
  },
};
const PAGES = [INCOME, CASH, BALANCE, PROFIT];

test.beforeEach(async ({ page }) => {
  await oturumAc(page);
});

test('temsili raporlar: içerik + axe iki tema, konsol hatası yok', async ({ page }) => {
  const errors = hatalariTopla(page, AG_HATASI);
  await reportEndpoints(page);
  for (const s of PAGES) {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto(s.yol);
    await hazirBekle(page, s);
    expect(await ciddiIhlaller(page), `${s.ad} açık`).toEqual([]);
    await page.emulateMedia({ colorScheme: 'dark' });
    expect(await ciddiIhlaller(page), `${s.ad} koyu`).toEqual([]);
  }
  expect(errors).toEqual([]);
});

test('özet raporu: kartlar, kırılım tabloları, export bağlantısı sunucunun verdiği adresle', async ({
  page,
}) => {
  await reportEndpoints(page);
  await page.goto(INCOME.yol);
  await hazirBekle(page, INCOME);
  const summary = page.getByRole('definition');
  await expect(summary.filter({ hasText: '12.500,00 ₺' })).toHaveCount(1);
  await expect(page.getByRole('heading', { name: 'Gelir kırılımı' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/raporlar/export/gelir-gider?format=excel',
  );
  await expect(page.getByRole('link', { name: 'PDF' })).toHaveCount(0);
});

test('dönem süzgeci: İstanbul günleri bas/bit olarak gider, URL ve export taşır', async ({
  page,
}) => {
  const requests = await reportEndpoints(page);
  await page.goto(CASH.yol);
  await hazirBekle(page, CASH);
  await page.getByRole('textbox', { name: 'Dönem' }).fill('01.09.2026 – 30.09.2026');
  await page.getByRole('checkbox', { name: 'Devir satırı' }).check();
  await page.getByRole('button', { name: 'Raporla' }).click();
  await expect(page).toHaveURL(/bas=2026-09-01.*bit=2026-09-30/);
  await expect
    .poll(() => requests.at(-1)?.search ?? '')
    .toMatch(/bas=2026-09-01&bit=2026-09-30.*devir=true.*sayfa=1&boyut=50/);
  // Dışa aktarma: sunucunun bağlantısı + motorun kuralı (sayfa taşınmaz).
  await expect(page.getByRole('link', { name: 'CSV' })).toHaveAttribute(
    'href',
    '/raporlar/export/kasa-banka?format=csv&hesap=Kasa',
  );
});

test('görünüm değişimi ve sıralama: doğru uç, sayfa 1, sirala yalnız görünümün beyaz listesinden', async ({
  page,
}) => {
  const requests = await reportEndpoints(page);
  await page.goto(BALANCE.yol);
  await hazirBekle(page, BALANCE);
  await page.locator('th[data-kod="bakiye"] button').first().click();
  await expect(page).toHaveURL(/sirala=bakiye/);
  await expect.poll(() => requests.at(-1)?.searchParams.get('sirala')).toMatch(/^-?bakiye$/);
  await page.getByRole('button', { name: 'Yaşlandırma' }).click();
  await expect(page).toHaveURL(/gorunum=yaslandirma/);
  await expect
    .poll(() => requests.at(-1)?.pathname)
    .toBe('/api/ui/v1/raporlar/cari-bakiye/yaslandirma');
  expect(requests.at(-1)?.searchParams.get('sirala')).toBeNull();
  await expect(page.getByRole('gridcell', { name: 'Anonim müşteri' })).toBeVisible();
  // Export bağlantısı gelmedi → düğme yok.
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveCount(0);
});

test('kârlılık: plaka araç karnesine bağlanır', async ({ page }) => {
  await reportEndpoints(page);
  await page.goto(PROFIT.yol);
  await hazirBekle(page, PROFIT);
  await expect(page.getByRole('link', { name: '34 ABC 123' })).toHaveAttribute(
    'href',
    `/app/raporlar/arac-karne/${VEHICLE_1}`,
  );
  await expect(page.getByRole('gridcell', { name: '%62,5' })).toBeVisible();
});

test('şube kapsamlı kullanıcı: firma geneli rapor 403 → açık mesaj, tablo/özet yok', async ({
  page,
}) => {
  await oturumAc(page, {
    ...BEN,
    subeKapsami: {
      tumSubeler: false,
      subeId: 'e1e1e1e1-0000-4000-8000-000000000001',
      subeAd: 'Merkez',
    },
  });
  await reportEndpoints(page, { firmWideForbidden: true });
  await page.goto(INCOME.yol);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(INCOME.baslik);
  const alert = page.locator('[data-rapor-durum="yetki"]');
  await expect(alert).toContainText('Bu raporu görüntüleme yetkiniz yok.');
  await expect(alert).toContainText('firma genelidir');
  await expect(page.getByRole('definition')).toHaveCount(0);
});

test('izinsiz kullanıcı rapor rotasına giremez', async ({ page }) => {
  await oturumAc(page, { ...BEN, izinler: ['OperationsWrite'] });
  await reportEndpoints(page);
  await page.goto(INCOME.yol);
  await expect(page).toHaveURL(/\/app\/?$/);
});

for (const s of PAGES) {
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await reportEndpoints(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await hazirBekle(page, s);
        expect(await tasmaOlc(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await reportEndpoints(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await hazirBekle(page, s);
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
