import { expect, test, type Page, type Route } from '@playwright/test';

import {
  BEN,
  seriousViolations,
  collectErrors,
  kaydet,
  type KayitliIstek,
  logIn,
  problem,
  writeXsrf,
} from './ortak';
import { CUSTOMER_1, TERM_1, TERM_2, sharedEndpoints, reservationTerm } from './planlama-sahte';
import { waitReady, measureOverflow, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F5.2b rez şartları (`/app/rez-sartlari`): üç zorunlu senaryo ("doğrulama hatasında form korunur",
 * "oturum düşünce form kaybolmaz", "`cakisma` formu silmez") + oluştur/karşılandı/sil + axe + taşma.
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];
const LISTE = [
  reservationTerm(TERM_1),
  reservationTerm(TERM_2, {
    sart: 'Teslim havalimanında',
    karsilandi: true,
    karsilamaTarihi: '2026-09-21T09:00:00Z',
  }),
];

const PAGE: VitrinSayfasi = {
  ad: 'rez-sartlari',
  yol: '/app/rez-sartlari',
  baslik: 'Rez Şartları (Müşteri Özel Talepleri)',
  hazir: async (page) => {
    await expect(page.getByRole('grid', { name: 'Rez şartları' })).not.toHaveAttribute(
      'aria-busy',
      'true',
    );
    await expect(page.getByRole('gridcell', { name: 'Bebek koltuğu', exact: true })).toBeVisible();
  },
};

interface Uclar {
  yazma: (route: Route) => Promise<void> | void;
  detay?: () => unknown;
}

async function termEndpoints(page: Page, endpoints: Uclar): Promise<KayitliIstek[]> {
  const written: KayitliIstek[] = [];
  await page.route(
    (url) => url.pathname === '/api/ui/v1/rez-sartlari',
    (route) => {
      if (route.request().method() === 'GET')
        return route.fulfill({ json: { kayitlar: LISTE, toplam: 2, sayfaNo: 1, boyut: 50 } });
      written.push(kaydet(route.request()));
      return endpoints.yazma(route);
    },
  );
  await page.route('**/api/ui/v1/rez-sartlari/gruplar', (route) =>
    route.fulfill({ json: ['Ekipman'] }),
  );
  await page.route(
    (url) => url.pathname.startsWith('/api/ui/v1/rez-sartlari/b'),
    (route) => {
      if (route.request().method() === 'GET')
        return route.fulfill({
          json: endpoints.detay?.() ?? reservationTerm(TERM_1, { surum: 'surum-1' }),
        });
      written.push(kaydet(route.request()));
      return endpoints.yazma(route);
    },
  );
  return written;
}

async function fillNew(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Yeni şart / talep' }).click();
  const editor = page.getByRole('region', { name: 'Yeni şart / talep' });
  await editor.getByRole('combobox', { name: 'Müşteri' }).click();
  await editor.getByRole('combobox', { name: 'Müşteri' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  await editor.getByRole('textbox', { name: 'Şart / talep' }).fill('Çocuk koltuğu (9 kg)');
}

async function isFormPreserved(page: Page): Promise<void> {
  // Diyalog açıkken arka plan erişilebilirlik ağacından gizlidir (modal): gizliler de aranır.
  const hidden = { includeHidden: true } as const;
  const editor = page.getByRole('region', { name: 'Yeni şart / talep', ...hidden });
  await expect(editor.getByRole('combobox', { name: 'Müşteri', ...hidden })).toHaveValue(
    'Ayşe Yılmaz',
  );
  await expect(editor.getByRole('textbox', { name: 'Şart / talep', ...hidden })).toHaveValue(
    'Çocuk koltuğu (9 kg)',
  );
}

test.beforeEach(async ({ page }) => {
  await logIn(page);
  await sharedEndpoints(page);
});

test('liste + oluştur: gövde API adlarıyla, talep tarihi bugün (İstanbul gece yarısı); axe iki tema', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const written = await termEndpoints(page, {
    yazma: (r) => r.fulfill({ status: 201, json: reservationTerm(TERM_1) }),
  });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);
  await expect(page.getByText('2 kayıt · 2 bekleyen')).toBeVisible();
  await expect(page.getByText('Karşılandı 21.09.2026')).toBeVisible();
  expect(await seriousViolations(page), 'açık tema').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await seriousViolations(page), 'koyu tema').toEqual([]);

  await fillNew(page);
  expect(await seriousViolations(page), 'form açık').toEqual([]);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText('Şart / talep oluşturuldu.')).toBeVisible();
  const body = JSON.parse(written[0]?.govde ?? '{}') as Record<string, unknown>;
  expect(body).toMatchObject({
    musteriId: CUSTOMER_1,
    sart: 'Çocuk koltuğu (9 kg)',
    karsilamaTarihi: null,
  });
  expect(String(body['talepTarihi'])).toMatch(/T21:00:00\.000Z$/);
  expect(written[0]?.anahtar).toBeTruthy();
  expect(errors).toEqual([]);
});

test('doğrulama hatasında form korunur: alan işaretlenir, değerler yerinde', async ({ page }) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await termEndpoints(page, {
    yazma: (r) =>
      problem(r, 400, 'dogrulama', 'Şart metni en çok 512 karakter olabilir.', {
        errors: { sart: ['Şart metni en çok 512 karakter olabilir.'] },
      }),
  });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);
  await fillNew(page);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  const alan = page.getByRole('textbox', { name: 'Şart / talep' });
  await expect(alan).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByText('Şart metni en çok 512 karakter olabilir.')).toBeVisible();
  await isFormPreserved(page);
  expect(errors).toEqual([]);
});

test('oturum düşünce form kaybolmaz: yerinde giriş → AYNI istek (aynı anahtar) tekrarlanır', async ({
  page,
}) => {
  let counter = 0;
  const written = await termEndpoints(page, {
    yazma: (r) =>
      ++counter === 1
        ? problem(r, 401, 'oturum_yok', 'Oturum açık değil.')
        : r.fulfill({ status: 201, json: reservationTerm(TERM_1) }),
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await writeXsrf(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await writeXsrf(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);
  await fillNew(page);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await isFormPreserved(page);
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByText('Şart / talep oluşturuldu.')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
});

test('cakisma formu silmez: bayat sürüm 409 → güncel kayıt birleşir (dokunulan korunur), sonraki PUT yeni sürümle', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let version = 'surum-1';
  let deliverer: string | null = null;
  let putCount = 0;
  const written = await termEndpoints(page, {
    detay: () => reservationTerm(TERM_1, { surum: version, teslimEden: deliverer }),
    yazma: (r) => {
      if (++putCount === 1) {
        // Başka oturum bu arada teslim edeni yazdı → sürüm değişti.
        version = 'surum-2';
        deliverer = 'Veli';
        return problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti; güncel hâli yükleyin.');
      }
      return r.fulfill({ json: reservationTerm(TERM_1, { surum: 'surum-3' }) });
    },
  });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);
  await page.getByRole('button', { name: 'Düzenle Bebek koltuğu' }).click();
  const editor = page.getByRole('region', { name: 'Şartı düzenle' });
  const term = editor.getByRole('textbox', { name: 'Şart / talep' });
  await expect(term).toHaveValue('Bebek koltuğu');
  await term.fill('Bebek koltuğu + yükseltici');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt siz düzenlerken değişti');
  await expect(term).toHaveValue('Bebek koltuğu + yükseltici'); // form SİLİNMEDİ
  await expect(editor.getByRole('textbox', { name: 'Teslim eden' })).toHaveValue('Veli'); // dokunulmayan güncellendi
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    surum: 'surum-1',
    teslimEden: null,
  });

  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText('Şart / talep kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({
    surum: 'surum-2',
    sart: 'Bebek koltuğu + yükseltici',
    teslimEden: 'Veli',
    basTar: '2026-09-30T21:00:00Z', // dokunulmayan tarih sunucunun anıyla aynen
  });
  expect(errors).toEqual([]);
});

test('karşılandı / geri al / sil (onaylı) satır işlemleri doğru uca gider', async ({ page }) => {
  const paths: string[] = [];
  await termEndpoints(page, {
    yazma: (r) => {
      paths.push(`${r.request().method()} ${new URL(r.request().url()).pathname}`);
      return r.request().method() === 'DELETE'
        ? r.fulfill({ status: 204 })
        : r.fulfill({ json: reservationTerm(TERM_1) });
    },
  });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);
  await page.getByRole('button', { name: 'Karşılandı Bebek koltuğu' }).click();
  await expect(page.getByText('Karşılandı olarak işaretlendi.')).toBeVisible();
  await page.getByRole('button', { name: 'Geri al Teslim havalimanında' }).click();
  await expect(page.getByText('Karşılama geri alındı.')).toBeVisible();
  await page.getByRole('button', { name: 'Sil Bebek koltuğu' }).click();
  const approval = page.getByRole('alertdialog').or(page.getByRole('dialog'));
  await expect(approval).toContainText('“Bebek koltuğu” talebi silinecek.');
  await approval.getByRole('button', { name: 'Sil' }).click();
  await expect(page.getByText('Talep silindi.')).toBeVisible();
  expect(paths).toEqual([
    `POST /api/ui/v1/rez-sartlari/${TERM_1}/karsilandi`,
    `POST /api/ui/v1/rez-sartlari/${TERM_2}/geri-al`,
    `DELETE /api/ui/v1/rez-sartlari/${TERM_1}`,
  ]);
});

test.describe('mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
  test('/app/rez-sartlari: 320/390/768 px gövde yatay taşması yok (form açıkken dahil)', async ({
    page,
  }) => {
    await termEndpoints(page, {
      yazma: (r) => r.fulfill({ status: 201, json: reservationTerm(TERM_1) }),
    });
    for (const width of [320, 390, 768]) {
      await page.setViewportSize({ width: width, height: 844 });
      await page.goto(PAGE.yol);
      await waitReady(page, PAGE);
      expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      await page.getByRole('button', { name: 'Yeni şart / talep' }).click();
      expect(await measureOverflow(page), `${width}px form`).toEqual({ tasma: 0, suclular: [] });
    }
  });
});

test('/app/rez-sartlari: 1440 px gövde yatay taşması yok', async ({ page }) => {
  await termEndpoints(page, {
    yazma: (r) => r.fulfill({ status: 201, json: reservationTerm(TERM_1) }),
  });
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);
  expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
});
