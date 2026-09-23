import { expect, test, type Page, type Route } from '@playwright/test';

import {
  ciddiIhlaller,
  hatalariTopla,
  kaydet,
  type KayitliIstek,
  oturumAc,
  problem,
} from './ortak';
import { ARAC_1, FILO_1, MUSTERI_1, filoDetay, ortakUclar } from './planlama-sahte';
import { hazirBekle, tasmaOlc, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F5.2b filo kiralama (`/app/filo-kiralama`, `/yeni`, `/:id`): liste, sunucu taksit planı, künye PUT'u
 * (surum + dokunulmayan 1995 tarihi AYNEN — #271 Low-1), 409 `cakisma` formu silmez, yeni sözleşme
 * doğrulama hatasında form korunur; axe + taşma.
 */
const AG_HATASI = [/Failed to load resource: the server responded with a status of 4\d\d/];

const SATIR = {
  id: FILO_1,
  no: 'FK-000001',
  sozlesmeNo: 'S-1',
  musteriId: MUSTERI_1,
  musteriAd: 'Ayşe Yılmaz',
  vehicleId: ARAC_1,
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
const DETAY: VitrinSayfasi = {
  ad: 'filo-detay',
  yol: `/app/filo-kiralama/${FILO_1}`,
  baslik: 'Filo sözleşmesi FK-000001 Aktif',
  hazir: async (page) => {
    await expect(page.getByRole('textbox', { name: 'Satış temsilcisi' })).toHaveValue('Ali');
  },
};
const YENI: VitrinSayfasi = {
  ad: 'filo-yeni',
  yol: '/app/filo-kiralama/yeni',
  baslik: 'Yeni Filo Sözleşmesi',
};

async function filoUclari(
  page: Page,
  secenek: { detay?: () => unknown; yazma?: (route: Route) => Promise<void> | void } = {},
): Promise<KayitliIstek[]> {
  const yazilan: KayitliIstek[] = [];
  await page.route(
    (url) => url.pathname === '/api/ui/v1/filo-kiralama',
    (route) => {
      if (route.request().method() === 'GET')
        return route.fulfill({ json: { kayitlar: [SATIR], toplam: 1, sayfaNo: 1, boyut: 50 } });
      yazilan.push(kaydet(route.request()));
      return (
        secenek.yazma?.(route) ??
        route.fulfill({ status: 201, json: { id: FILO_1, no: 'FK-000001' } })
      );
    },
  );
  await page.route(
    (url) => url.pathname.startsWith(`/api/ui/v1/filo-kiralama/${FILO_1}`),
    (route) => {
      if (route.request().method() === 'GET')
        return route.fulfill({ json: secenek.detay?.() ?? filoDetay() });
      yazilan.push(kaydet(route.request()));
      return secenek.yazma?.(route) ?? route.fulfill({ json: filoDetay() });
    },
  );
  return yazilan;
}

test.beforeEach(async ({ page }) => {
  await oturumAc(page);
  await ortakUclar(page);
});

test('liste → detay: sunucu taksit planı ve genel toplam, dışa aktarma Blazor ucu; axe iki tema', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await filoUclari(page);
  await page.goto(LISTE.yol);
  await hazirBekle(page, LISTE);
  await expect(page.getByText('1 sözleşme')).toBeVisible();
  await expect(page.getByRole('gridcell', { name: '3.650,00 ₺' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/filo-kiralama?format=excel',
  );
  expect(await ciddiIhlaller(page), 'liste').toEqual([]);

  await page.getByRole('link', { name: 'FK-000001' }).click();
  await expect(page).toHaveURL(new RegExp(`/app/filo-kiralama/${FILO_1}$`));
  await hazirBekle(page, DETAY);
  const plan = page.getByRole('region', { name: 'Taksit planı tablosu' });
  await expect(plan.getByRole('row')).toHaveCount(1 + 3 + 3);
  await expect(plan).toContainText('3.650,00 ₺');
  await expect(page.getByRole('button', { name: 'İptal et' })).toHaveCount(0); // yetkiler.iptal=false
  expect(await ciddiIhlaller(page), 'detay açık').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await ciddiIhlaller(page), 'detay koyu').toEqual([]);
  expect(hatalar).toEqual([]);
});

test('künye: surum + dokunulmayan 1995 tarihi AYNEN; 409 cakisma formu silmez, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let surum = 'surum-1';
  let kaynak: string | null = null;
  let put = 0;
  const yazilan = await filoUclari(page, {
    detay: () => filoDetay({ surum, kaynak }),
    yazma: (r) => {
      if (++put === 1) {
        surum = 'surum-2';
        kaynak = 'Web'; // başka oturum
        return problem(
          r,
          409,
          'cakisma',
          'Sözleşme siz düzenlerken değişti; güncel hâli yükleyin.',
        );
      }
      return r.fulfill({
        json: filoDetay({ surum: 'surum-3', kaynak, aciklama: 'yalnız açıklama' }),
      });
    },
  });
  await page.goto(DETAY.yol);
  await hazirBekle(page, DETAY);
  const aciklama = page.getByRole('textbox', { name: 'Açıklama' });
  await aciklama.fill('yalnız açıklama');
  await page.getByRole('button', { name: 'Künyeyi kaydet' }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Sözleşme siz düzenlerken değişti');
  await expect(aciklama).toHaveValue('yalnız açıklama');
  await expect(page.getByRole('textbox', { name: 'Kaynak' })).toHaveValue('Web');
  expect(JSON.parse(yazilan[0]?.govde ?? '{}')).toMatchObject({
    surum: 'surum-1',
    sozlesmeTarihi: '1995-03-10T00:00:00Z',
    aciklama: 'yalnız açıklama',
  });

  await page.getByRole('button', { name: 'Künyeyi kaydet' }).click();
  await expect(page.getByText('Künye kaydedildi.')).toBeVisible();
  expect(JSON.parse(yazilan[1]?.govde ?? '{}')).toMatchObject({
    surum: 'surum-2',
    kaynak: 'Web',
    sozlesmeTarihi: '1995-03-10T00:00:00Z',
  });
  expect(hatalar).toEqual([]);
});

test('yeni sözleşme: doğrulama hatasında form korunur; başarıda detaya gider', async ({ page }) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let n = 0;
  const yazilan = await filoUclari(page, {
    yazma: (r) =>
      ++n === 1
        ? problem(r, 400, 'dogrulama', 'Aylık ücret en fazla …', {
            errors: { aylikUcret: ['Aylık ücret çok büyük.'] },
          })
        : r.fulfill({ status: 201, json: { id: FILO_1, no: 'FK-000001' } }),
  });
  await page.goto(YENI.yol);
  await hazirBekle(page, YENI);
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
  expect(JSON.parse(yazilan[0]?.govde ?? '{}')).toMatchObject({
    musteriId: MUSTERI_1,
    vehicleId: ARAC_1,
    sureAy: 12,
    aylikUcret: '15000.50',
    kdvOrani: 0.2,
  });
  expect(await ciddiIhlaller(page)).toEqual([]);

  await page.getByRole('textbox', { name: 'Aylık ücret' }).fill('1.500');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/app/filo-kiralama/${FILO_1}$`));
  await expect(page.getByText('FK-000001 numaralı filo sözleşmesi oluşturuldu.')).toBeVisible();
  expect(hatalar).toEqual([]);
});

for (const sayfa of [LISTE, DETAY, YENI]) {
  test.describe(`${sayfa.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${sayfa.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await filoUclari(page);
      for (const genislik of [320, 390, 768]) {
        await page.setViewportSize({ width: genislik, height: 844 });
        await page.goto(sayfa.yol);
        await hazirBekle(page, sayfa);
        expect(await tasmaOlc(page), `${genislik}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${sayfa.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await filoUclari(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(sayfa.yol);
    await hazirBekle(page, sayfa);
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
