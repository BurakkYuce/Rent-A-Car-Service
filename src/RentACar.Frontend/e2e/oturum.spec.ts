import { expect, test, type Page } from '@playwright/test';

import {
  BEN,
  seriousViolations,
  collectErrors,
  kaydet,
  type KayitliIstek,
  fakeMenu,
  logIn,
  problem,
  writeXsrf,
} from './ortak';

/**
 * F3.3 Exit e2e'leri: "doğrulama hatasında form korunur", "oturum düşünce form kaybolmaz",
 * "`cakisma` formu silmez" + giriş/çıkış ve sorgu mesajları. Harness yalnız statik SPA sunduğu için
 * `/api/ui/v1` Playwright ile sahtelenir (yanıt biçimi backend `UiHata` sözleşmesi).
 * Beklenen tarayıcı konsol hatası: sahte 4xx yanıtlarının ağ günlüğü.
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];

const SHOWCASE = '/app/vitrin/geri-bildirim';

async function fillForm(page: Page): Promise<void> {
  await page.getByLabel('Plaka').fill('34 ABC 123');
  await page.getByLabel('Açıklama').fill('Uzun açıklama — 172 alanlı formun yerine geçen deneme.');
}

async function isFormPreserved(page: Page): Promise<void> {
  await expect(page.getByLabel('Plaka')).toHaveValue('34 ABC 123');
  await expect(page.getByLabel('Açıklama')).toHaveValue(
    'Uzun açıklama — 172 alanlı formun yerine geçen deneme.',
  );
  await expect(page).toHaveURL(/\/app\/vitrin\/geri-bildirim$/);
}

test('(a) doğrulama hatası: alan hatası gösterilir, form değerleri korunur, gezinme yok', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await logIn(page);
  await page.route('**/api/ui/v1/vitrin/kayit', (route) =>
    problem(route, 400, 'dogrulama', 'Plaka biçimi hatalı.', {
      errors: { Plaka: ['Plaka biçimi hatalı.'] },
    }),
  );
  await page.goto(SHOWCASE);
  await fillForm(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();

  await expect(page.locator('#vitrin-plaka-hata')).toHaveText('Plaka biçimi hatalı.');
  await expect(page.getByLabel('Plaka')).toHaveAttribute('aria-invalid', 'true');
  await isFormPreserved(page);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(await seriousViolations(page)).toEqual([]);
  expect(errors).toEqual([]);
});

test('(b) oturum düşer → yerinde giriş diyaloğu → AYNI istek tekrarlanır, form kaybolmaz', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await logIn(page);
  const records: KayitliIstek[] = [];
  let loginBody: unknown = null;
  await page.route('**/api/ui/v1/vitrin/kayit', async (route) => {
    records.push(kaydet(route.request()));
    if (records.length === 1) return problem(route, 401, 'oturum_yok', 'Oturum açık değil.');
    return route.fulfill({ json: { id: 'KR-2026-0001' } });
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await writeXsrf(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    loginBody = route.request().postDataJSON();
    await writeXsrf(page, 'yeni-belirtec'); // giriş yeni kimliğe bağlı belirteç verir
    return route.fulfill({ json: BEN });
  });

  await page.goto(SHOWCASE);
  await fillForm(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();

  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(dialog).toHaveAttribute('aria-modal', 'true');
  await expect(dialog.getByLabel('Firma kodu')).toHaveValue('pilot');
  await expect(dialog.getByLabel('Firma kodu')).not.toBeEditable();
  await expect(dialog.getByLabel('Kullanıcı adı')).toHaveValue('ayse');
  await expect(dialog.getByLabel('Parola')).toBeFocused();
  // Arkadaki form yerinde (diyalog üstünde açıldı, sayfadan gidilmedi).
  await isFormPreserved(page);
  expect(await seriousViolations(page)).toEqual([]);

  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();

  await expect(dialog).toHaveCount(0);
  await expect(page.getByText('Kayıt no: KR-2026-0001')).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'Kayıt kaydedildi.' })).toBeVisible();
  await isFormPreserved(page);

  expect(loginBody).toEqual({
    firma: 'pilot',
    kullanici: 'ayse',
    sifre: 'rastgele-e2e-parolasi',
  });
  expect(records).toHaveLength(2);
  const [first, repeat] = records;
  expect(repeat?.govde).toBe(first?.govde); // AYNI istek
  expect(repeat?.anahtar).toBe(first?.anahtar); // aynı Idempotency-Key (yeni anahtar yok)
  expect(first?.xsrf).toBe('eski-belirtec');
  expect(repeat?.xsrf).toBe('yeni-belirtec'); // tekrar TAZE belirteçle
  expect(errors).toEqual([]);
});

test('(b2) diyalogda vazgeç / Esc: istek düşer ama form yerinde kalır', async ({ page }) => {
  await logIn(page);
  await page.route('**/api/ui/v1/vitrin/kayit', (route) =>
    problem(route, 401, 'oturum_yok', 'Oturum açık değil.'),
  );
  await page.goto(SHOWCASE);
  await fillForm(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await isFormPreserved(page);
  await expect(page.getByRole('button', { name: 'Kaydet' })).toBeFocused(); // odak geri döner
});

test('(c) cakisma formu silmez: alanlıysa alan hatası, alansızsa uyarı bandı', async ({ page }) => {
  await logIn(page);
  let counter = 0;
  await page.route('**/api/ui/v1/vitrin/kayit', (route) =>
    ++counter === 1
      ? problem(route, 409, 'cakisma', 'Bu plaka başka bir araçta kayıtlı.', {
          errors: { Plaka: ['Bu plaka başka bir araçta kayıtlı.'] },
        })
      : problem(route, 409, 'cakisma', 'Araç bu tarihlerde müsait değil.'),
  );
  await page.goto(SHOWCASE);
  await fillForm(page);

  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.locator('#vitrin-plaka-hata')).toHaveText('Bu plaka başka bir araçta kayıtlı.');
  await isFormPreserved(page);

  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Araç bu tarihlerde müsait değil.');
  await isFormPreserved(page);
  expect(counter).toBe(2);
});

test('mukerrer: yeni anahtarla yeniden GÖNDERİLMEZ, kayıt yeniden yüklenir + bilgi', async ({
  page,
}) => {
  await logIn(page);
  let counter = 0;
  await page.route('**/api/ui/v1/vitrin/kayit', (route) => {
    counter++;
    return problem(route, 409, 'mukerrer', 'Bu işlem zaten kaydedildi.');
  });
  await page.goto(SHOWCASE);
  await fillForm(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();

  await expect(page.getByText('Kayıt yeniden yüklendi (1).')).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'Mükerrer işlem' })).toBeVisible();
  await page.waitForTimeout(300);
  expect(counter).toBe(1);
  await isFormPreserved(page);
});

test('yetki_yok uyarı bandıdır (form hatası değil); kiraci_kapali mesajlı giriş sayfasına götürür', async ({
  page,
}) => {
  await logIn(page);
  let counter = 0;
  await page.route('**/api/ui/v1/vitrin/kayit', (route) =>
    ++counter === 1
      ? problem(route, 403, 'yetki_yok', 'Bu işlem için yetkiniz yok.')
      : problem(route, 401, 'kiraci_kapali', 'Firma hesabı kapalı.'),
  );
  await page.goto(SHOWCASE);
  await fillForm(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Bu işlem için yetkiniz yok.');
  await isFormPreserved(page);

  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page).toHaveURL(/\/app\/giris\?neden=kiraci_kapali$/);
  await expect(page.getByRole('status')).toContainText('Firma hesabı kapalı.');
});

test('giriş: oturumsuz adres girişe döner; 400 genel mesaj + alanlar korunur; başarıda hedefe', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await fakeMenu(page);
  let hasSession = false;
  await page.route('**/api/ui/v1/oturum/ben', (route) =>
    hasSession ? route.fulfill({ json: BEN }) : problem(route, 401, 'oturum_yok', 'Oturum yok.'),
  );
  await page.route('**/api/ui/v1/oturum/xsrf', (route) => route.fulfill({ status: 204 }));
  let attempt = 0;
  await page.route('**/api/ui/v1/oturum/giris', (route) => {
    if (++attempt === 1)
      return problem(route, 400, 'dogrulama', 'Firma kodu, kullanıcı adı ya da şifre hatalı.');
    hasSession = true;
    return route.fulfill({ json: BEN });
  });

  await page.goto(`${SHOWCASE}?x=1`);
  await expect(page).toHaveURL(/\/app\/giris\?returnUrl=%2Fapp%2Fvitrin%2Fgeri-bildirim%3Fx%3D1$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Giriş yap' })).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByLabel('Firma kodu').fill('pilot');
  await page.getByLabel('Kullanıcı adı').fill('ayse');
  await page.getByLabel('Parola').fill('yanlis-parola');
  await page.getByRole('button', { name: 'Giriş yap' }).click();
  await expect(
    page.getByRole('alert').filter({ hasText: 'Firma, kullanıcı veya parola hatalı.' }),
  ).toBeVisible();
  await expect(page.getByLabel('Firma kodu')).toHaveValue('pilot');
  await expect(page.getByLabel('Kullanıcı adı')).toHaveValue('ayse');
  await expect(page.getByLabel('Parola')).toHaveValue('');

  await page.getByLabel('Parola').fill('dogru-parola');
  await page.getByRole('button', { name: 'Giriş yap' }).click();
  await expect(page).toHaveURL(/\/app\/vitrin\/geri-bildirim\?x=1$/);
  expect(errors).toEqual([]);
});

test('çıkış: tam temizlik (tema tercihi kalır) ve giriş sayfası', async ({ page }) => {
  await logIn(page);
  let logoutCalled = false;
  await page.route('**/api/ui/v1/oturum/cikis', (route) => {
    logoutCalled = true;
    return route.fulfill({ status: 204 });
  });
  await page.goto('/app/');
  await page.evaluate(() => {
    localStorage.setItem('rc.tema', 'koyu');
    localStorage.setItem('rc.sekmeler', '[{"rota":"/kiralar/5"}]');
  });
  await page.getByRole('button', { name: 'Çıkış yap' }).click();

  await expect(page).toHaveURL(/\/app\/giris\?neden=cikis$/);
  await expect(page.getByRole('status')).toContainText('Oturumunuz kapatıldı.');
  expect(logoutCalled).toBe(true);
  expect(await page.evaluate(() => localStorage.getItem('rc.sekmeler'))).toBeNull();
  expect(await page.evaluate(() => localStorage.getItem('rc.tema'))).toBe('koyu');
});

test('?bilgi= toast, ?hata= bant: yalnız KOD çevrilir, BİR kez gösterilir, URL’den silinir (#sekme korunur)', async ({
  page,
}) => {
  await logIn(page);
  await page.goto(`${SHOWCASE}?bilgi=kaydedildi&hata=yetki_yok&y=2#sekme=odeme`);
  await expect(page.getByRole('status').filter({ hasText: 'Kaydedildi.' })).toBeVisible();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Bu işlem için yetkiniz yok.');
  await expect(page).toHaveURL(/\/app\/vitrin\/geri-bildirim\?y=2#sekme=odeme$/);
});

test('içerik sahteciliği: ?hata= / ?bilgi= serbest metni ekranda GÖRÜNMEZ (genel metin)', async ({
  page,
}) => {
  await logIn(page);
  const phishing = 'Hesabınız askıya alındı, 0850 000 00 00 numarasını arayın';
  await page.goto(
    `${SHOWCASE}?hata=${encodeURIComponent(phishing)}&bilgi=${encodeURIComponent(phishing)}`,
  );
  await expect(page.locator('rc-uyari-bandi')).toContainText('İşlem tamamlanamadı.');
  await expect(page.getByRole('status').filter({ hasText: 'İşlem tamamlandı.' })).toBeVisible();
  await expect(page.getByText('0850 000 00 00')).toHaveCount(0);
});

test('onay diyaloğu: odak kilidi, Esc = vazgeç; onay = true', async ({ page }) => {
  await logIn(page);
  await page.goto(SHOWCASE);
  await page.getByRole('button', { name: 'Onay iste' }).click();
  const dialog = page.getByRole('alertdialog', { name: 'Kayıt silinsin mi?' });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Vazgeç' })).toBeFocused();
  await page.keyboard.press('Tab');
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('button', { name: 'Vazgeç' })).toBeFocused(); // kilit: dışarı çıkmaz
  expect(await seriousViolations(page)).toEqual([]);
  await page.keyboard.press('Escape');
  await expect(page.getByText('Vazgeçildi.')).toBeVisible();

  await page.getByRole('button', { name: 'Onay iste' }).click();
  await dialog.getByRole('button', { name: 'Onayla' }).click();
  await expect(page.getByText('Onaylandı.')).toBeVisible();
});
