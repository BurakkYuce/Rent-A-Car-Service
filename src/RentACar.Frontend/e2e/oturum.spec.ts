import { expect, test, type Page } from '@playwright/test';

import {
  BEN,
  ciddiIhlaller,
  hatalariTopla,
  kaydet,
  type KayitliIstek,
  oturumAc,
  problem,
  xsrfYaz,
} from './ortak';

/**
 * F3.3 Exit e2e'leri: "doğrulama hatasında form korunur", "oturum düşünce form kaybolmaz",
 * "`cakisma` formu silmez" + giriş/çıkış ve sorgu mesajları. Harness yalnız statik SPA sunduğu için
 * `/api/ui/v1` Playwright ile sahtelenir (yanıt biçimi backend `UiHata` sözleşmesi).
 * Beklenen tarayıcı konsol hatası: sahte 4xx yanıtlarının ağ günlüğü.
 */
const AG_HATASI = [/Failed to load resource: the server responded with a status of 4\d\d/];

const VITRIN = '/app/vitrin/geri-bildirim';

async function formuDoldur(page: Page): Promise<void> {
  await page.getByLabel('Plaka').fill('34 ABC 123');
  await page.getByLabel('Açıklama').fill('Uzun açıklama — 172 alanlı formun yerine geçen deneme.');
}

async function formKorunduMu(page: Page): Promise<void> {
  await expect(page.getByLabel('Plaka')).toHaveValue('34 ABC 123');
  await expect(page.getByLabel('Açıklama')).toHaveValue(
    'Uzun açıklama — 172 alanlı formun yerine geçen deneme.',
  );
  await expect(page).toHaveURL(/\/app\/vitrin\/geri-bildirim$/);
}

test('(a) doğrulama hatası: alan hatası gösterilir, form değerleri korunur, gezinme yok', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await oturumAc(page);
  await page.route('**/api/ui/v1/vitrin/kayit', (route) =>
    problem(route, 400, 'dogrulama', 'Plaka biçimi hatalı.', {
      errors: { Plaka: ['Plaka biçimi hatalı.'] },
    }),
  );
  await page.goto(VITRIN);
  await formuDoldur(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();

  await expect(page.locator('#vitrin-plaka-hata')).toHaveText('Plaka biçimi hatalı.');
  await expect(page.getByLabel('Plaka')).toHaveAttribute('aria-invalid', 'true');
  await formKorunduMu(page);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(await ciddiIhlaller(page)).toEqual([]);
  expect(hatalar).toEqual([]);
});

test('(b) oturum düşer → yerinde giriş diyaloğu → AYNI istek tekrarlanır, form kaybolmaz', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await oturumAc(page);
  const kayitlar: KayitliIstek[] = [];
  let girisGovdesi: unknown = null;
  await page.route('**/api/ui/v1/vitrin/kayit', async (route) => {
    kayitlar.push(kaydet(route.request()));
    if (kayitlar.length === 1) return problem(route, 401, 'oturum_yok', 'Oturum açık değil.');
    return route.fulfill({ json: { id: 'KR-2026-0001' } });
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await xsrfYaz(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    girisGovdesi = route.request().postDataJSON();
    await xsrfYaz(page, 'yeni-belirtec'); // giriş yeni kimliğe bağlı belirteç verir
    return route.fulfill({ json: BEN });
  });

  await page.goto(VITRIN);
  await formuDoldur(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();

  const diyalog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(diyalog).toBeVisible();
  await expect(diyalog).toHaveAttribute('aria-modal', 'true');
  await expect(diyalog.getByLabel('Firma kodu')).toHaveValue('pilot');
  await expect(diyalog.getByLabel('Firma kodu')).not.toBeEditable();
  await expect(diyalog.getByLabel('Kullanıcı adı')).toHaveValue('ayse');
  await expect(diyalog.getByLabel('Parola')).toBeFocused();
  // Arkadaki form yerinde (diyalog üstünde açıldı, sayfadan gidilmedi).
  await formKorunduMu(page);
  expect(await ciddiIhlaller(page)).toEqual([]);

  await diyalog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await diyalog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();

  await expect(diyalog).toHaveCount(0);
  await expect(page.getByText('Kayıt no: KR-2026-0001')).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'Kayıt kaydedildi.' })).toBeVisible();
  await formKorunduMu(page);

  expect(girisGovdesi).toEqual({
    firma: 'pilot',
    kullanici: 'ayse',
    sifre: 'rastgele-e2e-parolasi',
  });
  expect(kayitlar).toHaveLength(2);
  const [ilk, tekrar] = kayitlar;
  expect(tekrar?.govde).toBe(ilk?.govde); // AYNI istek
  expect(tekrar?.anahtar).toBe(ilk?.anahtar); // aynı Idempotency-Key (yeni anahtar yok)
  expect(ilk?.xsrf).toBe('eski-belirtec');
  expect(tekrar?.xsrf).toBe('yeni-belirtec'); // tekrar TAZE belirteçle
  expect(hatalar).toEqual([]);
});

test('(b2) diyalogda vazgeç / Esc: istek düşer ama form yerinde kalır', async ({ page }) => {
  await oturumAc(page);
  await page.route('**/api/ui/v1/vitrin/kayit', (route) =>
    problem(route, 401, 'oturum_yok', 'Oturum açık değil.'),
  );
  await page.goto(VITRIN);
  await formuDoldur(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  const diyalog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(diyalog).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(diyalog).toHaveCount(0);
  await formKorunduMu(page);
  await expect(page.getByRole('button', { name: 'Kaydet' })).toBeFocused(); // odak geri döner
});

test('(c) cakisma formu silmez: alanlıysa alan hatası, alansızsa uyarı bandı', async ({ page }) => {
  await oturumAc(page);
  let sayac = 0;
  await page.route('**/api/ui/v1/vitrin/kayit', (route) =>
    ++sayac === 1
      ? problem(route, 409, 'cakisma', 'Bu plaka başka bir araçta kayıtlı.', {
          errors: { Plaka: ['Bu plaka başka bir araçta kayıtlı.'] },
        })
      : problem(route, 409, 'cakisma', 'Araç bu tarihlerde müsait değil.'),
  );
  await page.goto(VITRIN);
  await formuDoldur(page);

  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.locator('#vitrin-plaka-hata')).toHaveText('Bu plaka başka bir araçta kayıtlı.');
  await formKorunduMu(page);

  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Araç bu tarihlerde müsait değil.');
  await formKorunduMu(page);
  expect(sayac).toBe(2);
});

test('mukerrer: yeni anahtarla yeniden GÖNDERİLMEZ, kayıt yeniden yüklenir + bilgi', async ({
  page,
}) => {
  await oturumAc(page);
  let sayac = 0;
  await page.route('**/api/ui/v1/vitrin/kayit', (route) => {
    sayac++;
    return problem(route, 409, 'mukerrer', 'Bu işlem zaten kaydedildi.');
  });
  await page.goto(VITRIN);
  await formuDoldur(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();

  await expect(page.getByText('Kayıt yeniden yüklendi (1).')).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'Mükerrer işlem' })).toBeVisible();
  await page.waitForTimeout(300);
  expect(sayac).toBe(1);
  await formKorunduMu(page);
});

test('yetki_yok uyarı bandıdır (form hatası değil); kiraci_kapali mesajlı giriş sayfasına götürür', async ({
  page,
}) => {
  await oturumAc(page);
  let sayac = 0;
  await page.route('**/api/ui/v1/vitrin/kayit', (route) =>
    ++sayac === 1
      ? problem(route, 403, 'yetki_yok', 'Bu işlem için yetkiniz yok.')
      : problem(route, 401, 'kiraci_kapali', 'Firma hesabı kapalı.'),
  );
  await page.goto(VITRIN);
  await formuDoldur(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Bu işlem için yetkiniz yok.');
  await formKorunduMu(page);

  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page).toHaveURL(/\/app\/giris\?neden=kiraci_kapali$/);
  await expect(page.getByRole('status')).toContainText('Firma hesabı kapalı.');
});

test('giriş: oturumsuz adres girişe döner; 400 genel mesaj + alanlar korunur; başarıda hedefe', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let oturumVar = false;
  await page.route('**/api/ui/v1/oturum/ben', (route) =>
    oturumVar ? route.fulfill({ json: BEN }) : problem(route, 401, 'oturum_yok', 'Oturum yok.'),
  );
  await page.route('**/api/ui/v1/oturum/xsrf', (route) => route.fulfill({ status: 204 }));
  let deneme = 0;
  await page.route('**/api/ui/v1/oturum/giris', (route) => {
    if (++deneme === 1)
      return problem(route, 400, 'dogrulama', 'Firma kodu, kullanıcı adı ya da şifre hatalı.');
    oturumVar = true;
    return route.fulfill({ json: BEN });
  });

  await page.goto(`${VITRIN}?x=1`);
  await expect(page).toHaveURL(/\/app\/giris\?returnUrl=%2Fvitrin%2Fgeri-bildirim%3Fx%3D1$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Giriş yap' })).toBeVisible();
  expect(await ciddiIhlaller(page)).toEqual([]);

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
  expect(hatalar).toEqual([]);
});

test('çıkış: tam temizlik (tema tercihi kalır) ve giriş sayfası', async ({ page }) => {
  await oturumAc(page);
  let cikisCagrildi = false;
  await page.route('**/api/ui/v1/oturum/cikis', (route) => {
    cikisCagrildi = true;
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
  expect(cikisCagrildi).toBe(true);
  expect(await page.evaluate(() => localStorage.getItem('rc.sekmeler'))).toBeNull();
  expect(await page.evaluate(() => localStorage.getItem('rc.tema'))).toBe('koyu');
});

test('?bilgi= toast, ?hata= bant olarak BİR kez gösterilir ve URL’den silinir (#sekme korunur)', async ({
  page,
}) => {
  await oturumAc(page);
  await page.goto(
    `${VITRIN}?bilgi=Kira%20kaydedildi.&hata=Tahsilat%20yap%C4%B1lamad%C4%B1.&y=2#sekme=odeme`,
  );
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Tahsilat yapılamadı.');
  await expect(page).toHaveURL(/\/app\/vitrin\/geri-bildirim\?y=2#sekme=odeme$/);
});

test('onay diyaloğu: odak kilidi, Esc = vazgeç; onay = true', async ({ page }) => {
  await oturumAc(page);
  await page.goto(VITRIN);
  await page.getByRole('button', { name: 'Onay iste' }).click();
  const diyalog = page.getByRole('alertdialog', { name: 'Kayıt silinsin mi?' });
  await expect(diyalog).toBeVisible();
  await expect(diyalog.getByRole('button', { name: 'Vazgeç' })).toBeFocused();
  await page.keyboard.press('Tab');
  await page.keyboard.press('Tab');
  await expect(diyalog.getByRole('button', { name: 'Vazgeç' })).toBeFocused(); // kilit: dışarı çıkmaz
  expect(await ciddiIhlaller(page)).toEqual([]);
  await page.keyboard.press('Escape');
  await expect(page.getByText('Vazgeçildi.')).toBeVisible();

  await page.getByRole('button', { name: 'Onay iste' }).click();
  await diyalog.getByRole('button', { name: 'Onayla' }).click();
  await expect(page.getByText('Onaylandı.')).toBeVisible();
});
