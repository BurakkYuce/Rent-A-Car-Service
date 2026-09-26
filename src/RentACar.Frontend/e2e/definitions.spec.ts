import { expect, test } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, logIn, problem, writeXsrf } from './ortak';
import { BRANCH_2, brand, branchEndpoints, definitionEndpoints } from './definition-fakes';
import { waitReady, measureOverflow, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F11.2a tanım ekranları: genel tanım CRUD'u (markalar temsili), sürüm 409 formu silmez, kullanımda silme 400
 * mesajı, şube birleştirme önizleme + onay, üç zorunlu senaryo + axe iki tema + 320/390/768/1440 taşma.
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];

const BRANDS: VitrinSayfasi = {
  ad: 'markalar',
  yol: '/app/markalar',
  baslik: 'Marka Tanımları',
  hazir: async (page) => {
    await expect(page.getByRole('cell', { name: 'Fiat', exact: true })).toBeVisible();
  },
};
const BRANCHES: VitrinSayfasi = {
  ad: 'subeler',
  yol: '/app/subeler',
  baslik: 'Şube Tanımları',
  hazir: async (page) => {
    await expect(page.getByRole('cell', { name: 'Havalimanı teslim' })).toBeVisible();
  },
};

test.beforeEach(async ({ page }) => {
  await logIn(page, { ...BEN, izinler: [...BEN.izinler, 'ManageUsers'] });
});

test('markalar: oluştur (aktif varsayılan), düzenle (surum ile PUT), axe iki tema', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const writes = await definitionEndpoints(page, 'markalar', { rows: () => [brand()] });
  await page.goto(BRANDS.yol);
  await waitReady(page, BRANDS);
  expect(await seriousViolations(page), 'açık').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await seriousViolations(page), 'koyu').toEqual([]);

  await page.getByRole('button', { name: 'Yeni kayıt' }).click();
  await page.getByRole('textbox', { name: 'Kod' }).fill('RNLT');
  await page.getByRole('textbox', { name: 'Ad' }).fill('Renault');
  expect(await seriousViolations(page), 'form').toEqual([]);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0]?.method).toBe('POST');
  expect(JSON.parse(writes[0]?.govde ?? '{}')).toEqual({ kod: 'RNLT', ad: 'Renault', aktif: true });

  await page.getByRole('button', { name: 'Düzenle' }).click();
  await page.getByRole('combobox', { name: 'Durum' }).selectOption({ label: 'Pasif' });
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1]?.path).toBe(`/api/ui/v1/markalar/${brand().id}`);
  expect(JSON.parse(writes[1]?.govde ?? '{}')).toEqual({
    kod: 'FIAT',
    ad: 'Fiat',
    aktif: false,
    surum: 'b-1',
  });
  expect(errors).toEqual([]);
});

test('markalar: kullanımdaki kayıt silinemez — sunucu mesajı görünür', async ({ page }) => {
  collectErrors(page, NETWORK_ERROR);
  const message =
    "'Fiat' markası 3 araç/tip/grup/sipariş kaydında kullanılıyor; silmek yerine pasife alın.";
  await definitionEndpoints(page, 'markalar', {
    rows: () => [brand()],
    write: (r) => problem(r, 400, 'dogrulama', message),
  });
  await page.goto(BRANDS.yol);
  await waitReady(page, BRANDS);
  await page.getByRole('button', { name: 'Sil' }).click();
  await page.getByRole('button', { name: 'Evet, sil' }).click();
  await expect(page.getByRole('alert').filter({ hasText: message })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Fiat', exact: true })).toBeVisible();
});

test('markalar: doğrulama hatasında form korunur (alan hatası alanın altında)', async ({
  page,
}) => {
  collectErrors(page, NETWORK_ERROR);
  await definitionEndpoints(page, 'markalar', {
    rows: () => [brand()],
    write: (r) =>
      problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
        errors: { kod: ["'FIAT' kodlu marka zaten var."] },
      }),
  });
  await page.goto(BRANDS.yol);
  await waitReady(page, BRANDS);
  await page.getByRole('button', { name: 'Yeni kayıt' }).click();
  await page.getByRole('textbox', { name: 'Kod' }).fill('FIAT');
  await page.getByRole('textbox', { name: 'Ad' }).fill('Fiat 2');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText("'FIAT' kodlu marka zaten var.")).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Ad' })).toHaveValue('Fiat 2');
});

test('markalar: oturum düşünce form kaybolmaz — yeniden girişte aynı istek aynı anahtarla', async ({
  page,
}) => {
  collectErrors(page, [...NETWORK_ERROR, /401/]);
  let n = 0;
  const writes = await definitionEndpoints(page, 'markalar', {
    rows: () => [brand()],
    write: (r) =>
      ++n === 1
        ? problem(r, 401, 'oturum_yok', 'Oturum açık değil.')
        : r.fulfill({ json: brand() }),
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await writeXsrf(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await writeXsrf(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });
  await page.goto(BRANDS.yol);
  await waitReady(page, BRANDS);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  await page.getByRole('textbox', { name: 'Ad' }).fill('Fiat Otomobil');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(
    page.locator('.duzenleme').getByRole('textbox', { name: 'Ad', includeHidden: true }),
  ).toHaveValue('Fiat Otomobil');
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1]?.govde).toBe(writes[0]?.govde);
  expect(writes[1]?.anahtar).toBe(writes[0]?.anahtar);
});

test('markalar: cakisma formu silmez — güncel kayıt birleşir, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  collectErrors(page, [...NETWORK_ERROR, /409/]);
  let current = brand();
  let n = 0;
  const writes = await definitionEndpoints(page, 'markalar', {
    rows: () => [brand()],
    one: () => current,
    write: (r) => {
      if (++n === 1) {
        current = brand({ ad: 'Fiat (başka oturum)', aktif: false, surum: 'b-2' });
        return problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti.');
      }
      return r.fulfill({ json: current });
    },
  });
  await page.goto(BRANDS.yol);
  await waitReady(page, BRANDS);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  await page.getByRole('textbox', { name: 'Ad' }).fill('Fiat Benim');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Bu alan siz düzenlerken değişti')).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Ad' })).toHaveValue('Fiat Benim');
  await expect(page.getByRole('combobox', { name: 'Durum' })).toHaveValue('1');
  expect(writes).toHaveLength(1);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(JSON.parse(writes[1]?.govde ?? '{}')).toEqual({
    kod: 'FIAT',
    ad: 'Fiat Benim',
    aktif: false,
    surum: 'b-2',
  });
});

test('şubeler: panel formu gizli hesabı korur; birleştirme önizleme → onay kutusu → onaylı birleştir', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const { merges, writes } = await branchEndpoints(page);
  await page.goto(BRANCHES.yol);
  await waitReady(page, BRANCHES);
  expect(await seriousViolations(page), 'açık').toEqual([]);

  // Panel düzenleme: formda olmayan bağlı kasa hesabı tam PUT'ta aynen geri gider.
  await page.getByRole('button', { name: 'Düzenle' }).first().click();
  await page.getByRole('textbox', { name: 'Yetkili' }).fill('Veli');
  expect(await seriousViolations(page), 'panel').toEqual([]);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => writes.length).toBe(1);
  const put = JSON.parse(writes[0]?.govde ?? '{}') as Record<string, unknown>;
  expect(put['yetkili']).toBe('Veli');
  expect(put['nakitHesapId']).toBe('33333333-0000-4000-8000-00000000d001');
  expect(put['surum']).toBe('s-MRK');
  expect(put['komisyonOran']).toBe(0.1);

  await page.getByRole('combobox', { name: 'Kaynak (pasife alınacak)' }).selectOption({
    label: 'Kadıköy',
  });
  await page.getByRole('combobox', { name: 'Hedef' }).selectOption({ label: 'Merkez' });
  await page.getByRole('button', { name: 'Önizle' }).click();
  await expect(page.getByText('Kadıköy → Merkez · toplam 8 kayıt')).toBeVisible();
  await expect(page.getByText('Kiralar: 5')).toBeVisible();
  const mergeButton = page.getByRole('button', { name: 'Birleştir', exact: true });
  await expect(mergeButton).toBeDisabled();
  await page.getByRole('checkbox', { name: 'Etkiyi gördüm, birleştir' }).check();
  expect(await seriousViolations(page), 'önizleme').toEqual([]);
  await mergeButton.click();
  const dialog = page.getByRole('alertdialog', { name: 'Şubeler birleştirilsin mi?' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Birleştir' }).click();
  await expect.poll(() => merges.length).toBe(1);
  expect(JSON.parse(merges[0]?.govde ?? '{}')).toEqual({
    kaynakId: BRANCH_2,
    hedefId: '22222222-0000-4000-8000-00000000c001',
    onay: true,
  });
  await expect(page.getByText('Birleştirme tamamlandı: 8 kayıt taşındı.')).toBeVisible();
  expect(errors).toEqual([]);
});

for (const s of [BRANDS, BRANCHES]) {
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await definitionEndpoints(page, 'markalar', { rows: () => [brand()] });
      await branchEndpoints(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await waitReady(page, s);
        expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await definitionEndpoints(page, 'markalar', { rows: () => [brand()] });
    await branchEndpoints(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await waitReady(page, s);
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
