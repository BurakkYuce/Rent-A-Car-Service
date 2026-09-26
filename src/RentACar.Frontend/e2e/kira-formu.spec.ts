import { expect, test, type Page, type Request, type Route } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, logIn, problem, writeXsrf } from './ortak';
import {
  RENTAL_ID,
  MUSTERI_ID,
  ARAC_ID,
  DEFINITION_ID,
  YENI,
  RENTAL,
  DETAIL,
  fakeRentalApi,
  quick,
  formReady,
} from './kira-sahte';
import { measureOverflow } from './vitrin-sayfalari';

/**
 * F4.3 kira formu (sahte `/api/ui/v1`, üretim derlemesi + CSP). Faz çıkışı e2e'leri: "doğrulama
 * hatasında form korunur", "oturum düşünce form kaybolmaz", "müsaitlik `cakisma` formu silmez",
 * "`?varac=` dolu form açar", "`#sekme=` doğru sekmeyi açar" + ek hizmet çift tık + PUT 58 alan + sürüm;
 * adversarial kalıcılaştırma: R2 bayat sekme (409 → birleştir → yeniden kaydet), P261-10, F4, F7.
 * Beklenen değerler sahte yanıtlardan ELLE kurulur (formül yok).
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];

async function isFormPreserved(page: Page): Promise<void> {
  await expect(page).toHaveURL(/\/app\/kiralar\/yeni\?varac=/);
  await expect(quick(page).getByLabel('Araç', { exact: true })).toHaveValue(
    '34 ABC 123 — Fiat Egea',
  );
  // Odaktaki para girdisi düzenleme yazımını (gruplamasız) gösterir; değer aynıdır.
  await expect(quick(page).getByLabel('Günlük ücret / toplam')).toHaveValue(/^1\.?250,50$/);
  await expect(quick(page).getByLabel('Rezervasyon kaynağı')).toHaveValue('Web sitesi');
}

async function fill(page: Page): Promise<void> {
  await quick(page).getByLabel('Günlük ücret / toplam').fill('1250,50');
  await quick(page).getByLabel('Rezervasyon kaynağı').fill('Web sitesi');
  await quick(page).getByLabel('Rezervasyon kaynağı').blur();
}

test.beforeEach(async ({ page }) => logIn(page));

test('?varac&vfrom&vto&musteriId dolu form açar; canlı hesap sunucudan (UI formül taşımaz)', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const account = await fakeRentalApi(page);
  await page.goto(YENI);
  await formReady(page);

  const panel = quick(page);
  // F4.3b: bağlantıdaki kimlik gerçek adla çözülür (secim/musteri/{id}).
  await expect(panel.getByLabel('Müşteri', { exact: true })).toHaveValue('Ayşe Yılmaz');
  await expect(panel.getByLabel('Başlangıç', { exact: true })).toHaveValue('01.10.2026');
  await expect(panel.getByLabel('Bitiş (beklenen)', { exact: true })).toHaveValue('04.10.2026');
  await expect(panel.getByRole('textbox', { name: 'Saat' }).first()).toHaveValue('09:00');
  // Özet sunucu yanıtının biçimi: 3.600,00 ₺ (hesap yok).
  await expect(panel.getByTestId('canli-hesap')).toContainText('3.600,00');
  await expect(page.getByTestId('yan-ozet')).toContainText('3.600,00');
  await expect.poll(() => account.at(-1) ?? '').toContain(`vehicleId=${ARAC_ID}`);
  const last = new URLSearchParams(account.at(-1));
  expect(last.get('basTar')).toBe('2026-10-01T06:00:00.000Z');
  expect(last.get('musteriId')).toBe(MUSTERI_ID);

  // Araç sekmesi: müsait liste + seçili satır.
  await page.getByRole('tab', { name: 'Araç' }).click();
  await expect(page).toHaveURL(/#sekme=arac$/);
  await expect(page.getByTestId('musait-notu')).toContainText('1 araç müsait');
  expect(await seriousViolations(page)).toEqual([]);
  expect(errors).toEqual([]);
});

test('#sekme= doğru sekmeyi açar (yeni ve kayıtlı kira; Ayrıntılar alt sekmesi dahil)', async ({
  page,
}) => {
  await fakeRentalApi(page);
  await page.goto(`${YENI}#sekme=fiyat`);
  await expect(page.getByRole('tab', { name: 'Fiyat/Toplam' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByRole('tabpanel', { name: 'Fiyat/Toplam' })).toBeVisible();
  await expect(page.getByRole('tabpanel', { name: 'Hızlı Giriş' })).toHaveCount(0);

  await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=ayrintilar&alt=aksesuar`);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');
  await expect(page.getByRole('tab', { name: 'Ayrıntılar' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByRole('tab', { name: 'Aksesuar' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByRole('table', { name: 'Aksesuar' })).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);
});

test('doğrulama hatasında form korunur: alan işaretlenir, gezinme yok', async ({ page }) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await fakeRentalApi(page, {
    yazma: (route) =>
      problem(route, 400, 'dogrulama', 'Günlük ücret negatif olamaz.', {
        errors: { gunlukUcret: ['Günlük ücret negatif olamaz.'] },
      }),
  });
  await page.goto(YENI);
  await formReady(page);
  await fill(page);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  const alan = quick(page).getByLabel('Günlük ücret / toplam');
  await expect(alan).toHaveAttribute('aria-invalid', 'true');
  await expect(quick(page)).toContainText('Günlük ücret negatif olamaz.');
  await isFormPreserved(page);
  expect(errors).toEqual([]);
});

test('oturum düşünce form kaybolmaz: yerinde giriş → AYNI istek tekrarlanır → kayıt açılır', async ({
  page,
}) => {
  const bodies: (string | null)[] = [];
  await fakeRentalApi(page, {
    yazma: (route, request) => {
      bodies.push(request.postData());
      if (bodies.length === 1) return problem(route, 401, 'oturum_yok', 'Oturum açık değil.');
      return route.fulfill({
        status: 201,
        json: { id: RENTAL_ID, sozlesmeNo: '2026220901001', uyari: null },
      });
    },
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await writeXsrf(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await writeXsrf(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });

  await page.goto(YENI);
  await formReady(page);
  await fill(page);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await isFormPreserved(page);
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();

  await expect(page).toHaveURL(new RegExp(`/app/kiralar/${RENTAL_ID}$`));
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');
  expect(bodies).toHaveLength(2);
  expect(bodies[1]).toBe(bodies[0]);
  const body = JSON.parse(bodies[0] ?? '{}') as Record<string, unknown>;
  expect(body).toMatchObject({
    musteriId: MUSTERI_ID,
    vehicleId: ARAC_ID,
    basTar: '2026-10-01T06:00:00.000Z',
    bitTar: '2026-10-04T06:00:00.000Z',
    gunlukUcret: '1250.50',
    kaynak: 'Web sitesi',
  });
});

test('müsaitlik `cakisma` formu silmez: uyarı bandı + form üstü mesaj, değerler yerinde', async ({
  page,
}) => {
  await fakeRentalApi(page, {
    yazma: (route) => problem(route, 409, 'cakisma', 'Araç bu tarihlerde müsait değil.'),
  });
  await page.goto(YENI);
  await formReady(page);
  await fill(page);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Araç bu tarihlerde müsait değil.');
  await expect(page.locator('.rc-form-hatalari')).toContainText('Araç bu tarihlerde müsait değil.');
  await isFormPreserved(page);
});

test('kayıtlı kira: PUT 58 alanın hepsini + sürümü taşır; ek hizmet çift tık TEK kalem yazar', async ({
  page,
}) => {
  const puts: Record<string, unknown>[] = [];
  let addOnRequest = 0;
  const addOnKeys: string[] = [];
  await fakeRentalApi(page, {
    yazma: async (route, request) => {
      if (request.method() === 'PUT') {
        puts.push(request.postDataJSON() as Record<string, unknown>);
        return route.fulfill({ json: RENTAL });
      }
      if (request.url().endsWith('/ek-hizmetler')) {
        addOnRequest++;
        addOnKeys.push(request.headers()['idempotency-key'] ?? '');
        await new Promise((r) => setTimeout(r, 400)); // istek sürerken ikinci tık
        return route.fulfill({ json: { kalemler: [], kira: RENTAL } });
      }
      return route.fulfill({ status: 500 });
    },
  });
  await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=ayrintilar`);
  const description = page
    .getByRole('tabpanel', { name: 'Ayrıntılar' })
    .getByRole('textbox', { name: 'Açıklama', exact: true });
  await description.fill('Müşteri erken gelecek');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  expect(puts).toHaveLength(1);
  expect(Object.keys(puts[0] ?? {})).toHaveLength(59);
  expect(puts[0]?.['surum']).toBe('v1');
  expect(puts[0]).toMatchObject({
    aciklama: 'Müşteri erken gelecek',
    kmLimit: 300,
    fazlaKmUcret: 2.5,
    cikisOfisi: 'Merkez',
    odemeSekli: 'Nakit',
  });

  await page.getByRole('tab', { name: 'Ek Hizmetler' }).click();
  const add = page.getByTestId('ek-hizmet-ekle');
  await add.getByRole('combobox').click();
  await page.getByRole('option', { name: /Bebek koltuğu/ }).click();
  await add.getByRole('button', { name: 'Ekle' }).dblclick();
  await expect(page.getByRole('status').filter({ hasText: 'Ek hizmet eklendi.' })).toBeVisible();
  expect(addOnRequest).toBe(1);
  // Low-B: sunucu Idempotency-Key ister (16–128 görünür ASCII); çift gönderim ikinci kalem yazamaz.
  expect(addOnKeys[0]?.length ?? 0).toBeGreaterThanOrEqual(16);
});

test('sözleşme linki: oluştur → adres kendi kökünden; yeni sürüm onay ister', async ({ page }) => {
  let link: object | null = null;
  const writes: string[] = [];
  await fakeRentalApi(page, {
    yazma: (route, request) => {
      writes.push(`${request.method()} ${new URL(request.url()).pathname}`);
      link = {
        yol: '/sozlesme/tahmin-edilemez',
        erisimSayisi: 0,
        sonErisimUtc: null,
        olusturmaUtc: '2026-09-22T06:00:00Z',
        anlikGoruntuUtc: '2026-09-22T06:00:00Z',
        bayat: false,
      };
      return route.fulfill({ json: { link } });
    },
  });
  // Detay: paylaşım barı operasyon izniyle dolu, link sonradan gelir.
  await page.route(new RegExp(`/api/ui/v1/kiralar/${RENTAL_ID}$`), (route) =>
    route.fulfill({
      json: {
        ...DETAIL,
        paylasim: { link, musteriTel: null, musteriEmail: null, konu: 'Kira Sözleşmesi' },
      },
    }),
  );
  await page.goto(`/app/kiralar/${RENTAL_ID}`);
  await page.getByRole('button', { name: 'Paylaşım linki oluştur' }).click();
  const address = page.getByTestId('paylasim-adresi');
  await expect(address).toHaveValue(/^http:\/\/127\.0\.0\.1:\d+\/sozlesme\/tahmin-edilemez$/);
  await page.getByRole('button', { name: 'Yeni sürüm' }).click();
  const approval = page.getByRole('alertdialog', { name: 'Yeni sürüm paylaşılsın mı?' });
  await expect(approval).toBeVisible();
  await approval.getByRole('button', { name: 'Vazgeç' }).click();
  expect(writes).toEqual([`POST /api/ui/v1/kiralar/${RENTAL_ID}/paylasim`]);
});

test('faturalı kirada ek hizmet ekle/sil 400: mesaj gösterilir, seçim silinmez', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await fakeRentalApi(page, {
    yazma: (route, request) =>
      request.method() === 'DELETE'
        ? problem(route, 400, 'dogrulama', 'Faturalanmış kiranın ek hizmeti silinemez.')
        : problem(route, 400, 'dogrulama', 'Faturalanmış kiraya ek hizmet eklenemez.'),
  });
  await page.route(new RegExp(`/api/ui/v1/kiralar/${RENTAL_ID}$`), (route) =>
    route.fulfill({
      json: {
        ...DETAIL,
        ekHizmetler: [
          {
            id: '0b0e7c1a-7777-4aaa-8bbb-000000000007',
            ekHizmetTanimId: DEFINITION_ID,
            ad: 'Bebek koltuğu',
            miktar: 1,
            birimNetFiyat: 100,
            kdvOrani: 0.2,
            netTutar: 100,
            kdvTutar: 20,
            toplam: 120,
          },
        ],
      },
    }),
  );
  await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=ekhizmet`);
  const add = page.getByTestId('ek-hizmet-ekle');
  await add.getByRole('combobox').click();
  await page.getByRole('option', { name: /Bebek koltuğu/ }).click();
  await add.getByRole('button', { name: 'Ekle' }).click();
  await expect(page.locator('.rc-form-hatalari')).toContainText(
    'Faturalanmış kiraya ek hizmet eklenemez.',
  );
  // F4.3b: seçenek etiketi katalogdan, Blazor gibi "Ad (birim net)".
  await expect(add.getByRole('combobox')).toHaveValue('Bebek koltuğu (75,50 ₺ net)');

  await page.getByRole('button', { name: 'Sil Bebek koltuğu' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Onayla' }).click();
  await expect(
    page.getByRole('alert').filter({ hasText: 'Faturalanmış kiranın ek hizmeti silinemez.' }),
  ).toBeVisible();
  await expect(page.getByRole('table', { name: 'Ek hizmet kalemleri' })).toContainText(
    'Bebek koltuğu',
  );
  expect(errors).toEqual([]);
});

/** Kayıtlı kira için değişebilir sunucu durumu: detay GET'i ve yazmalar aynı nesneyi görür. */
async function rentalWithStatus(
  page: Page,
  start: Record<string, unknown>,
  write: (route: Route, request: Request, server: { kira: Record<string, unknown> }) => unknown,
  detailExtra: Record<string, unknown> = {},
): Promise<{ kira: Record<string, unknown> }> {
  const server = { kira: { ...RENTAL, ...start } as Record<string, unknown> };
  await fakeRentalApi(page, { yazma: (route, request) => write(route, request, server) });
  await page.route(new RegExp(`/api/ui/v1/kiralar/${RENTAL_ID}$`), (route) =>
    route.request().method() === 'GET'
      ? route.fulfill({ json: { ...DETAIL, ...detailExtra, kira: server.kira } })
      : (write(route, route.request(), server) as Promise<void>),
  );
  return server;
}

const detailDescription = (page: Page) =>
  page
    .getByRole('tabpanel', { name: 'Ayrıntılar' })
    .getByRole('textbox', { name: 'Açıklama', exact: true });

test('F2/R2 + N2 bayat sekme: başka oturumun drop ücreti geri ALINMAZ — 409 → güncel hâl birleşir, çakışma yoksa TEK sefer sessiz yeniden gönderim', async ({
  page,
}) => {
  const errors = collectErrors(page, [...NETWORK_ERROR, /status of 409/]);
  const puts: Record<string, unknown>[] = [];
  const server = await rentalWithStatus(
    page,
    { surum: 'v1', dropUcreti: null },
    (route, request, s) => {
      if (request.method() !== 'PUT') return route.fulfill({ status: 500 });
      const g = request.postDataJSON() as Record<string, unknown>;
      puts.push(g);
      if (g['surum'] !== s.kira['surum']) {
        return problem(
          route,
          409,
          'cakisma',
          'Kira başka bir oturumda değişti; güncel hâli yüklendi.',
        );
      }
      s.kira = { ...s.kira, aciklama: g['aciklama'], dropUcreti: g['dropUcreti'], surum: 'v3' };
      return route.fulfill({ json: s.kira });
    },
  );
  await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=ayrintilar`);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');

  // Başka oturum drop ücretini 300 yaptı (sunucu sürümü v2); bu sekme yeniden okumadı.
  server.kira = { ...server.kira, dropUcreti: 300, genelToplam: 3960, surum: 'v2' };
  await detailDescription(page).fill('bayat sekme');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  // Kullanıcı TEK kez bastı: 409 → güncel hâl birleşti (dokunulmayan drop ücreti 300, dokunulan açıklama
  // yerinde; çakışan alan yok) → birleştirilmiş gövde v2 ile kendiliğinden bir kez daha gitti.
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  expect(puts).toHaveLength(2);
  expect(puts[0]).toMatchObject({ surum: 'v1', dropUcreti: null }); // reddedildi, hiçbir şey yazılmadı
  expect(puts[1]).toMatchObject({ surum: 'v2', dropUcreti: 300, aciklama: 'bayat sekme' });
  expect(server.kira['dropUcreti']).toBe(300);
  await expect(page.locator('rc-uyari-bandi')).not.toContainText('başka bir oturumda değişti');
  expect(errors).toEqual([]);
});

test('N2 aynı alana başka oturum yazdı: 409 → çakışma işaretlenir + bant, OTOMATİK yeniden gönderim YOK', async ({
  page,
}) => {
  const errors = collectErrors(page, [...NETWORK_ERROR, /status of 409/]);
  const puts: Record<string, unknown>[] = [];
  const server = await rentalWithStatus(
    page,
    { surum: 'v1', aciklama: null },
    (route, request, s) => {
      const g = request.postDataJSON() as Record<string, unknown>;
      puts.push(g);
      if (g['surum'] !== s.kira['surum']) {
        return problem(
          route,
          409,
          'cakisma',
          'Kira başka bir oturumda değişti; güncel hâli yüklendi.',
        );
      }
      s.kira = { ...s.kira, aciklama: g['aciklama'], surum: 'v3' };
      return route.fulfill({ json: s.kira });
    },
  );
  await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=ayrintilar`);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');
  server.kira = { ...server.kira, aciklama: 'öteki oturumun notu', surum: 'v2' };
  await detailDescription(page).fill('benim notum');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('alan başka bir oturumda da değişti');
  await expect(detailDescription(page)).toHaveValue('benim notum');
  await expect(detailDescription(page)).toHaveAttribute('aria-invalid', 'true');
  await page.waitForTimeout(400);
  expect(puts).toHaveLength(1);

  // Kullanıcı bilinçli yeniden kaydeder: v2 ile gider.
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  expect(puts[1]).toMatchObject({ surum: 'v2', aciklama: 'benim notum' });
  expect(errors).toEqual([]);
});

test('N1 işlem sonrası kayıt yeniden okunurken Kaydet PASİF; okuma bitince yeni sürümle tek PUT', async ({
  page,
}) => {
  const puts: Record<string, unknown>[] = [];
  let held: (() => void) | null = null;
  const server = await rentalWithStatus(
    page,
    { surum: 'v1', provizyon: 500, provizyonDurum: 'Yok' },
    (route, request, s) => {
      if (new URL(request.url()).pathname.endsWith('/provizyon/al')) {
        s.kira = { ...s.kira, provizyonDurum: 'Alindi', surum: 'v2' };
        return route.fulfill({ json: s.kira });
      }
      const g = request.postDataJSON() as Record<string, unknown>;
      puts.push(g);
      if (g['surum'] !== s.kira['surum']) return problem(route, 409, 'cakisma', 'Bayat.');
      return route.fulfill({ json: s.kira });
    },
  );
  await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=ayrintilar`);
  await detailDescription(page).fill('işlemden sonra');
  // Provizyon sonrası detay okuması GECİKİR (yavaş ağ).
  await page.route(new RegExp(`/api/ui/v1/kiralar/${RENTAL_ID}$`), async (route) => {
    if (route.request().method() !== 'GET' || server.kira['surum'] !== 'v2')
      return route.fallback();
    await new Promise<void>((r) => (held = r));
    return route.fallback();
  });
  await page.getByRole('tab', { name: 'Finans/Uçuş' }).click();
  await page.getByRole('button', { name: 'Provizyon al (manuel)' }).click();
  const save = page.getByRole('button', { name: 'Kaydet', exact: true });
  await expect(save).toBeDisabled(); // tazeleme bitmeden kendi değişikliğiyle 409 almasın
  await expect.poll(() => held !== null).toBe(true);
  (held as unknown as () => void)();
  await expect(page.getByTestId('provizyon-durum')).toHaveText('Alındı');
  await expect(save).toBeEnabled();
  await save.click();
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  expect(puts).toHaveLength(1);
  expect(puts[0]).toMatchObject({ surum: 'v2', aciklama: 'işlemden sonra' });
});

test('P261-10 form kirliyken "Provizyon al": sunucunun yazdığı provizyon tarihi Kaydet\'te SİLİNMEZ', async ({
  page,
}) => {
  const puts: Record<string, unknown>[] = [];
  await rentalWithStatus(
    page,
    { provizyon: 500, provizyonTarih: null, provizyonDurum: 'Yok' },
    (route, request, s) => {
      if (new URL(request.url()).pathname.endsWith('/provizyon/al')) {
        s.kira = {
          ...s.kira,
          provizyonTarih: '2026-09-22T22:30:00+00:00', // İstanbul 23.09 01:30
          provizyonDurum: 'Alindi',
          surum: 'v2',
        };
        return route.fulfill({ json: s.kira });
      }
      if (request.method() === 'PUT') {
        const g = request.postDataJSON() as Record<string, unknown>;
        puts.push(g);
        if (g['surum'] !== s.kira['surum']) return problem(route, 409, 'cakisma', 'Bayat.');
        return route.fulfill({ json: s.kira });
      }
      return route.fulfill({ status: 500 });
    },
  );
  await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=ayrintilar`);
  await detailDescription(page).fill('not');
  await page.getByRole('tab', { name: 'Finans/Uçuş' }).click();
  await page.getByRole('button', { name: 'Provizyon al (manuel)' }).click();
  await expect(page.getByTestId('provizyon-durum')).toHaveText('Alındı');
  await expect(
    page.getByRole('tabpanel', { name: 'Finans/Uçuş' }).getByLabel('Provizyon tarihi'),
  ).toHaveValue('23.09.2026');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  expect(puts).toHaveLength(1);
  expect(puts[0]).toMatchObject({
    surum: 'v2',
    aciklama: 'not',
    provizyonTarih: '2026-09-22T22:30:00+00:00', // orijinal an, gün yuvarlaması yok (F6)
  });
});

test('F4 2. sürücü carisi silinmiş: kimlik korunur, PUT null göndermez', async ({ page }) => {
  const SECOND = '0b0e7c1a-9999-4aaa-8bbb-000000000009';
  const puts: Record<string, unknown>[] = [];
  await rentalWithStatus(
    page,
    { ikinciSurucuId: SECOND },
    (route, request, s) => {
      puts.push(request.postDataJSON() as Record<string, unknown>);
      return route.fulfill({ json: s.kira });
    },
    { ikinciSurucu: null },
  );
  await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=musteri`);
  await expect(
    page.getByRole('tabpanel', { name: 'Müşteri' }).getByLabel('2. sürücü (kayıtlı cari)'),
  ).toHaveValue('(kayıt bulunamadı)');
  await page.getByRole('tab', { name: 'Ayrıntılar' }).click();
  await detailDescription(page).fill('yalnız açıklama');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect.poll(() => puts.length).toBe(1);
  expect(puts[0]).toMatchObject({ ikinciSurucuId: SECOND, aciklama: 'yalnız açıklama' });
});

test('F7 hızlı müşteri: ana form geçersizken (araç yok) cari AÇILMAZ', async ({ page }) => {
  const customerPosts: string[] = [];
  await fakeRentalApi(page, {
    yazma: (route, request) => {
      customerPosts.push(new URL(request.url()).pathname);
      return route.fulfill({ status: 201, json: { id: MUSTERI_ID, etiket: 'Yetim' } });
    },
  });
  await page.goto('/app/kiralar/yeni');
  await page.locator('#kf-yeni-musteri summary').click();
  await page.locator('#kf-yeni-musteri').getByLabel('Ad', { exact: true }).fill('Yetim');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(quick(page).getByLabel('Araç', { exact: true })).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  await page.waitForTimeout(300);
  expect(customerPosts).toEqual([]);
  await expect(page.locator('#kf-yeni-musteri').getByLabel('Ad', { exact: true })).toHaveValue(
    'Yetim',
  );
});

test("yazdırma rotası sunucunun PDF ucuna gider (SPA'ya yönlenmez)", async ({ page }) => {
  await page.route(`**/kiralar/${RENTAL_ID}/pdf`, (route) =>
    route.fulfill({ contentType: 'text/plain', body: 'PDF' }),
  );
  await page.goto(`/app/kiralar/${RENTAL_ID}/yazdir`);
  await expect(page).toHaveURL(new RegExp(`/kiralar/${RENTAL_ID}/pdf$`));
  expect(new URL(page.url()).pathname.startsWith('/app/')).toBe(false);
});

test('müşteri sekmesi: TC şifreli notu, belge no MASKELİ, kara liste, risk limiti', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await fakeRentalApi(page);
  await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=musteri`);
  const panel = page.getByRole('tabpanel', { name: 'Müşteri' });
  const summary = panel.getByTestId('musteri-ozeti');
  await expect(summary.getByTestId('tc-kimlik')).toHaveText('***şifreli — cari kartında');
  await expect(summary).toContainText('05321112233');
  await expect(panel).toContainText('****6543');
  await expect(panel).toContainText('****4567');
  await expect(panel).toContainText('Atatürk Cd. No:5');
  await expect(panel).toContainText('01.06.2015');
  await expect(panel.getByTestId('kara-liste')).toContainText('Kara liste');
  await expect(panel.getByTestId('kara-liste')).toContainText('Geç iade geçmişi');
  await expect(panel.getByTestId('risk-limiti')).toHaveText('5.000,00 ₺');
  expect(await seriousViolations(page)).toEqual([]);
  expect(errors).toEqual([]);
});

test('Hızlı Giriş: ceza rozeti ve ek hizmet tutarı SUNUCU toplamından', async ({ page }) => {
  await fakeRentalApi(page);
  await page.goto(`/app/kiralar/${RENTAL_ID}`);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');
  const badges = quick(page).getByTestId('hizli-rozetler');
  await expect(badges).toContainText('Tahsilat: 1.000,00 ₺');
  await expect(badges).toContainText('Kalan: 2.600,00 ₺');
  await expect(badges.getByTestId('ceza-rozeti')).toHaveText('Ceza: 250,00 ₺');
  await expect(quick(page).getByTestId('kayitli-ek-hizmet')).toHaveText('180,00 ₺');
});

test('paylaş: WhatsApp / Gmail bağlantıları sunucu metni + link, geçersiz GSM uyarır', async ({
  page,
}) => {
  // window.open yakalanır (dış siteye gidilmez); init betiği CDP ile eklenir, CSP'den etkilenmez.
  await page.addInitScript(() => {
    const w = window as unknown as { __acilan: string[] };
    w.__acilan = [];
    window.open = (u?: string | URL) => {
      w.__acilan.push(String(u));
      return null;
    };
  });
  await fakeRentalApi(page);
  const MESSAGE =
    'Sayın Ayşe Yılmaz, 2026220901001 nolu kira sözleşmeniz: 22.09.2026 - 25.09.2026, genel toplam 3.600,00 TL.';
  await page.route(new RegExp(`/api/ui/v1/kiralar/${RENTAL_ID}$`), (route) =>
    route.fulfill({
      json: {
        ...DETAIL,
        paylasim: {
          link: {
            yol: '/sozlesme/tahmin-edilemez',
            erisimSayisi: 0,
            sonErisimUtc: null,
            olusturmaUtc: '2026-09-22T06:00:00Z',
            anlikGoruntuUtc: '2026-09-22T06:00:00Z',
            bayat: false,
          },
          musteriTel: '0532 111 22 33',
          musteriEmail: 'ayse@ornek.test',
          konu: 'Kira Sözleşmesi 2026220901001',
          mesaj: MESSAGE,
        },
      },
    }),
  );
  await page.goto(`/app/kiralar/${RENTAL_ID}`);
  const bar = page.getByTestId('paylas-bari');
  await expect(bar.getByLabel('Numara (GSM)')).toHaveValue('0532 111 22 33');
  await bar.getByRole('button', { name: "WhatsApp'ta aç" }).click();
  await bar.getByRole('button', { name: "Gmail'de aç" }).click();
  const opened = await page.evaluate(() => (window as unknown as { __acilan: string[] }).__acilan);
  const origin = new URL(page.url()).origin;
  const fullMessage = `${MESSAGE} Sözleşmeniz: ${origin}/sozlesme/tahmin-edilemez`;
  expect(opened).toEqual([
    `https://wa.me/905321112233?text=${encodeURIComponent(fullMessage)}`,
    'https://mail.google.com/mail/?view=cm&fs=1&to=ayse%40ornek.test' +
      `&su=${encodeURIComponent('Kira Sözleşmesi 2026220901001')}&body=${encodeURIComponent(fullMessage)}`,
  ]);

  await bar.getByLabel('Numara (GSM)').fill('123');
  await bar.getByRole('button', { name: "WhatsApp'ta aç" }).click();
  await expect(bar.getByRole('alert')).toHaveText('Geçerli bir GSM girin (örn. 05xx xxx xx xx).');
  expect(
    await page.evaluate(() => (window as unknown as { __acilan: string[] }).__acilan.length),
  ).toBe(2);
});

test('ek hizmet matrisi: tanım fiyat/KDV satırları, işaret hesaba girer (tutar sunucudan)', async ({
  page,
}) => {
  const account = await fakeRentalApi(page);
  await page.goto(`${YENI}#sekme=ekhizmet`);
  const matrix = page.getByTestId('ek-hizmet-matrisi');
  const row = matrix.getByRole('row', { name: /Bebek koltuğu/ });
  await expect(row).toContainText('75,50 ₺');
  await expect(row).toContainText('%10');
  await expect(row).toContainText('maks 30 gün');
  await row.getByRole('checkbox', { name: 'Seç Bebek koltuğu' }).check();
  await expect(row.getByRole('textbox', { name: 'Miktar Bebek koltuğu' })).toHaveValue(/^1(,00)?$/);
  await expect
    .poll(() => decodeURIComponent(account.at(-1) ?? ''))
    .toContain(`ek=${DEFINITION_ID}:1`);
  await row.getByRole('checkbox', { name: 'Seç Bebek koltuğu' }).uncheck();
  await expect.poll(() => account.at(-1) ?? '').not.toContain('ek=');
  expect(await seriousViolations(page)).toEqual([]);
});

test('ek hizmet kataloğu KESİKSE listede olmayan tanım sunucu aramasıyla eklenir (#262 L2)', async ({
  page,
}) => {
  const account = await fakeRentalApi(page);
  const NAV_ID = '0b0e7c1a-6666-4aaa-8bbb-000000000016';
  await page.route('**/api/ui/v1/kiralar/ek-hizmet-katalogu', (route) =>
    route.fulfill({
      json: {
        ogeler: [{ id: NAV_ID, kod: 'NAV', ad: 'Navigasyon', birimUcret: 40, kdvOrani: 0.2 }],
        toplam: 206,
      },
    }),
  );
  await page.goto(`${YENI}#sekme=ekhizmet`);
  const panel = page.getByRole('tabpanel', { name: 'Ek Hizmetler' });
  await expect(panel).toContainText('İlk 1 tanım gösteriliyor (toplam 206).');
  const other = panel.getByRole('combobox', {
    name: 'Listede olmayan ek hizmet ekle (ada göre ara)',
  });
  await other.click();
  await page.getByRole('option', { name: /Bebek koltuğu/ }).click();
  const row = page.getByTestId('ek-hizmet-matrisi').getByRole('row', { name: /Bebek koltuğu/ });
  await expect(row.getByRole('checkbox', { name: 'Seç Bebek koltuğu' })).toBeChecked();
  await expect
    .poll(() => decodeURIComponent(account.at(-1) ?? ''))
    .toContain(`ek=${DEFINITION_ID}:1`);
  // `uncheck()` DEĞİL `click()`: katalog dışı satır işaret kalkınca TABLODAN ÇIKAR. `uncheck()` tıklamadan sonra AYNI
  // öğenin durumunu okur; Angular satırı o okumadan önce kaldırırsa (yavaş CI) öğe DOM'dan kopmuş olur, Playwright
  // eylemi baştan dener, locator bir daha çözülmez ve test 30 sn'de zaman aşımına düşer (main #262 CI). Yerelde
  // kaldırma durum okumasından sonra geldiği için geçiyordu. Sonuç aşağıda satırın yokluğuyla doğrulanır.
  await row.getByRole('checkbox', { name: 'Seç Bebek koltuğu' }).click();
  await expect(page.getByTestId('ek-hizmet-matrisi')).not.toContainText('Bebek koltuğu');
  await expect.poll(() => account.at(-1) ?? '').not.toContain('ek=');
});

test('anonim cari (#262 M1): paylaşım kutuları boş gelir, geçersiz numarayla WhatsApp açılmaz', async ({
  page,
}) => {
  await fakeRentalApi(page);
  await page.route(new RegExp(`/api/ui/v1/kiralar/${RENTAL_ID}$`), (route) =>
    route.fulfill({
      json: {
        ...DETAIL,
        musteri: { id: MUSTERI_ID, ad: 'Anonim müşteri' },
        paylasim: {
          link: null,
          musteriTel: null,
          musteriEmail: null,
          konu: 'Kira Sözleşmesi 2026220901001',
          mesaj: 'Sayın müşterimiz, 2026220901001 nolu kira sözleşmeniz: …',
        },
      },
    }),
  );
  await page.goto(`/app/kiralar/${RENTAL_ID}`);
  const bar = page.getByTestId('paylas-bari');
  await expect(bar.getByLabel('Numara (GSM)')).toHaveValue('');
  await expect(bar.getByLabel('E-posta')).toHaveValue('');
  await bar.getByRole('button', { name: "WhatsApp'ta aç" }).click();
  await expect(bar.getByRole('alert')).toHaveText('Geçerli bir GSM girin (örn. 05xx xxx xx xx).');
  await expect(quick(page).getByLabel('Müşteri', { exact: true })).toHaveValue('Anonim müşteri');
});

test.describe('390 px ve koyu tema', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  test('yeni kira: gövde taşması yok, koyu temada axe ciddi/kritik 0', async ({ page }) => {
    await fakeRentalApi(page);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto(YENI);
    await formReady(page);
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
    expect(await seriousViolations(page)).toEqual([]);
    await page.getByRole('tab', { name: 'Araç' }).click();
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });

    await page.getByRole('tab', { name: 'Ek Hizmetler' }).click();
    await expect(page.getByTestId('ek-hizmet-matrisi')).toBeVisible();
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });

    // Kayıtlı kira: müşteri özeti ızgarası taşmaz.
    await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=musteri`);
    await expect(page.getByTestId('musteri-ozeti')).toBeVisible();
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });

    // Kayıtlı kira: geniş tablolar kendi kutusunda kayar, gövde taşmaz.
    await page.goto(`/app/kiralar/${RENTAL_ID}#sekme=ekhizmet`);
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
    expect(await seriousViolations(page)).toEqual([]);
  });
});
