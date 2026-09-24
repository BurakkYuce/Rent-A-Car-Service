import { expect, test, type Page } from '@playwright/test';

import { ciddiIhlaller, hatalariTopla, oturumAc, problem, xsrfYaz } from './ortak';
import { ADMIN_BEN, record, settings, settingsEndpoints, type Write } from './system-fakes';

/**
 * F11.2b firma ayarları: sır alanları yalnız yazılabilir (yanıtta yok, boş = korunur, new-password), tam PUT'ta surum,
 * üç zorunlu senaryo (doğrulama hatasında form korunur, oturum düşünce aynı istek tekrar, cakisma formu silmez),
 * alan adı TXT talimatı + doğrulama, dürüst test gönderimi.
 */
const AG_HATASI = [/Failed to load resource: the server responded with a status of 4\d\d/];

test.beforeEach(async ({ page }) => {
  await oturumAc(page, ADMIN_BEN);
});

async function open(page: Page) {
  await page.goto('/app/ayarlar');
  await expect(page.getByRole('heading', { name: 'Firma Ayarları' })).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Ünvan (hukuki)' })).toHaveValue(
    'Örnek Otomotiv A.Ş.',
  );
}

const body = (w: Write | undefined) => JSON.parse(w?.govde ?? '{}') as Record<string, unknown>;

test('ayarlar: sır yalnız yazılabilir, PUT surum taşır, kayıttan sonra sır formdan silinir; axe iki tema', async ({
  page,
}) => {
  const errors = hatalariTopla(page);
  const writes = await settingsEndpoints(page);
  await open(page);
  expect(await ciddiIhlaller(page), 'açık').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await ciddiIhlaller(page), 'koyu').toEqual([]);

  const smtp = page.getByLabel('SMTP şifre', { exact: true });
  await expect(smtp).toHaveAttribute('type', 'password');
  await expect(smtp).toHaveAttribute('autocomplete', 'new-password');
  await expect(smtp).toHaveValue('');
  await expect(smtp).toHaveAttribute('placeholder', /kayıtlı/);
  await expect(page.getByLabel('POS API anahtarı')).toHaveAttribute('placeholder', '(boş)');

  await page.getByRole('textbox', { name: 'Ünvan (hukuki)' }).fill('Yeni Ünvan A.Ş.');
  await page.getByRole('button', { name: 'Ayarları kaydet' }).click();
  await expect.poll(() => writes.length).toBe(1);
  const first = body(writes[0]);
  expect(first['firmaUnvan']).toBe('Yeni Ünvan A.Ş.');
  expect(first['smtpSifre']).toBeNull();
  expect(first['surum']).toBe('v1');
  expect('smtpSifreTanimli' in first).toBe(false);
  await expect(page.getByText('Ayarlar kaydedildi.')).toBeVisible();

  await smtp.fill('yeni-smtp-parolasi');
  await page.getByRole('button', { name: 'Ayarları kaydet' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(body(writes[1])['smtpSifre']).toBe('yeni-smtp-parolasi');
  await expect(smtp).toHaveValue('');
  // Tarayıcı deposuna sır ya da firma verisi yazılmaz.
  const stored = await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }));
  expect(stored).not.toContain('yeni-smtp-parolasi');
  expect(stored).not.toContain('Ünvan');
  expect(errors).toEqual([]);
});

test('ayarlar: doğrulama hatasında form korunur (sunucu alan hatası alanın altında)', async ({
  page,
}) => {
  hatalariTopla(page, AG_HATASI);
  const message =
    'SMTP şifresi: sunucu, port ya da kullanıcı değişince şifre yeniden girilmelidir.';
  await settingsEndpoints(page, {
    put: (r) =>
      problem(r, 400, 'dogrulama', 'Doğrulama hatası.', { errors: { smtpSifre: [message] } }),
  });
  await open(page);
  await page.getByRole('textbox', { name: 'SMTP sunucusu' }).fill('smtp.baska.test');
  await page.getByRole('button', { name: 'Ayarları kaydet' }).click();
  await expect(page.getByText(message)).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'SMTP sunucusu' })).toHaveValue('smtp.baska.test');
  await expect(page.getByLabel('SMTP şifre', { exact: true })).toBeFocused();
});

test('ayarlar: oturum düşünce form kaybolmaz — yeniden girişte aynı istek aynı anahtarla', async ({
  page,
}) => {
  hatalariTopla(page, [...AG_HATASI, /401/]);
  let n = 0;
  const writes = await settingsEndpoints(page, {
    put: (r) =>
      ++n === 1
        ? problem(r, 401, 'oturum_yok', 'Oturum açık değil.')
        : r.fulfill({ json: settings() }),
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await xsrfYaz(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await xsrfYaz(page, 'yeni-belirtec');
    return route.fulfill({ json: ADMIN_BEN });
  });
  await open(page);
  await page.getByRole('textbox', { name: 'Marka (ticari)' }).fill('Yeni Marka');
  await page.getByRole('button', { name: 'Ayarları kaydet' }).click();
  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(
    page.getByRole('textbox', { name: 'Marka (ticari)', includeHidden: true }),
  ).toHaveValue('Yeni Marka');
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1]?.govde).toBe(writes[0]?.govde);
  expect(writes[1]?.anahtar).toBe(writes[0]?.anahtar);
  expect(writes[1]?.xsrf).toBe('yeni-belirtec');
});

test('ayarlar: cakisma formu silmez — güncel kayıt birleşir, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  hatalariTopla(page, [...AG_HATASI, /409/]);
  let current = settings();
  let n = 0;
  const writes = await settingsEndpoints(page, {
    current: () => current,
    put: (r) => {
      if (++n === 1) {
        current = settings({
          firmaUnvan: 'Başka Oturum Ünvanı',
          firmaTel: '0216 999 99 99',
          surum: 'v2',
        });
        return problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti.');
      }
      return r.fulfill({ json: current });
    },
  });
  await open(page);
  await page.getByRole('textbox', { name: 'Ünvan (hukuki)' }).fill('Benim Ünvanım');
  await page.getByRole('button', { name: 'Ayarları kaydet' }).click();
  await expect(page.getByText('Bu alan siz düzenlerken değişti').first()).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Ünvan (hukuki)' })).toHaveValue('Benim Ünvanım');
  await expect(page.getByRole('textbox', { name: 'Telefon (ofis)' })).toHaveValue('0216 999 99 99');
  expect(writes).toHaveLength(1);
  await page.getByRole('button', { name: 'Ayarları kaydet' }).click();
  await expect.poll(() => writes.length).toBe(2);
  const second = body(writes[1]);
  expect(second['surum']).toBe('v2');
  expect(second['firmaUnvan']).toBe('Benim Ünvanım');
  expect(second['firmaTel']).toBe('0216 999 99 99');
});

test('ayarlar: bekleyen alan adı TXT talimatı + doğrula; test e-postası onaylı ve dürüst sonuç', async ({
  page,
}) => {
  const errors = hatalariTopla(page);
  const tests: Write[] = [];
  const writes = await settingsEndpoints(page, {
    post: (r) =>
      r.fulfill({
        json: settings({
          domainler: [
            {
              host: 'kirala.ornek.test',
              tur: 'Custom',
              durum: 'Active',
              dogrulamaKaydi: null,
              dogrulamaDegeri: null,
            },
          ],
          surum: 'v3',
        }),
      }),
  });
  // Son kaydedilen rota önce eşleşir: test ucu genel `/ayarlar` sahtesinden SONRA.
  await page.route('**/api/ui/v1/ayarlar/test/eposta', (r) => {
    tests.push(record(r));
    return r.fulfill({
      json: { basarili: false, durum: 'basarisiz', mesaj: 'SMTP sunucusuna bağlanılamadı.' },
    });
  });
  await open(page);
  await expect(page.getByText('_racar-verify.kirala.ornek.test')).toBeVisible();
  await expect(page.getByText('racar-0123456789abcdef')).toBeVisible();
  await page.getByRole('button', { name: 'kirala.ornek.test için doğrula' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0]?.path).toBe('/api/ui/v1/ayarlar/domainler/dogrula');
  expect(body(writes[0])).toEqual({ host: 'kirala.ornek.test' });
  await expect(page.getByRole('cell', { name: 'Etkin' })).toBeVisible();
  await expect(page.getByText('_racar-verify.kirala.ornek.test')).toHaveCount(0);

  await expect(page.getByRole('textbox', { name: 'Test e-posta adresi' })).toHaveValue(
    'info@ornek.test',
  );
  await page.getByRole('button', { name: 'Test e-postası gönder' }).click();
  const dialog = page
    .locator('[role=dialog],[role=alertdialog]')
    .filter({ hasText: 'gerçek bir e-posta' });
  await expect(dialog).toBeVisible();
  expect(tests).toHaveLength(0);
  await dialog.getByRole('button', { name: 'Gönder', exact: true }).click();
  await expect.poll(() => tests.length).toBe(1);
  expect(body(tests[0])).toEqual({ alici: 'info@ornek.test' });
  await expect(
    page.getByRole('status').filter({ hasText: 'SMTP sunucusuna bağlanılamadı.' }),
  ).toBeVisible();
  expect(errors).toEqual([]);
});
