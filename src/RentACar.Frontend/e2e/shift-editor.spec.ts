import { expect, test, type Page, type Route } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, logIn, problem, writeXsrf } from './ortak';
import { SHIFTS, reportEndpoints } from './report-fakes';
import { measureOverflow } from './vitrin-sayfalari';

/**
 * F10.3 personel çalışma — vardiya ekle / düzenle / sil (`/api/ui/v1/vardiyalar`). Üç zorunlu senaryo: doğrulama
 * hatasında form korunur, oturum düşünce form kaybolmaz (AYNI istek), `cakisma` formu silmez (güncel kayıt
 * birleşir, sonraki PUT yeni sürümle). Ayrıca: yetkisiz (OperationsWrite yok) kullanıcıda yazma bölümü yok,
 * silme onaylı ve rapor yeniden yüklenir. Sahte API; süreler elle kurulmuş (ekran hesap yapmaz).
 */
const NETWORK_ERROR = [
  /Failed to load resource: the server responded with a status of 4\d\d/,
  /Failed to load resource: net::ERR_FAILED/,
];
const PAGE = '/app/raporlar/personel-calisma';
const STAFF = 'f1f1f1f1-0000-4000-8000-000000000001';
const SHIFT_ID = 'f2f2f2f2-0000-4000-8000-000000000001';

const listRow = {
  id: SHIFT_ID,
  personelId: STAFF,
  personelAd: 'Ali Veli',
  personelKadroSube: 'Merkez',
  tarih: '2026-09-21',
  baslangicSaat: '09:00:00',
  bitisSaat: '17:00:00',
  sureDk: 480,
  aralik: '09:00-17:00',
  sube: 'Merkez',
  aciklama: 'Sabah',
};

function shift(o: { surum: string; sube?: string; aciklama?: string | null; bitis?: string }) {
  return {
    id: SHIFT_ID,
    personelId: STAFF,
    personelAd: 'Ali Veli',
    tarih: '2026-09-21',
    baslangicSaat: '09:00',
    bitisSaat: o.bitis ?? '17:00',
    sureDk: 480,
    aralik: '09:00-17:00',
    sube: o.sube ?? 'Merkez',
    aciklama: o.aciklama === undefined ? 'Sabah' : o.aciklama,
    surum: o.surum,
  };
}

interface Written {
  readonly method: string;
  readonly path: string;
  readonly govde: string;
  readonly anahtar: string | null;
}

interface Options {
  /** Yazma isteğini yanıtlar; `false` → varsayılan (201/200/204). */
  readonly write?: (route: Route, method: string) => Promise<boolean>;
  readonly record?: () => object;
}

/** Rapor sahteleri + vardiya uçları + personel seçimi. Yazma istekleri kaydedilir; rapor istekleri sayılır. */
async function shiftEndpoints(page: Page, o: Options = {}) {
  const written: Written[] = [];
  const reportCalls: URL[] = await reportEndpoints(page);
  await page.route('**/api/ui/v1/raporlar/personel-calisma**', (route) => {
    reportCalls.push(new URL(route.request().url()));
    return route.fulfill({ json: { ...SHIFTS, liste: [listRow] } });
  });
  await page.route('**/api/ui/v1/secim/personel**', (route) =>
    route.fulfill({ json: [{ id: STAFF, etiket: 'Ali Veli', kod: 'P1' }] }),
  );
  await page.route('**/api/ui/v1/vardiyalar**', async (route) => {
    const req = route.request();
    const method = req.method();
    if (method === 'GET') return route.fulfill({ json: o.record?.() ?? shift({ surum: 'v-1' }) });
    const url = new URL(req.url());
    written.push({
      method,
      path: url.pathname,
      govde: req.postData() ?? '',
      anahtar: req.headers()['idempotency-key'] ?? null,
    });
    if (o.write && (await o.write(route, method))) return;
    if (method === 'POST') return route.fulfill({ status: 201, json: shift({ surum: 'v-9' }) });
    if (method === 'DELETE') return route.fulfill({ status: 204, body: '' });
    return route.fulfill({ json: shift({ surum: 'v-2' }) });
  });
  return { written, reportCalls };
}

async function openPage(page: Page) {
  await page.goto(PAGE);
  await expect(page.getByRole('heading', { name: 'Vardiya Ekle' })).toBeVisible();
}

async function pickStaff(page: Page) {
  const form = page.getByRole('region', { name: 'Vardiya Ekle' });
  await form.getByRole('combobox', { name: 'Personel' }).fill('Al');
  await page.getByRole('option', { name: 'Ali Veli' }).click();
  return form;
}

test.beforeEach(async ({ page }) => {
  await logIn(page);
});

test('vardiya ekle: doğrulama hatasında form korunur; hatalı saat istek göndermez; gövde birebir', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let n = 0;
  const { written, reportCalls } = await shiftEndpoints(page, {
    write: async (r) => {
      if (++n > 1) return false;
      await problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
        errors: { baslangicSaat: ['Ali Veli için çakışan vardiya var: 21.09.2026 09:00-17:00.'] },
      });
      return true;
    },
  });
  await openPage(page);
  const form = await pickStaff(page);
  await expect(form.getByRole('textbox', { name: 'Tarih' })).toHaveValue('21.09.2026'); // pencerenin ilk günü
  const start = form.getByRole('textbox', { name: 'Başlangıç' });
  await start.fill('25:00');
  await form.getByRole('button', { name: 'Ekle' }).click();
  expect(written).toHaveLength(0); // istemci doğrulaması: istek gitmez

  await start.fill('10:00');
  await form.getByRole('textbox', { name: 'Bitiş' }).fill('02:00');
  await form.getByRole('combobox', { name: 'Şube' }).selectOption('Merkez');
  await form.getByRole('textbox', { name: 'Açıklama' }).fill('Gece desteği');
  await form.getByRole('button', { name: 'Ekle' }).click();
  await expect(form.getByText('Ali Veli için çakışan vardiya var')).toBeVisible();
  await expect(start).toHaveValue('10:00');
  await expect(form.getByRole('textbox', { name: 'Açıklama' })).toHaveValue('Gece desteği');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toEqual({
    personelId: STAFF,
    tarih: '2026-09-21',
    baslangicSaat: '10:00',
    bitisSaat: '02:00',
    sube: 'Merkez',
    aciklama: 'Gece desteği',
    surum: null,
  });
  expect(await seriousViolations(page)).toEqual([]);

  const before = reportCalls.length;
  await form.getByRole('button', { name: 'Ekle' }).click();
  await expect(page.getByText('Vardiya eklendi.')).toBeVisible();
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar); // ilk istek yazılmadı: aynı işlem
  await expect.poll(() => reportCalls.length).toBeGreaterThan(before); // rapor tazelendi
  await expect(form.getByRole('textbox', { name: 'Açıklama' })).toHaveValue(''); // form sıfırlandı
  expect(errors).toEqual([]);
});

test('vardiya ekle: oturum düşünce form kaybolmaz — yerinde giriş, AYNI istek (aynı anahtar ve gövde)', async ({
  page,
}) => {
  let n = 0;
  const { written } = await shiftEndpoints(page, {
    write: async (r) => {
      if (++n > 1) return false;
      await problem(r, 401, 'oturum_yok', 'Oturum açık değil.');
      return true;
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
  await openPage(page);
  const form = await pickStaff(page);
  await form.getByRole('textbox', { name: 'Açıklama' }).fill('Hafta sonu');
  await form.getByRole('button', { name: 'Ekle' }).click();

  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(
    page.getByRole('textbox', { name: 'Açıklama', includeHidden: true }).first(),
  ).toHaveValue('Hafta sonu');
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByText('Vardiya eklendi.')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    personelId: STAFF,
    baslangicSaat: '08:00',
    bitisSaat: '18:00',
    aciklama: 'Hafta sonu',
  });
});

test('vardiya düzenleme: cakisma formu silmez — güncel kayıt birleşir, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let version = 'v-1';
  let branch = 'Merkez';
  let put = 0;
  const { written } = await shiftEndpoints(page, {
    record: () => shift({ surum: version, sube: branch }),
    write: async (r, method) => {
      if (method !== 'PUT') return false;
      if (++put === 1) {
        version = 'v-2';
        branch = 'Havalimanı'; // başka oturum şubeyi değiştirdi
        await problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti; güncel hâli yükleyin.');
        return true;
      }
      await r.fulfill({ json: shift({ surum: 'v-3', aciklama: 'Yeni not', sube: 'Havalimanı' }) });
      return true;
    },
  });
  await openPage(page);
  await page.getByRole('button', { name: /^Düzenle — Ali Veli/ }).click();
  const form = page.getByRole('region', { name: 'Vardiyayı Düzenle' });
  const note = form.getByRole('textbox', { name: 'Açıklama' });
  await expect(note).toHaveValue('Sabah');
  await expect(form.getByRole('textbox', { name: 'Başlangıç' })).toHaveValue('09:00');
  await note.fill('Yeni not');
  await form.getByRole('button', { name: 'Kaydet' }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt siz düzenlerken değişti');
  await expect(note).toHaveValue('Yeni not'); // form SİLİNMEDİ
  await expect(form.getByRole('combobox', { name: 'Şube' }).locator('option:checked')).toHaveText(
    'Havalimanı',
  );
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    surum: 'v-1',
    aciklama: 'Yeni not',
    baslangicSaat: '09:00',
    bitisSaat: '17:00',
  });
  await form.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Vardiya kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({
    surum: 'v-2',
    aciklama: 'Yeni not',
    sube: 'Havalimanı',
  });
  expect(written[1]?.path).toBe(`/api/ui/v1/vardiyalar/${SHIFT_ID}`);
  expect(errors).toEqual([]);
});

test('vardiya sil: onaylı; vazgeçince istek yok, onayda DELETE + rapor tazelenir', async ({
  page,
}) => {
  const { written, reportCalls } = await shiftEndpoints(page);
  await openPage(page);
  const del = page.getByRole('button', { name: /^Sil — Ali Veli/ });
  await del.click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Vazgeç' }).click();
  expect(written).toHaveLength(0);
  const before = reportCalls.length;
  await del.click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Sil' }).click();
  await expect(page.getByText('Vardiya silindi.')).toBeVisible();
  expect(written[0]).toMatchObject({ method: 'DELETE', path: `/api/ui/v1/vardiyalar/${SHIFT_ID}` });
  await expect.poll(() => reportCalls.length).toBeGreaterThan(before);
});

test('OperationsWrite olmayan kullanıcı: yazma bölümü yok, vardiya listesi salt okunur çizilir', async ({
  page,
}) => {
  await logIn(page, { ...BEN, rol: 'Muhasebe', izinler: ['FinanceWrite', 'ViewReports'] });
  await shiftEndpoints(page);
  await page.goto(PAGE);
  await expect(page.getByRole('heading', { name: 'Vardiyalar' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Vardiya Ekle' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Düzenle/ })).toHaveCount(0);
});

test.describe('yazma bölümü dolu listeyle: mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
  test('320/390/768 px gövde yatay taşması yok', async ({ page }) => {
    await shiftEndpoints(page);
    for (const width of [320, 390, 768]) {
      await page.setViewportSize({ width, height: 844 });
      await openPage(page);
      await expect(page.getByRole('button', { name: /^Sil — Ali Veli/ })).toBeVisible();
      expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
    }
  });
});
