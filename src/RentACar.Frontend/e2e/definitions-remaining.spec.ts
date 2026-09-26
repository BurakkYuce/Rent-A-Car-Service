import { expect, test } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, logIn, problem, writeXsrf } from './ortak';
import { definitionEndpoints } from './definition-fakes';
import {
  GROUP_1,
  SOURCE_1,
  SOURCE_2,
  TEMPLATE_1,
  insurer,
  pagedEndpoints,
  source,
  template,
  vatRate,
  vehicleGroupEndpoints,
} from './definition-remaining-fakes';
import { waitReady, measureOverflow, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F11.2c kalan tanım ekranları: sayfalı F11.1b uçları (satır sürümsüz → düzenlemede tekil okuma, PUT'a `surum`),
 * genel F11.1a uçları, rezervasyon kaynağı "Aşağıya Yansıt", araç grubu eşleme; üç zorunlu senaryo + axe + taşma.
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];

const SOURCES: VitrinSayfasi = {
  ad: 'rezervasyon-kaynaklari',
  yol: '/app/rezervasyon-kaynaklari',
  baslik: 'Rezervasyon Kaynağı Tanımları',
  hazir: async (page) => {
    await expect(page.getByRole('cell', { name: 'Web Sitesi', exact: true })).toBeVisible();
  },
};
const GROUPS: VitrinSayfasi = {
  ad: 'arac-gruplari',
  yol: '/app/arac-gruplari',
  baslik: 'Araç Grubu Tanımları',
  hazir: async (page) => {
    await expect(page.getByRole('cell', { name: 'Ekonomik', exact: true })).toBeVisible();
  },
};
const TEMPLATES: VitrinSayfasi = {
  ad: 'belge-sablonlari',
  yol: '/app/belge-sablonlari',
  baslik: 'Belge Şablonları',
  hazir: async (page) => {
    await expect(page.getByRole('cell', { name: 'Kurumsal', exact: true })).toBeVisible();
  },
};

const sources = () => [
  source(SOURCE_1, 'WEB', 'Web Sitesi', { kiraOrani: 12.5, uzatamaz: true }),
  source(SOURCE_2, 'TEL', 'Telefon'),
];

test.beforeEach(async ({ page }) => {
  await logIn(page, { ...BEN, izinler: [...BEN.izinler, 'ManageUsers'] });
});

test('kdv oranları: sayfalı liste, sürümsüz satır tekil okunur, PUT sürümle; axe iki tema', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const { writes, pages } = await pagedEndpoints(page, 'kdv-oranlari', {
    rows: () => [vatRate()],
  });
  await page.goto('/app/kdv-oranlari');
  await expect(page.getByRole('cell', { name: 'Genel %20' })).toBeVisible();
  expect(pages[0]).toContain('boyut=200');
  expect(await seriousViolations(page), 'açık').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await seriousViolations(page), 'koyu').toEqual([]);

  await page.getByRole('button', { name: 'Düzenle' }).click();
  await page.getByRole('textbox', { name: 'Oran (0,20 = %20)' }).fill('0,1');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(JSON.parse(writes[0]?.govde ?? '{}')).toEqual({
    kod: 'KDV20',
    ad: 'Genel %20',
    oran: 0.1,
    aktif: true,
    surum: 'k-1',
  });
  expect(errors).toEqual([]);
});

test('ceza türleri: doğrulama hatasında form korunur (alan hatası alanın altında)', async ({
  page,
}) => {
  collectErrors(page, NETWORK_ERROR);
  await pagedEndpoints(page, 'ceza-turleri', {
    rows: () => [
      { id: 'a', kod: 'HIZ', ad: 'Hız cezası', varsayilanTutar: 1500, aktif: true, surum: 'c-1' },
    ],
    write: (r) =>
      problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
        errors: { varsayilanTutar: ['Varsayılan tutar negatif olamaz.'] },
      }),
  });
  await page.goto('/app/ceza-turleri');
  await expect(page.getByRole('cell', { name: 'Hız cezası' })).toBeVisible();
  await page.getByRole('button', { name: 'Yeni kayıt' }).click();
  await page.getByRole('textbox', { name: 'Kod' }).fill('OTP');
  await page.getByRole('textbox', { name: 'Ad' }).fill('Otopark');
  await page.getByRole('textbox', { name: 'Varsayılan Tutar' }).fill('250');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Varsayılan tutar negatif olamaz.')).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Ad' })).toHaveValue('Otopark');
  await expect(page.getByRole('textbox', { name: 'Kod' })).toHaveValue('OTP');
});

test('sigorta şirketleri: oturum düşünce form kaybolmaz — aynı istek aynı anahtar ve gövdeyle', async ({
  page,
}) => {
  collectErrors(page, [...NETWORK_ERROR, /401/]);
  let n = 0;
  const { writes } = await pagedEndpoints(page, 'sigorta-sirketleri', {
    rows: () => [insurer()],
    write: (r) =>
      ++n === 1
        ? problem(r, 401, 'oturum_yok', 'Oturum açık değil.')
        : r.fulfill({ json: insurer() }),
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await writeXsrf(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await writeXsrf(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });
  await page.goto('/app/sigorta-sirketleri');
  await expect(page.getByRole('cell', { name: 'Anadolu Sigorta' })).toBeVisible();
  await page.getByRole('button', { name: 'Düzenle' }).click();
  await page.getByRole('textbox', { name: 'Telefon' }).fill('0850 111 11 11');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(
    page.locator('.duzenleme').getByRole('textbox', { name: 'Telefon', includeHidden: true }),
  ).toHaveValue('0850 111 11 11');
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1]?.govde).toBe(writes[0]?.govde);
  expect(writes[1]?.anahtar).toBe(writes[0]?.anahtar);
  expect(JSON.parse(writes[1]?.govde ?? '{}')).toMatchObject({
    telefon: '0850 111 11 11',
    surum: 'i-1',
  });
});

test('belge şablonları: cakisma formu silmez — güncel kayıt birleşir, sonraki PUT yeni sürümle; metin düz', async ({
  page,
}) => {
  collectErrors(page, [...NETWORK_ERROR, /409/]);
  let current = template();
  let n = 0;
  const { writes } = await pagedEndpoints(page, 'belge-sablonlari', {
    rows: () => [template()],
    one: () => current,
    write: (r) => {
      if (++n === 1) {
        current = template({ altBilgi: 'Başka oturum', aktif: false, surum: 't-2' });
        return problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti.');
      }
      return r.fulfill({ json: current });
    },
  });
  await page.goto(TEMPLATES.yol);
  await waitReady(page, TEMPLATES);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const left = page.getByRole('textbox', { name: 'Hukuki Metin — Sol (yalnız kira sözleşmesi)' });
  await expect(left).toHaveValue('Sol metin <b>kalın değil</b>');
  await expect(page.locator('b')).toHaveCount(0);
  await left.fill('Benim metnim\nikinci satır');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  // Dokunulmayan alan sunucu değerine çekilir (alt bilgi), dokunulan korunur (sol metin).
  await expect(page.getByRole('textbox', { name: 'Alt Bilgi' })).toHaveValue('Başka oturum');
  await expect(left).toHaveValue('Benim metnim\nikinci satır');
  await expect(page.getByRole('textbox', { name: 'Alt Bilgi' })).toHaveValue('Başka oturum');
  expect(writes).toHaveLength(1);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(JSON.parse(writes[1]?.govde ?? '{}')).toMatchObject({
    hukukiMetinSol: 'Benim metnim\nikinci satır',
    altBilgi: 'Başka oturum',
    aktif: false,
    surum: 't-2',
  });
  expect(writes[1]?.path).toBe(`/api/ui/v1/belge-sablonlari/${TEMPLATE_1}`);
});

test('rezervasyon kaynakları: kural bayrağı ve oran gövdede; Aşağıya Yansıt onaylı', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const writes = await definitionEndpoints(page, 'rezervasyon-kaynaklari', {
    rows: sources,
    write: (r, w) =>
      w.path.endsWith('/yansit')
        ? r.fulfill({ json: { guncellenen: 1 } })
        : r.fulfill({ json: sources()[0] }),
  });
  await page.goto(SOURCES.yol);
  await waitReady(page, SOURCES);
  expect(await seriousViolations(page), 'açık').toEqual([]);

  await page.getByRole('button', { name: 'Düzenle' }).first().click();
  await page.getByRole('checkbox', { name: 'Kural: KM sınırsız' }).check();
  await page.getByRole('textbox', { name: 'Kural: En fazla gün' }).fill('30');
  expect(await seriousViolations(page), 'panel').toEqual([]);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => writes.length).toBe(1);
  const put = JSON.parse(writes[0]?.govde ?? '{}') as Record<string, unknown>;
  expect(put).toMatchObject({
    kod: 'WEB',
    kiraOrani: 12.5,
    uzatamaz: true,
    kmSinirsiz: true,
    maxGun: 30,
    surum: 'r-WEB',
  });

  await page
    .getByRole('combobox', { name: 'Oranları kopyalanacak kaynak' })
    .selectOption({ label: 'WEB — Web Sitesi' });
  await page.getByRole('button', { name: 'Oranları Yansıt' }).click();
  const dialog = page.getByRole('alertdialog', { name: 'Oranlar yansıtılsın mı?' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Oranları Yansıt' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1]?.path).toBe(`/api/ui/v1/rezervasyon-kaynaklari/${SOURCE_1}/yansit`);
  await expect(page.getByText('Oranlar 1 kaynağa kopyalandı.')).toBeVisible();
  expect(errors).toEqual([]);
});

test('araç grupları: eşleşmeyen boş grup değeri hedef gruba atanır', async ({ page }) => {
  const errors = collectErrors(page);
  const { assigns } = await vehicleGroupEndpoints(page);
  await page.goto(GROUPS.yol);
  await waitReady(page, GROUPS);
  await expect(page.getByText('Hiçbir gruba eşleşmeyen 2 araç grup değeri')).toBeVisible();
  expect(await seriousViolations(page), 'açık').toEqual([]);
  await page
    .getByRole('combobox', { name: 'Grup değeri' })
    .selectOption({ label: '(grubu boş araçlar)' });
  await page.getByRole('combobox', { name: 'Hedef grup' }).selectOption({ label: 'Ekonomik' });
  await page.getByRole('button', { name: 'Ata', exact: true }).click();
  await expect.poll(() => assigns.length).toBe(1);
  expect(JSON.parse(assigns[0]?.govde ?? '{}')).toEqual({
    hedefGrupId: GROUP_1,
    kaynak: null,
    bos: true,
  });
  await expect(page.getByText('2 araç hedef gruba taşındı.')).toBeVisible();
  expect(errors).toEqual([]);
});

test('ödeme tipleri ve hesap kodları: genel uç, oluşturma gövdesi', async ({ page }) => {
  const errors = collectErrors(page);
  const payments = await definitionEndpoints(page, 'odeme-tipleri', {
    rows: () => [{ id: 'p1', kod: 'NAKIT', ad: 'Nakit', aktif: true, surum: 'p-1' }],
  });
  const codes = await definitionEndpoints(page, 'hesap-kodlari', {
    rows: () => [
      { id: 'h1', kod: '600', ad: 'Yurtiçi Satışlar', aciklama: null, aktif: true, surum: 'h-1' },
    ],
  });
  await page.goto('/app/odeme-tipleri');
  await expect(page.getByRole('cell', { name: 'Nakit', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Yeni kayıt' }).click();
  await page.getByRole('textbox', { name: 'Kod' }).fill('KK');
  await page.getByRole('textbox', { name: 'Ad' }).fill('Kredi Kartı');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => payments.length).toBe(1);
  expect(JSON.parse(payments[0]?.govde ?? '{}')).toEqual({
    kod: 'KK',
    ad: 'Kredi Kartı',
    aktif: true,
  });

  await page.goto('/app/hesap-kodlari');
  await expect(page.getByRole('cell', { name: 'Yurtiçi Satışlar' })).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);
  await page.getByRole('button', { name: 'Yeni kayıt' }).click();
  await page.getByRole('textbox', { name: 'Kod' }).fill('770');
  await page.getByRole('textbox', { name: 'Ad' }).fill('Genel Yönetim Giderleri');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => codes.length).toBe(1);
  expect(JSON.parse(codes[0]?.govde ?? '{}')).toEqual({
    kod: '770',
    ad: 'Genel Yönetim Giderleri',
    aciklama: null,
    aktif: true,
  });
  expect(errors).toEqual([]);
});

for (const s of [SOURCES, GROUPS, TEMPLATES]) {
  const fakes = async (page: import('@playwright/test').Page) => {
    await definitionEndpoints(page, 'rezervasyon-kaynaklari', { rows: sources });
    await vehicleGroupEndpoints(page);
    await pagedEndpoints(page, 'belge-sablonlari', { rows: () => [template()] });
  };
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await fakes(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await waitReady(page, s);
        expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await fakes(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await waitReady(page, s);
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
