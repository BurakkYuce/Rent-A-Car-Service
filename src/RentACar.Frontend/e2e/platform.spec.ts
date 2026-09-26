import { expect, test, type Page } from '@playwright/test';

import { seriousViolations, collectErrors, logIn } from './ortak';
import { fakePlatformApi, OPERATOR, OPERATOR_SECRET, TENANT_A, TENANT_B } from './platform-fakes';
import { measureOverflow } from './vitrin-sayfalari';

/**
 * F12.2 platform console (`/app/platform/*`): separate session and layout from the tenant shell.
 * Mandatory scenarios: sign in/out, create tenant, close with the typed code, pilot on/off; a TENANT
 * session cannot reach the screens; axe (light + dark) and overflow at 320/390/768/1440.
 */
const EXPECTED_4XX = [/Failed to load resource: the server responded with a status of 4\d\d/];

async function ready(page: Page, heading: string | RegExp): Promise<void> {
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(heading);
  await page.waitForFunction(
    () =>
      [...document.querySelectorAll('rc-ikon')].every((i) => i.querySelector('svg') !== null) &&
      document.fonts.status === 'loaded',
  );
}

async function signIn(page: Page): Promise<void> {
  await page.getByLabel('Kullanıcı').fill(OPERATOR);
  await page.getByLabel('Şifre').fill(OPERATOR_SECRET);
  await page.getByRole('button', { name: 'Giriş', exact: true }).click();
  await ready(page, 'Platform Özeti');
}

const confirmDialog = (page: Page) => page.getByRole('alertdialog').or(page.getByRole('dialog'));

test('giriş / çıkış: yanlış parola genel mesaj, doğru giriş özete, çıkış giriş sayfasına', async ({
  page,
}) => {
  const errors = collectErrors(page, EXPECTED_4XX);
  const api = await fakePlatformApi(page);
  await page.goto('/app/platform/kiracilar');
  await ready(page, 'Platform Girişi');
  await expect(page).toHaveURL(/\/app\/platform\/giris$/);

  await page.getByLabel('Kullanıcı').fill(OPERATOR);
  await page.getByLabel('Şifre').fill('yanlis');
  await page.getByRole('button', { name: 'Giriş', exact: true }).click();
  await expect(page.locator('.rc-form-mesaji--hata')).toHaveText(
    'Kullanıcı adı veya şifre hatalı.',
  );
  await expect(page.getByLabel('Şifre')).toHaveValue('');
  await expect(page.getByLabel('Kullanıcı')).toHaveValue(OPERATOR);

  await page.getByLabel('Şifre').fill(OPERATOR_SECRET);
  await page.getByRole('button', { name: 'Giriş', exact: true }).click();
  await ready(page, 'Platform Özeti');
  await expect(page.getByTestId('platform-ozet')).toContainText('Toplam firma');
  await expect(page.getByTestId('platform-kullanici')).toHaveText(OPERATOR);
  // No tenant shell: no tenant menu request, no tenant session probe.
  expect(api.calls).not.toContain('GET /menu');

  await page.getByRole('button', { name: 'Çıkış' }).click();
  await ready(page, 'Platform Girişi');
  await expect(page.getByRole('status')).toContainText('Çıkış yapıldı.');
  expect(api.calls).toContain('POST /oturum/cikis');
  // Back to a console page after sign-out → login again (session gone).
  await page.goto('/app/platform/kiracilar');
  await ready(page, 'Platform Girişi');
  expect(errors).toEqual([]);
});

test('firma oluştur: gövde sözleşmeye uygun, yeni firmanın detayına gidilir', async ({ page }) => {
  const api = await fakePlatformApi(page, { signedIn: true });
  await page.goto('/app/platform/kiracilar');
  await ready(page, 'Firma Konsolu');
  await expect(page.getByTestId('platform-firma-tablosu')).toContainText('yucerent');

  await page.getByRole('button', { name: 'Yeni Firma' }).click();
  await page.getByLabel('Firma kodu (giriş)').fill('yenifirma');
  await page.getByLabel('Firma adı').fill('Yeni Firma Rent');
  await page.getByLabel('Admin kullanıcı').fill('patron');
  await page.getByLabel('Admin parola').fill('uzun-bir-deger');
  await page.getByRole('button', { name: 'Oluştur' }).click();

  await ready(page, /Yeni Firma Rent/);
  await expect(page.getByText('yenifirma firması oluşturuldu.')).toBeVisible();
  expect(api.bodies).toContainEqual({
    kod: 'yenifirma',
    ad: 'Yeni Firma Rent',
    adminKullanici: 'patron',
    adminSifre: 'uzun-bir-deger',
  });
});

test('firma kapatma: yanlış onay kodu alanda hata + kapanmaz; doğru kod Kapalı, yeniden açılabilir', async ({
  page,
}) => {
  const errors = collectErrors(page, EXPECTED_4XX);
  const api = await fakePlatformApi(page, { signedIn: true });
  await page.goto(`/app/platform/kiracilar/${TENANT_A}`);
  await ready(page, /Yüce Rent A Car/);
  await expect(page.getByTestId('platform-firma-durum')).toHaveText('Aktif');

  const code = page.getByLabel('Onay için firma kodu');
  await code.fill('YUCERENT');
  await page.getByRole('button', { name: 'Firmayı Kapat' }).click();
  await expect(
    page.getByText('Onay kodu uyuşmadı — firma KAPATILMADI.', { exact: false }),
  ).toBeVisible();
  await expect(code).toHaveValue('YUCERENT'); // typed value kept
  await expect(page.getByTestId('platform-firma-durum')).toHaveText('Aktif');

  await code.fill('yucerent');
  await page.getByRole('button', { name: 'Firmayı Kapat' }).click();
  await expect(page.getByTestId('platform-firma-durum')).toHaveText('Kapalı');
  await expect(page.getByRole('button', { name: 'Firmayı Kapat' })).toHaveCount(0);
  expect(api.bodies).toContainEqual({ durum: 'Kapali', onayKod: 'YUCERENT' });
  expect(api.bodies).toContainEqual({ durum: 'Kapali', onayKod: 'yucerent' });

  await page.getByRole('button', { name: 'Yeniden Aç' }).click();
  await confirmDialog(page)
    .getByRole('button', { name: /Onayla|Evet|Tamam/ })
    .click();
  await expect(page.getByTestId('platform-firma-durum')).toHaveText('Aktif');
  expect(api.bodies).toContainEqual({ durum: 'Aktif', onayKod: null });
  expect(errors).toEqual([]);
});

test('yeni arayüz pilotu aç / kapat (onaylı; vazgeç istek göndermez)', async ({ page }) => {
  const api = await fakePlatformApi(page, { signedIn: true });
  await page.goto(`/app/platform/kiracilar/${TENANT_B}`);
  await ready(page, /Demo Firma/);
  const pilot = page.getByTestId('platform-pilot-durum');
  await expect(pilot).toHaveText('Kapalı');

  await page.getByRole('button', { name: 'Aç Yeni Arayüz Pilotu' }).click();
  await expect(confirmDialog(page)).toContainText('demo için yeni arayüz pilotu açılsın mı?');
  await confirmDialog(page)
    .getByRole('button', { name: /Onayla|Evet|Tamam/ })
    .click();
  await expect(pilot).toHaveText('Açık');

  await page.getByRole('button', { name: 'Kapat Yeni Arayüz Pilotu' }).click();
  // Cancel keeps it on (no request).
  await confirmDialog(page)
    .getByRole('button', { name: /Vazgeç|İptal/ })
    .click();
  await expect(pilot).toHaveText('Açık');
  await page.getByRole('button', { name: 'Kapat Yeni Arayüz Pilotu' }).click();
  await confirmDialog(page)
    .getByRole('button', { name: /Onayla|Evet|Tamam/ })
    .click();
  await expect(pilot).toHaveText('Kapalı');
  expect(api.calls.filter((c) => c.endsWith('/yeni-arayuz-pilot'))).toHaveLength(2);
  expect(api.bodies).toContainEqual({ aktif: true });
  expect(api.bodies).toContainEqual({ aktif: false });
});

test('firma oturumu platform ekranlarına erişemez: platform girişi görünür, platform verisi istenmez', async ({
  page,
}) => {
  await logIn(page); // tenant session (Admin) — tenant `ben` answers 200
  const calls: string[] = [];
  await page.route('**/api/ui/v1/platform/**', (r) => {
    calls.push(new URL(r.request().url()).pathname);
    return r.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({ status: 403, kod: 'yetki_yok', detail: 'Yetkiniz yok.' }),
    });
  });
  for (const path of [
    '/app/platform',
    '/app/platform/kiracilar',
    `/app/platform/kiracilar/${TENANT_A}`,
    '/app/platform/belgeler',
  ]) {
    await page.goto(path);
    await ready(page, 'Platform Girişi');
    await expect(page.getByRole('navigation', { name: 'Platform konsolu' })).toHaveCount(0);
  }
  // Only the session probe went out — never the summary, tenant list, detail or documents.
  expect(new Set(calls)).toEqual(new Set(['/api/ui/v1/platform/oturum/ben']));
});

test('belge merkezi: liste, taslak yayınla, yükleme dosyasız gitmez', async ({ page }) => {
  const api = await fakePlatformApi(page, { signedIn: true });
  await page.goto('/app/platform/belgeler');
  await ready(page, 'Belge Merkezi');
  const table = page.getByTestId('platform-belge-tablosu');
  await expect(table).toContainText('KVKK Aydınlatma Metni');
  await expect(table).toContainText('tüm firmalar');
  await expect(table).toContainText('Taslak');
  await page.getByRole('button', { name: 'Yayınla' }).click();
  await expect(table).toContainText('Yayında');
  expect(api.bodies).toContainEqual({ durum: 'Yayinda' });

  await page.getByRole('button', { name: 'Yeni Belge Yükle' }).click();
  await page.getByLabel('Başlık').fill('Kılavuz');
  await page.getByRole('button', { name: 'Yükle (taslak)' }).click();
  await expect(page.getByText('PDF dosyası seçilmedi.')).toBeVisible();
  expect(api.calls.filter((c) => c === 'POST /belgeler')).toHaveLength(0);
});

const PAGES = [
  { path: '/app/platform', heading: 'Platform Özeti' },
  { path: '/app/platform/kiracilar', heading: 'Firma Konsolu' },
  { path: `/app/platform/kiracilar/${TENANT_A}`, heading: /Yüce Rent A Car/ },
  { path: '/app/platform/belgeler', heading: 'Belge Merkezi' },
] as const;

test('axe: giriş + dört ekran, açık ve koyu temada ciddi/kritik ihlal yok', async ({ page }) => {
  const errors = collectErrors(page, EXPECTED_4XX);
  await fakePlatformApi(page);
  await page.goto('/app/platform/giris');
  await ready(page, 'Platform Girişi');
  expect(await seriousViolations(page), 'giriş açık').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await seriousViolations(page), 'giriş koyu').toEqual([]);
  await page.emulateMedia({ colorScheme: 'light' });
  await signIn(page);
  for (const p of PAGES) {
    await page.goto(p.path);
    await ready(page, p.heading);
    expect(await seriousViolations(page), `${p.path} açık`).toEqual([]);
    await page.emulateMedia({ colorScheme: 'dark' });
    expect(await seriousViolations(page), `${p.path} koyu`).toEqual([]);
    await page.emulateMedia({ colorScheme: 'light' });
  }
  expect(errors).toEqual([]);
});

test.describe('mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
  test('320/390/768 px: giriş + dört ekranda gövde yatay taşması yok', async ({ page }) => {
    const api = await fakePlatformApi(page);
    for (const width of [320, 390, 768]) {
      await page.setViewportSize({ width, height: 844 });
      api.signedIn = false;
      await page.goto('/app/platform/giris');
      await ready(page, 'Platform Girişi');
      expect(await measureOverflow(page), `${width}px giriş`).toEqual({ tasma: 0, suclular: [] });
      api.signedIn = true;
      for (const p of PAGES) {
        await page.goto(p.path);
        await ready(page, p.heading);
        expect(await measureOverflow(page), `${width}px ${p.path}`).toEqual({
          tasma: 0,
          suclular: [],
        });
      }
    }
  });
});

test('1440 px: dört ekranda gövde yatay taşması yok', async ({ page }) => {
  await fakePlatformApi(page, { signedIn: true });
  await page.setViewportSize({ width: 1440, height: 900 });
  for (const p of PAGES) {
    await page.goto(p.path);
    await ready(page, p.heading);
    expect(await measureOverflow(page), p.path).toEqual({ tasma: 0, suclular: [] });
  }
});
