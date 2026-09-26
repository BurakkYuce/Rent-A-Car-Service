import { expect, test, type Page } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, logIn, problem, writeXsrf } from './ortak';
import {
  RENTAL_ID,
  CUSTOMER_ID,
  VEHICLE_ID,
  RES_ID,
  QUOTATION_ID,
  resDetail,
  resRow,
  fakeReservationApi,
  quotationDetail,
  quotationRow,
} from './rezervasyon-sahte';
import { measureOverflow } from './vitrin-sayfalari';

/**
 * F5.2a rezervasyon + teklif ekranları (sahte `/api/ui/v1`, üretim derlemesi + CSP). Faz çıkışı senaryoları:
 * "doğrulama hatasında form korunur", "oturum düşünce form kaybolmaz", "`cakisma` formu silmez" + teklif kabul
 * tekrarı (409 → yeniden yükle, oluşan rezervasyon görünür). axe iki temada, taşma 320/390/768/1440.
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];
const YENI = '/app/rezervasyonlar/yeni';
const DETAIL = `/app/rezervasyonlar/${RES_ID}`;

async function listReady(page: Page, label: string, no: string): Promise<void> {
  const grid = page.getByRole('grid', { name: label });
  await expect(grid).not.toHaveAttribute('aria-busy', 'true');
  await expect(grid.getByRole('link', { name: no, exact: true })).toBeVisible();
}

async function select(page: Page, alan: string, text: string, option: string): Promise<void> {
  const box = page.getByRole('combobox', { name: alan, exact: true });
  await box.click();
  await box.fill(text);
  await page.getByRole('option', { name: option }).click();
}

/** Yeni form: müşteri + araç + proje adı + günlük ücret. */
async function fill(page: Page): Promise<void> {
  await select(page, 'Müşteri', 'Ay', 'Ayşe Yılmaz');
  await select(page, 'Araç', '34', '34 ABC 123');
  await page.getByLabel('Günlük ücret').fill('1250,50');
  await page.getByLabel('Proje adı').fill('Fuar');
  await page.getByLabel('Proje adı').blur();
}

/** `rc-alan` etiketiyle başlayan alanın girdisi. */
const input = (page: Page, label: string) =>
  page
    .locator('rc-alan')
    .filter({ has: page.locator('label', { hasText: new RegExp(`^\\s*${label}`) }) })
    .locator('input')
    .first();

async function isFormPreserved(page: Page): Promise<void> {
  await expect(page).toHaveURL(/\/app\/rezervasyonlar\/yeni$/);
  // DOM ile (rol/etiket değil): yeniden giriş diyaloğu açıkken sayfa erişilebilirlik ağacından gizlenir.
  await expect(input(page, 'Müşteri')).toHaveValue('Ayşe Yılmaz');
  await expect(input(page, 'Araç')).toHaveValue('34 ABC 123');
  await expect(input(page, 'Günlük ücret')).toHaveValue(/^1\.?250,50$/);
  await expect(input(page, 'Proje adı')).toHaveValue('Fuar');
}

const saveButton = (page: Page) => page.getByRole('button', { name: 'Kaydet', exact: true });

test.beforeEach(async ({ page }) => logIn(page));

test('rezervasyon listesi: axe iki tema, bağlantılar, dışa aktarma süzgeci Blazor adlarıyla, Onayla', async ({
  page,
}) => {
  const errors = collectErrors(page);
  let status = 'Rezerv';
  const fake = await fakeReservationApi(page, {
    rezSatirlari: () => [resRow('RZ-000042', { durum: status })],
  });
  await page.route(`**/api/ui/v1/rezervasyonlar/${RES_ID}/onayla`, (route) => {
    status = 'Onayli';
    return route.fulfill({ json: resDetail({ durum: 'Onayli' }) });
  });
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto('/app/rezervasyonlar');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Rezervasyonlar');
  await listReady(page, 'Rezervasyonlar', 'RZ-000042');

  await expect(page.getByRole('link', { name: 'RZ-000042', exact: true })).toHaveAttribute(
    'href',
    DETAIL,
  );
  await expect(page.getByRole('gridcell', { name: '3.751,50 ₺' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/rezervasyonlar?format=excel',
  );
  expect(await seriousViolations(page), 'açık tema').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await seriousViolations(page), 'koyu tema').toEqual([]);

  // Süzgeç: URL = API adları; dışa aktarma Blazor export adlarıyla (ara/durum).
  await page.getByRole('button', { name: /Filtreler/ }).click();
  await page.getByRole('searchbox', { name: 'Ara', exact: true }).fill('Yılmaz');
  await page.getByRole('combobox', { name: 'Durum' }).selectOption({ label: 'Rezerv' });
  await page.getByRole('button', { name: 'Filtrele', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/rezervasyonlar\?q=Y%C4%B1lmaz&durum=Rezerv$/);
  await expect.poll(() => fake.listeIstekleri.at(-1)?.searchParams.get('durum')).toBe('Rezerv');
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/rezervasyonlar?format=excel&ara=Y%C4%B1lmaz&durum=Rezerv',
  );
  expect(await seriousViolations(page), 'filtre açık').toEqual([]);

  // Onayla: istek → bildirim → liste yenilenir, satır Onaylı.
  await page.getByRole('button', { name: 'Onayla RZ-000042' }).click();
  await expect(page.getByText('RZ-000042 onaylandı.')).toBeVisible();
  await expect(page.getByRole('gridcell', { name: 'Onaylı' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Onayla RZ-000042' })).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('kiraya çevir: onay sorulur → SPA kira formuna gidilir (F4.3 rota sözleşmesi)', async ({
  page,
}) => {
  await fakeReservationApi(page);
  await page.route(`**/api/ui/v1/kiralar/${RENTAL_ID}`, (route) =>
    route.fulfill({ status: 404, json: { status: 404, detail: 'yok' } }),
  );
  await page.goto('/app/rezervasyonlar');
  await listReady(page, 'Rezervasyonlar', 'RZ-000042');
  await page.getByRole('button', { name: 'Kiraya çevir RZ-000042' }).click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('kira sözleşmesine çevrilsin mi');
  await dialog.getByRole('button', { name: 'Kiraya çevir' }).click();
  await expect(page).toHaveURL(new RegExp(`/app/kiralar/${RENTAL_ID}$`));
  await expect(page.getByText('RZ-000042 kiraya çevrildi: sözleşme 2026230901001.')).toBeVisible();
});

test('doğrulama hatasında form korunur: alan işaretlenir, gezinme yok', async ({ page }) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await fakeReservationApi(page, {
    yazma: (route) =>
      problem(route, 400, 'dogrulama', 'Günlük ücret negatif olamaz.', {
        errors: { gunlukUcret: ['Günlük ücret negatif olamaz.'] },
      }),
  });
  await page.goto(YENI);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni Rezervasyon');
  await fill(page);
  await saveButton(page).click();

  await expect(page.getByLabel('Günlük ücret')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByText('Günlük ücret negatif olamaz.')).toBeVisible();
  await isFormPreserved(page);
  expect(await seriousViolations(page)).toEqual([]);
  expect(errors).toEqual([]);
});

test('oturum düşünce form kaybolmaz: yerinde giriş → AYNI istek tekrarlanır → kayıt açılır', async ({
  page,
}) => {
  const bodies: (string | null)[] = [];
  await fakeReservationApi(page, {
    yazma: (route, request) => {
      bodies.push(request.postData());
      if (bodies.length === 1) return problem(route, 401, 'oturum_yok', 'Oturum açık değil.');
      return route.fulfill({ status: 201, json: { id: RES_ID, no: 'RZ-000042' } });
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
  await fill(page);
  await saveButton(page).click();

  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await isFormPreserved(page);
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();

  await expect(page).toHaveURL(new RegExp(`${DETAIL}$`));
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Rezervasyon RZ-000042');
  expect(bodies).toHaveLength(2);
  expect(bodies[1]).toBe(bodies[0]);
  expect(JSON.parse(bodies[0] ?? '{}')).toMatchObject({
    musteriId: CUSTOMER_ID,
    vehicleId: VEHICLE_ID,
    gunlukUcret: '1250.50',
    projeAdi: 'Fuar',
  });
});

test('`cakisma` formu silmez: PUT sürümü taşır, 409 → bant + güncel kayıt birleşir, yeniden kayıt yeni sürümle', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  const puts: Record<string, unknown>[] = [];
  let server = resDetail();
  await fakeReservationApi(page, {
    detay: () => server,
    yazma: (route, request) => {
      puts.push(request.postDataJSON() as Record<string, unknown>);
      if (puts.length === 1) {
        // Başka oturum bu arada onay kodunu değiştirdi (sürüm 813).
        server = resDetail({ surum: '813', onayKodu: 'ONY-2' });
        return problem(route, 409, 'cakisma', 'Rezervasyon başka bir oturumda değişti.');
      }
      server = resDetail({ surum: '814', onayKodu: 'ONY-2', projeAdi: 'Kongre' });
      return route.fulfill({ json: server });
    },
  });
  await page.goto(DETAIL);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Rezervasyon RZ-000042');
  await expect(page.getByTestId('rez-ozet')).toContainText('3.751,50 ₺');
  await expect(page.getByLabel('Proje adı')).toHaveValue('Fuar');
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByLabel('Proje adı').fill('Kongre');
  await saveButton(page).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText(
    'Rezervasyon başka bir oturumda değişti.',
  );
  await expect(page.getByLabel('Proje adı')).toHaveValue('Kongre');
  await expect(page.getByLabel('Onay kodu')).toHaveValue('ONY-2'); // dokunulmamış → güncel
  expect(puts[0]).toMatchObject({ surum: '812', projeAdi: 'Kongre', gunlukUcret: 1250.5 });
  expect(Object.keys(puts[0] ?? {})).toHaveLength(33);

  await expect(saveButton(page)).toBeEnabled();
  await saveButton(page).click();
  await expect(page.getByText('Rezervasyon RZ-000042 kaydedildi.')).toBeVisible();
  expect(puts[1]).toMatchObject({ surum: '813', projeAdi: 'Kongre', onayKodu: 'ONY-2' });
  expect(errors).toEqual([]);
});

test('kiraya çevrilmiş rezervasyon: form salt okunur, eylem yok, kira bağlantısı', async ({
  page,
}) => {
  await fakeReservationApi(page, {
    detay: () =>
      resDetail(
        { durum: 'KirayaCevrildi', kiraId: RENTAL_ID },
        { duzenle: false, onayla: false, kirayaCevir: false, iptal: false },
      ),
  });
  await page.goto(DETAIL);
  await expect(page.getByTestId('rez-durum')).toHaveText('Kiraya çevrildi');
  await expect(page.getByLabel('Proje adı')).toBeDisabled();
  await expect(saveButton(page)).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'İptal' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Sözleşmeyi aç' })).toHaveAttribute(
    'href',
    `/app/kiralar/${RENTAL_ID}`,
  );
  expect(await seriousViolations(page)).toEqual([]);
});

test('teklif kabul TEKRARI: 409 cakisma → yeniden gönderim yok, teklif yeniden yüklenir, oluşan rezervasyon görünür', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let acceptCount = 0;
  let accepted = false;
  const fake = await fakeReservationApi(page, {
    teklifDetay: () =>
      accepted ? quotationDetail({ durum: 'Kabul', rezervasyonId: RES_ID }) : quotationDetail(),
    teklifKabul: (route) => {
      acceptCount++;
      // Başka sekmede zaten kabul edildi (ya da ilk yanıt kayboldu): sunucu ikinci rezervasyon AÇMAZ.
      accepted = true;
      return problem(route, 409, 'cakisma', 'Teklif zaten kabul edilmiş.');
    },
  });
  await page.goto(`/app/teklifler/${QUOTATION_ID}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Teklif TK-000007');
  await expect(page.getByTestId('teklif-ozet')).toContainText('3.600,00 ₺');
  expect(await seriousViolations(page)).toEqual([]);
  const detailBefore = fake.teklifDetayIstekleri.length;

  await page.getByRole('button', { name: 'Kabul → Rezervasyon' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Kabul et' }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Teklif zaten kabul edilmiş.');
  await expect(page.getByTestId('teklif-durum')).toHaveText('Kabul');
  await expect(page.getByRole('link', { name: 'Rezervasyonu aç' })).toHaveAttribute('href', DETAIL);
  await expect(page.getByRole('button', { name: 'Kabul → Rezervasyon' })).toHaveCount(0);
  expect(fake.teklifDetayIstekleri.length).toBeGreaterThan(detailBefore);
  await page.waitForTimeout(300);
  expect(acceptCount).toBe(1);
  expect(errors).toEqual([]);
});

test('teklif listesi + yeni teklif: axe, kabul edilen teklif rezervasyona bağlanır, gövde Blazor alanları', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const posts: Record<string, unknown>[] = [];
  await fakeReservationApi(page, {
    teklifSatirlari: () => [
      quotationRow('TK-000007'),
      { ...quotationRow('TK-000006', { durum: 'Kabul', rezervasyonId: RES_ID }), id: 'x-6' },
    ],
  });
  await page.route(
    (url) => url.pathname === '/api/ui/v1/teklifler',
    (route, request) => {
      if (request.method() !== 'POST') return route.fallback();
      posts.push(request.postDataJSON() as Record<string, unknown>);
      return route.fulfill({ status: 201, json: { id: QUOTATION_ID, no: 'TK-000007' } });
    },
  );
  await page.goto('/app/teklifler');
  await listReady(page, 'Teklifler', 'TK-000007');
  await expect(page.getByRole('link', { name: 'Kabul', exact: true })).toHaveAttribute(
    'href',
    DETAIL,
  );
  await expect(page.getByRole('button', { name: 'Reddet TK-000007' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Reddet TK-000006' })).toHaveCount(0);
  expect(await seriousViolations(page)).toEqual([]);

  await page.getByRole('link', { name: 'Yeni teklif' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni Teklif');
  await expect(
    page.getByRole('combobox', { name: 'Fiyat türü' }).locator('option:checked'),
  ).toHaveText('Otomatik');
  await select(page, 'Müşteri', 'Ay', 'Ayşe Yılmaz');
  await select(page, 'Araç', '34', '34 ABC 123');
  expect(await seriousViolations(page)).toEqual([]);
  await page.getByRole('button', { name: 'Teklif oluştur' }).click();
  await expect(page).toHaveURL(new RegExp(`/app/teklifler/${QUOTATION_ID}$`));
  expect(posts).toHaveLength(1);
  expect(posts[0]).toMatchObject({
    musteriId: CUSTOMER_ID,
    vehicleId: VEHICLE_ID,
    fiyatTuru: 'Otomatik',
    gunlukUcret: null,
    gecerlilikTarihi: null,
  });
  expect(errors).toEqual([]);
});

const PAGES = [
  { yol: '/app/rezervasyonlar', baslik: 'Rezervasyonlar' },
  { yol: YENI, baslik: 'Yeni Rezervasyon' },
  { yol: DETAIL, baslik: 'Rezervasyon RZ-000042' },
  { yol: '/app/teklifler', baslik: 'Teklifler' },
  { yol: '/app/teklifler/yeni', baslik: 'Yeni Teklif' },
  { yol: `/app/teklifler/${QUOTATION_ID}`, baslik: 'Teklif TK-000007' },
];

test.describe('mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  test('rezervasyon/teklif sayfaları: 320/390/768 px gövde yatay taşması yok', async ({ page }) => {
    await fakeReservationApi(page);
    for (const width of [320, 390, 768]) {
      await page.setViewportSize({ width: width, height: 844 });
      for (const s of PAGES) {
        await page.goto(s.yol);
        await expect(page.getByRole('heading', { level: 1 })).toHaveText(s.baslik);
        await page.evaluate(() => document.fonts.ready.then(() => undefined));
        expect(await measureOverflow(page), `${width}px ${s.yol}`).toEqual({
          tasma: 0,
          suclular: [],
        });
      }
    }
  });
});

test('rezervasyon/teklif sayfaları: 1440 px gövde yatay taşması yok', async ({ page }) => {
  await fakeReservationApi(page);
  await page.setViewportSize({ width: 1440, height: 900 });
  for (const s of PAGES) {
    await page.goto(s.yol);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(s.baslik);
    expect(await measureOverflow(page), s.yol).toEqual({ tasma: 0, suclular: [] });
  }
});
