import { expect, test, type Page, type Route } from '@playwright/test';

import {
  seriousViolations,
  collectErrors,
  kaydet,
  type KayitliIstek,
  logIn,
  problem,
} from './ortak';
import { VEHICLE_1, FLEET_1, CUSTOMER_1, fleetDetail, sharedEndpoints } from './planlama-sahte';
import { waitReady, measureOverflow, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F5.2b filo kiralama (`/app/filo-kiralama`, `/yeni`, `/:id`): liste, sunucu taksit planı, künye PUT'u
 * (surum + dokunulmayan 1995 tarihi AYNEN — #271 Low-1), 409 `cakisma` formu silmez, yeni sözleşme
 * doğrulama hatasında form korunur; axe + taşma.
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];

const ROW = {
  id: FLEET_1,
  no: 'FK-000001',
  sozlesmeNo: 'S-1',
  musteriId: CUSTOMER_1,
  musteriAd: 'Ayşe Yılmaz',
  vehicleId: VEHICLE_1,
  plaka: '34ABC123',
  basTar: '2026-09-30T21:00:00Z',
  sureAy: 3,
  aylikUcret: 1000,
  genelToplam: 3650,
  doviz: 'TRY',
  satisTemsilcisi: 'Ali',
  kaynak: null,
  vadeGun: 30,
  durum: 'Aktif',
};

const LISTE: VitrinSayfasi = {
  ad: 'filo-kiralama',
  yol: '/app/filo-kiralama',
  baslik: 'Filo / Uzun Dönem Kiralama',
  hazir: async (page) => {
    await expect(page.getByRole('link', { name: 'FK-000001' })).toBeVisible();
  },
};
const DETAIL: VitrinSayfasi = {
  ad: 'filo-detay',
  yol: `/app/filo-kiralama/${FLEET_1}`,
  baslik: 'Filo sözleşmesi FK-000001',
  hazir: async (page) => {
    await expect(page.getByRole('textbox', { name: 'Satış temsilcisi' })).toHaveValue('Ali');
  },
};
const YENI: VitrinSayfasi = {
  ad: 'filo-yeni',
  yol: '/app/filo-kiralama/yeni',
  baslik: 'Yeni Filo Sözleşmesi',
};

async function fleetEndpoints(
  page: Page,
  option: { detay?: () => unknown; yazma?: (route: Route) => Promise<void> | void } = {},
): Promise<KayitliIstek[]> {
  const written: KayitliIstek[] = [];
  await page.route(
    (url) => url.pathname === '/api/ui/v1/filo-kiralama',
    (route) => {
      if (route.request().method() === 'GET')
        return route.fulfill({ json: { kayitlar: [ROW], toplam: 1, sayfaNo: 1, boyut: 50 } });
      written.push(kaydet(route.request()));
      return (
        option.yazma?.(route) ??
        route.fulfill({ status: 201, json: { id: FLEET_1, no: 'FK-000001' } })
      );
    },
  );
  await page.route(
    (url) => url.pathname.startsWith(`/api/ui/v1/filo-kiralama/${FLEET_1}`),
    (route) => {
      if (route.request().method() === 'GET')
        return route.fulfill({ json: option.detay?.() ?? fleetDetail() });
      written.push(kaydet(route.request()));
      return option.yazma?.(route) ?? route.fulfill({ json: fleetDetail() });
    },
  );
  return written;
}

test.beforeEach(async ({ page }) => {
  await logIn(page);
  await sharedEndpoints(page);
});

test('liste → detay: sunucu taksit planı ve genel toplam, dışa aktarma Blazor ucu; axe iki tema', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await fleetEndpoints(page);
  await page.goto(LISTE.yol);
  await waitReady(page, LISTE);
  await expect(page.getByText('1 sözleşme')).toBeVisible();
  await expect(page.getByRole('gridcell', { name: '3.650,00 ₺' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/filo-kiralama?format=excel',
  );
  expect(await seriousViolations(page), 'liste').toEqual([]);

  await page.getByRole('link', { name: 'FK-000001' }).click();
  await expect(page).toHaveURL(new RegExp(`/app/filo-kiralama/${FLEET_1}$`));
  await waitReady(page, DETAIL);
  const plan = page.getByRole('region', { name: 'Taksit planı tablosu' });
  await expect(plan.getByRole('row')).toHaveCount(1 + 3 + 3);
  await expect(plan).toContainText('3.650,00 ₺');
  await expect(page.getByRole('button', { name: 'İptal et' })).toHaveCount(0); // yetkiler.iptal=false
  expect(await seriousViolations(page), 'detay açık').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await seriousViolations(page), 'detay koyu').toEqual([]);
  expect(errors).toEqual([]);
});

test('künye: surum + dokunulmayan 1995 tarihi AYNEN; 409 cakisma formu silmez, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let version = 'surum-1';
  let source: string | null = null;
  let put = 0;
  const written = await fleetEndpoints(page, {
    detay: () => fleetDetail({ surum: version, kaynak: source }),
    yazma: (r) => {
      if (++put === 1) {
        version = 'surum-2';
        source = 'Web'; // başka oturum
        return problem(
          r,
          409,
          'cakisma',
          'Sözleşme siz düzenlerken değişti; güncel hâli yükleyin.',
        );
      }
      return r.fulfill({
        json: fleetDetail({ surum: 'surum-3', kaynak: source, aciklama: 'yalnız açıklama' }),
      });
    },
  });
  await page.goto(DETAIL.yol);
  await waitReady(page, DETAIL);
  const description = page.getByRole('textbox', { name: 'Açıklama' });
  await description.fill('yalnız açıklama');
  await page.getByRole('button', { name: 'Künyeyi kaydet' }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Sözleşme siz düzenlerken değişti');
  await expect(description).toHaveValue('yalnız açıklama');
  await expect(page.getByRole('textbox', { name: 'Kaynak' })).toHaveValue('Web');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    surum: 'surum-1',
    sozlesmeTarihi: '1995-03-10T00:00:00Z',
    aciklama: 'yalnız açıklama',
  });

  await page.getByRole('button', { name: 'Künyeyi kaydet' }).click();
  await expect(page.getByText('Künye kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({
    surum: 'surum-2',
    kaynak: 'Web',
    sozlesmeTarihi: '1995-03-10T00:00:00Z',
  });
  expect(errors).toEqual([]);
});

test('yeni sözleşme: doğrulama hatasında form korunur; başarıda detaya gider', async ({ page }) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let n = 0;
  const written = await fleetEndpoints(page, {
    yazma: (r) =>
      ++n === 1
        ? problem(r, 400, 'dogrulama', 'Aylık ücret en fazla …', {
            errors: { aylikUcret: ['Aylık ücret çok büyük.'] },
          })
        : r.fulfill({ status: 201, json: { id: FLEET_1, no: 'FK-000001' } }),
  });
  await page.goto(YENI.yol);
  await waitReady(page, YENI);
  await page.getByRole('combobox', { name: 'Müşteri' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  await page.getByRole('combobox', { name: 'Araç' }).fill('34');
  await page.getByRole('option', { name: '34ABC123' }).click();
  await page.getByRole('textbox', { name: 'Süre (ay)' }).fill('12');
  await page.getByRole('textbox', { name: 'Aylık ücret' }).fill('15.000,50');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  await expect(page.getByRole('textbox', { name: 'Aylık ücret' })).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  await expect(page.getByText('Aylık ücret çok büyük.')).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Aylık ücret' })).toHaveValue('15.000,50');
  await expect(page.getByRole('combobox', { name: 'Müşteri' })).toHaveValue('Ayşe Yılmaz');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    musteriId: CUSTOMER_1,
    vehicleId: VEHICLE_1,
    sureAy: 12,
    aylikUcret: '15000.50',
    kdvOrani: 0.2,
  });
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByRole('textbox', { name: 'Aylık ücret' }).fill('1.500');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/app/filo-kiralama/${FLEET_1}$`));
  await expect(page.getByText('FK-000001 numaralı filo sözleşmesi oluşturuldu.')).toBeVisible();
  expect(errors).toEqual([]);
});

for (const pageRef of [LISTE, DETAIL, YENI]) {
  test.describe(`${pageRef.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${pageRef.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await fleetEndpoints(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width: width, height: 844 });
        await page.goto(pageRef.yol);
        await waitReady(page, pageRef);
        expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${pageRef.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await fleetEndpoints(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(pageRef.yol);
    await waitReady(page, pageRef);
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
