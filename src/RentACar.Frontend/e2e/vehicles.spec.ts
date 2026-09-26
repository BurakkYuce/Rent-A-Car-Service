import { expect, test, type Page } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, logIn, problem, writeXsrf } from './ortak';
import { OWNER_1, VEHICLE_1, vehicleCard, vehicleEndpoints } from './vehicle-fakes';
import { waitReady, measureOverflow, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F6.2a araç ekranları: liste (49 sütun, sabit plaka, modele göre grupla), detaylı liste, kart (yeni/düzenle +
 * foto), detay, durum panosu, tanımlar. Üç zorunlu senaryo (doğrulama hatasında form korunur, oturum düşünce
 * form kaybolmaz, `cakisma` formu silmez) + axe iki tema + 320/390/768/1440 taşma.
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];

const LIST: VitrinSayfasi = {
  ad: 'araclar',
  yol: '/app/araclar',
  baslik: 'Araç Listesi',
  hazir: async (page) => {
    await expect(page.getByRole('link', { name: '34 ABC 123' }).first()).toBeVisible();
  },
};
const DETAILED: VitrinSayfasi = {
  ad: 'araclar-detayli',
  yol: '/app/araclar/detayli',
  baslik: 'Detaylı Araç Listesi',
  hazir: async (page) => {
    await expect(page.getByRole('link', { name: '34 ABC 123' })).toBeVisible();
  },
};
const NEW: VitrinSayfasi = { ad: 'arac-yeni', yol: '/app/araclar/yeni', baslik: 'Yeni Araç' };
const EDIT: VitrinSayfasi = {
  ad: 'arac-kart',
  yol: `/app/araclar/${VEHICLE_1}`,
  baslik: '34ABC123 — Araç Düzenle',
  hazir: async (page) => {
    await expect(page.getByRole('textbox', { name: 'Plaka' })).toHaveValue('34ABC123');
  },
};
const DETAIL: VitrinSayfasi = {
  ad: 'arac-detay',
  yol: `/app/araclar/${VEHICLE_1}/detay`,
  baslik: '34ABC123 Fiat',
  hazir: async (page) => {
    await expect(page.getByRole('link', { name: '2026230901001' })).toBeVisible();
  },
};
const BOARD: VitrinSayfasi = {
  ad: 'arac-durum',
  yol: '/app/arac-durum',
  baslik: 'Araç Güncel Durum',
  hazir: async (page) => {
    await expect(page.getByText("1 araç • 1 kirada • 0 serviste • 0 BAF'ta")).toBeVisible();
  },
};
const OWNERS: VitrinSayfasi = {
  ad: 'arac-sahipleri',
  yol: '/app/arac-sahipleri',
  baslik: 'Araç Sahip Tanımları',
  hazir: async (page) => {
    await expect(page.getByRole('cell', { name: 'Bizim Filo' })).toBeVisible();
  },
};

async function ownerEndpoints(page: Page, owner: () => Record<string, unknown>) {
  const puts: string[] = [];
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/arac-sahipleri'),
    async (r) => {
      if (r.request().method() === 'PUT') {
        puts.push(r.request().postData() ?? '');
        return r.fulfill({ json: owner() });
      }
      const path = new URL(r.request().url()).pathname;
      if (path === '/api/ui/v1/arac-sahipleri')
        return r.fulfill({
          json: { kayitlar: [{ ...owner(), surum: null }], toplam: 1, sayfaNo: 1, boyut: 200 },
        });
      return r.fulfill({ json: owner() });
    },
  );
  return puts;
}

const owner = (extra: Record<string, unknown> = {}) => ({
  id: OWNER_1,
  kod: 'BIZIM',
  ad: 'Bizim Filo',
  tur: 'Bizim',
  aktif: true,
  surum: 'o-1',
  ...extra,
});

test.beforeEach(async ({ page }) => {
  await logIn(page, { ...BEN, izinler: [...BEN.izinler, 'OperationsDelete'] });
});

test('liste: sabit plaka, sağa yaslı para, dışa aktarma; modele göre grupla; axe iki tema', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await vehicleEndpoints(page);
  await page.goto(LIST.yol);
  await waitReady(page, LIST);
  await expect(page.getByText('Kayıt: 1')).toBeVisible();
  await expect(page.getByRole('gridcell', { name: '850.000,50 ₺' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/araclar?format=excel',
  );
  expect(await seriousViolations(page), 'liste açık').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await seriousViolations(page), 'liste koyu').toEqual([]);

  await page.getByRole('button', { name: 'Modele göre grupla' }).click();
  await expect(page).toHaveURL(/gorunum=grup/);
  await expect(page.locator('summary').getByText('Fiat Egea')).toBeVisible();
  // Görünüm düğmesinin renk geçişi bitmeden axe ara rengi ölçer.
  await page.mouse.move(0, 0);
  await page.locator('body').click({ position: { x: 1, y: 1 } });
  await page.waitForFunction(() => document.getAnimations().length === 0);
  expect(await seriousViolations(page), 'gruplu').toEqual([]);
  expect(errors).toEqual([]);
});

test('yeni araç: doğrulama hatasında form korunur; başarıda karta gider', async ({ page }) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let n = 0;
  const written = await vehicleEndpoints(page, {
    write: (r) =>
      ++n === 1
        ? problem(r, 400, 'dogrulama', 'Plaka zaten kayıtlı.', {
            errors: { plaka: ['34 ABC 123 plakası zaten kayıtlı.'] },
          })
        : r.fulfill({ status: 201, json: vehicleCard() }),
  });
  await page.goto(NEW.yol);
  await waitReady(page, NEW);
  await expect(page.getByRole('combobox', { name: 'Grup' })).toHaveValue('Ekonomik');
  await page.getByRole('textbox', { name: 'Plaka' }).fill('34ABC123');
  await page.getByRole('combobox', { name: 'Marka' }).fill('Fiat');
  await page.getByRole('button', { name: 'Oluştur' }).click();

  const plate = page.getByRole('textbox', { name: 'Plaka' });
  await expect(plate).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByText('34 ABC 123 plakası zaten kayıtlı.')).toBeVisible();
  await expect(plate).toHaveValue('34ABC123');
  await expect(page.getByRole('combobox', { name: 'Marka' })).toHaveValue('Fiat');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    plaka: '34ABC123',
    marka: 'Fiat',
    grup: 'Ekonomik',
    grupBilincliBos: false,
    durum: 'Musait',
    km: 0,
    yakit: null,
  });
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByRole('button', { name: 'Oluştur' }).click();
  await expect(page).toHaveURL(new RegExp(`/app/araclar/${VEHICLE_1}$`));
  await expect(page.getByText('34ABC123 plakalı araç oluşturuldu.')).toBeVisible();
  expect(errors).toEqual([]);
});

test('kart: oturum düşünce form kaybolmaz — yerinde giriş, AYNI istek (aynı anahtar) tekrarlanır', async ({
  page,
}) => {
  let n = 0;
  const written = await vehicleEndpoints(page, {
    write: (r) =>
      ++n === 1
        ? problem(r, 401, 'oturum_yok', 'Oturum açık değil.')
        : r.fulfill({ json: vehicleCard({ surum: 'surum-2', aciklama: 'yeni not' }) }),
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await writeXsrf(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await writeXsrf(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });
  await page.goto(EDIT.yol);
  await waitReady(page, EDIT);
  await page.getByRole('tab', { name: 'Kart derinliği' }).click();
  await page.getByRole('textbox', { name: 'Açıklama' }).fill('yeni not');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Açıklama', includeHidden: true })).toHaveValue(
    'yeni not',
  );
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByText('34ABC123 plakalı araç kaydedildi.')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
});

test('kart: cakisma formu silmez — güncel kart birleşir, dokunulmayan tarih aynen, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let version = 'surum-1';
  let branch = 'Merkez';
  let put = 0;
  const written = await vehicleEndpoints(page, {
    card: () => vehicleCard({ surum: version, sube: branch }),
    write: (r) => {
      if (++put === 1) {
        version = 'surum-2';
        branch = 'Havalimanı'; // başka oturum şubeyi değiştirdi
        return problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti; güncel hâli yükleyin.');
      }
      return r.fulfill({ json: vehicleCard({ surum: 'surum-3', sube: branch, marka: 'Renault' }) });
    },
  });
  await page.goto(EDIT.yol);
  await waitReady(page, EDIT);
  const brand = page.getByRole('combobox', { name: 'Marka' });
  await brand.fill('Renault');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt siz düzenlerken değişti');
  await expect(brand).toHaveValue('Renault'); // form SİLİNMEDİ
  await expect(page.getByRole('combobox', { name: 'Şube' })).toHaveValue('Havalimanı');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    surum: 'surum-1',
    marka: 'Renault',
    sube: 'Merkez',
    tescilTarihi: '1995-03-09T22:00:00Z',
  });

  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText('34ABC123 plakalı araç kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({
    surum: 'surum-2',
    marka: 'Renault',
    sube: 'Havalimanı',
    tescilTarihi: '1995-03-09T22:00:00Z',
  });
  expect(errors).toEqual([]);
});

test('detay, detaylı liste, durum panosu, foto sekmesi: içerik + axe', async ({ page }) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await vehicleEndpoints(page);
  for (const s of [DETAIL, DETAILED, BOARD]) {
    await page.goto(s.yol);
    await waitReady(page, s);
    expect(await seriousViolations(page), s.ad).toEqual([]);
  }
  await expect(page.getByRole('gridcell', { name: '2 gün gecikme' })).toBeVisible();
  await expect(page.getByRole('gridcell', { name: 'Ayşe Yılmaz' })).toBeVisible();

  await page.goto(`${EDIT.yol}#sekme=fotograflar`);
  await waitReady(page, { ...EDIT, hazir: undefined });
  await expect(page.getByText('1/20 fotoğraf')).toBeVisible();
  await expect(page.getByText('Kapak', { exact: true })).toBeVisible();
  expect(await seriousViolations(page), 'foto').toEqual([]);
  expect(errors).toEqual([]);
});

test('tanım (araç sahipleri): düzenle → tekil okunan surum ile PUT; aktif durum korunur', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const puts = await ownerEndpoints(page, () => owner());
  await page.goto(OWNERS.yol);
  await waitReady(page, OWNERS);
  expect(await seriousViolations(page)).toEqual([]);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  await page.getByRole('textbox', { name: 'Tür' }).fill('Dış');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => puts.length).toBe(1);
  expect(JSON.parse(puts[0] ?? '{}')).toEqual({
    kod: 'BIZIM',
    ad: 'Bizim Filo',
    tur: 'Dış',
    aktif: true,
    surum: 'o-1',
  });
  expect(errors).toEqual([]);
});

for (const s of [LIST, DETAILED, NEW, EDIT, DETAIL, BOARD, OWNERS]) {
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await vehicleEndpoints(page);
      await ownerEndpoints(page, () => owner());
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await waitReady(page, s);
        expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await vehicleEndpoints(page);
    await ownerEndpoints(page, () => owner());
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await waitReady(page, s);
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
