import { expect, test, type Page, type Request } from '@playwright/test';

import {
  seriousViolations,
  collectErrors,
  kaydet,
  type KayitliIstek,
  logIn,
  problem,
} from './ortak';
import { waitReady, measureOverflow, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F4.2 kira listesi (`/app/kiralar`), sahte `/api/ui/v1` ile (harness yalnız statik SPA sunar):
 * sunucu sayfalı liste, dışa aktarma/PDF bağlantıları (Blazor GET uçları — SPA'ya yönlenmez), süzgecin
 * URL'e yazılması ve PARA: "Tahsil Et" DTO anahtarını aynen geri gönderir, istek uçarken kilitli,
 * 409 `mukerrer`'de yeniden gönderim yok + liste yenilenir, `cakisma` paneli/değeri silmez.
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];

/** #257: sunucu `tahsilatAnahtar`'ı yeniden hesaplar; bayat/başka kiranın anahtarı bu detail ile 409 mukerrer. */
const STALE_DETAIL =
  'Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti ya da tahsilat anahtarı bu ' +
  'kiraya ait değil; kaydı yeniden yükleyip tekrar deneyin.';

const KEY_1 = 'aaaaaaaa-0000-5000-8000-000000000001';
const KEY_2 = 'aaaaaaaa-0000-5000-8000-000000000002';

function satir(no: string, extra: Record<string, unknown> = {}) {
  const id = `${no.slice(-8)}-0000-4000-8000-000000000000`;
  return {
    id,
    sozlesmeNo: no,
    musteriId: 'c0000000-0000-4000-8000-000000000001',
    musteriAd: 'Ayşe Yılmaz',
    plaka: '34 ABC 123',
    basTar: '2026-09-20T07:00:00Z',
    bitTar: '2026-09-23T07:00:00Z',
    vadeTar: '2026-09-30T00:00:00Z',
    gun: 3,
    hediyeGun: null,
    faturalananGun: 3,
    tutar: 3600,
    bakiye: 1234.5,
    doviz: 'TRY',
    kaynak: 'Web',
    cikisOfisi: 'Merkez',
    donusOfisi: 'Havalimanı',
    provizyon: 5000,
    depozito: null,
    komisyonOran: null,
    komisyonTutar: null,
    onayKodu: null,
    projeAdi: null,
    assistFirma: null,
    ozelSoforBilgisi: null,
    durum: 'Kirada',
    faturali: false,
    tahsilat: null,
    ...extra,
  };
}

const RENTAL_1 = satir('2026220901001');
const withCollection = (key: string, balance = 1234.5) => ({
  ...RENTAL_1,
  bakiye: balance,
  tahsilat: {
    anahtar: key,
    cariId: RENTAL_1.musteriId,
    rentalId: RENTAL_1.id,
    doviz: 'TRY',
    varsayilanTutar: balance,
  },
});
const RENTAL_2 = satir('2026220901002', { durum: 'Tamamlandi', bakiye: 0, faturali: true });

interface Sahte {
  /** Liste ucunun sıradaki yanıtı (her GET'te çağrılır). */
  satirlar: () => unknown[];
  readonly listeIstekleri: URL[];
}

async function rentalEndpoints(page: Page, rows: () => unknown[]): Promise<Sahte> {
  const fake: Sahte = { satirlar: rows, listeIstekleri: [] };
  await page.route(
    (url) => url.pathname === '/api/ui/v1/kiralar',
    (route) => {
      fake.listeIstekleri.push(new URL(route.request().url()));
      const records = fake.satirlar();
      return route.fulfill({
        json: { kayitlar: records, toplam: records.length, sayfaNo: 1, boyut: 50 },
      });
    },
  );
  await page.route(
    (url) => url.pathname === '/api/ui/v1/kiralar/ozet',
    (route) => route.fulfill({ json: { toplam: 2, kirada: 1, faturasiz: 1 } }),
  );
  await page.route('**/api/ui/v1/kiralar/filtre-secenekleri', (route) =>
    route.fulfill({ json: { sahipler: ['Filo A'], gruplar: ['Ekonomi'] } }),
  );
  await page.route('**/api/ui/v1/tablo-duzenleri/**', (route) =>
    route.fulfill({ json: { tabloKodu: 'kiralar.liste', duzen: null, guncellemeUtc: null } }),
  );
  await page.route(
    (url) => url.pathname === '/api/ui/v1/finans/hesaplar',
    (route) => route.fulfill({ json: [] }),
  );
  return fake;
}

const PAGE: VitrinSayfasi = {
  ad: 'kiralar',
  yol: '/app/kiralar',
  baslik: 'Kira listesi',
  hazir: async (page) => {
    const grid = page.getByRole('grid', { name: 'Kira sözleşmeleri' });
    await expect(grid).not.toHaveAttribute('aria-busy', 'true');
    await expect(grid.getByRole('link', { name: '2026220901001', exact: true })).toBeVisible();
  },
};

const collectButton = (page: Page) => page.getByRole('button', { name: 'Tahsil et 2026220901001' });
const panelGonder = (page: Page) => page.getByRole('button', { name: 'Tahsil et', exact: true });
const panel = (page: Page) => page.getByRole('region', { name: 'Tahsilat — 2026220901001' });

test.beforeEach(async ({ page }) => logIn(page));

test('liste: axe iki temada ciddi/kritik 0, konsol hatası yok; bağlantılar Blazor uçlarına, süzgeç URL’e', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const fake = await rentalEndpoints(page, () => [withCollection(KEY_1), RENTAL_2]);
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);

  await expect(page.getByText('2 sözleşme · 1 kirada · 1 faturasız')).toBeVisible();
  // Para sağa yaslı, tr biçimi, kiranın dövizi.
  const balance = page.getByRole('gridcell', { name: '1.234,50 ₺' });
  await expect(balance).toBeVisible();
  expect(await balance.evaluate((td) => getComputedStyle(td).textAlign)).toMatch(/^(right|end)$/);

  // Sözleşme no → SPA kira formu rotası; PDF ve dışa aktarma → Blazor GET (tam sayfa/yeni sekme).
  await expect(page.getByRole('link', { name: '2026220901001', exact: true })).toHaveAttribute(
    'href',
    `/app/kiralar/${RENTAL_1.id}`,
  );
  const pdf = page.getByRole('link', { name: /PDF 2026220901001 sözleşmesi/ });
  await expect(pdf).toHaveAttribute('href', `/kiralar/${RENTAL_1.id}/pdf`);
  await expect(pdf).toHaveAttribute('target', '_blank');
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/kiralar?format=excel',
  );
  await expect(collectButton(page)).toHaveCount(1); // yalnız sunucunun tahsilat verdiği satır
  await expect(page.getByRole('button', { name: /Tahsil et 2026220901002/ })).toHaveCount(0);

  expect(await seriousViolations(page), 'açık tema').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect
    .poll(() => page.evaluate(() => getComputedStyle(document.body).backgroundColor))
    .toBe('rgb(20, 19, 16)');
  expect(await seriousViolations(page), 'koyu tema').toEqual([]);

  // Süzgeç: URL'e yazılır, API aynı adla çağrılır, dışa aktarma süzgeci taşır.
  await page.getByRole('searchbox', { name: 'Ara', exact: true }).fill('Yılmaz');
  await page.getByRole('button', { name: 'Filtrele', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/kiralar\?q=Y%C4%B1lmaz$/);
  await expect.poll(() => fake.listeIstekleri.at(-1)?.searchParams.get('q')).toBe('Yılmaz');
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/kiralar?format=excel&q=Y%C4%B1lmaz',
  );
  expect(await seriousViolations(page), 'filtre açık').toEqual([]);
  expect(errors).toEqual([]);
});

test('Tahsil Et (PARA): DTO anahtarı aynen gider, uçarken kilitli, 2xx → liste yeni anahtarla yenilenir', async ({
  page,
}) => {
  const errors = collectErrors(page);
  let balance = 1234.5;
  let key = KEY_1;
  const fake = await rentalEndpoints(page, () => [withCollection(key, balance), RENTAL_2]);
  const sent: KayitliIstek[] = [];
  let release: () => void = () => undefined;
  const pending = new Promise<void>((r) => (release = r));
  await page.route('**/api/ui/v1/finans/tahsilat', async (route) => {
    sent.push(kaydet(route.request()));
    await pending;
    balance = 234.5;
    key = KEY_2;
    await route.fulfill({ json: { id: 'f0000000-0000-4000-8000-000000000001' } });
  });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);

  await collectButton(page).click();
  await expect(panel(page)).toBeVisible();
  const amount = page.getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toBeFocused();
  await amount.fill('1000');
  await panelGonder(page).click();

  // Uçarken: gönder ve satır düğmesi kilitli; ikinci tık yeni istek üretmez.
  await expect(page.getByRole('button', { name: 'Gönderiliyor…' })).toBeDisabled();
  await expect(collectButton(page)).toBeDisabled();
  await page.getByRole('button', { name: 'Gönderiliyor…' }).click({ force: true });
  const listBefore = fake.listeIstekleri.length;
  release();

  await expect(page.getByText('2026220901001: 1.000,00 ₺ tahsil edildi.')).toBeVisible();
  await expect(panel(page)).toHaveCount(0);
  expect(sent).toHaveLength(1);
  const body = JSON.parse(sent[0]?.govde ?? '{}') as Record<string, unknown>;
  expect(body).toEqual({
    cariId: RENTAL_1.musteriId,
    tutar: '1000.00',
    hesap: 'Kasa',
    kiraId: RENTAL_1.id,
    doviz: 'TRY',
    hesapId: null,
    kanal: 'Masaüstü',
    aciklama: 'Hızlı tahsilat (liste) — 2026220901001',
    tahsilatAnahtar: KEY_1,
  });
  expect(sent[0]?.anahtar).toBe(KEY_1);
  expect(sent[0]?.xsrf).toBe('eski-belirtec');

  // 2xx sonrası liste tazelendi; yeni bakiye ve yeni anahtarla tekrar tahsil edilebilir.
  await expect.poll(() => fake.listeIstekleri.length).toBeGreaterThan(listBefore);
  await expect(page.getByRole('gridcell', { name: '234,50 ₺' })).toBeVisible();
  await collectButton(page).click();
  await expect(page.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('234,50');
  expect(errors).toEqual([]);
});

test('409 mukerrer (bayat anahtar): yeniden gönderim YOK, liste yenilenir + sunucu detayı; 409 cakisma paneli ve değeri silmez', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  const fake = await rentalEndpoints(page, () => [withCollection(KEY_1), RENTAL_2]);
  const sent: Request[] = [];
  await page.route('**/api/ui/v1/finans/tahsilat', (route) => {
    sent.push(route.request());
    return sent.length === 1
      ? problem(route, 409, 'cakisma', 'Kayıt başka bir işlemle değişti; kontrol edin.')
      : problem(route, 409, 'mukerrer', STALE_DETAIL);
  });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);

  await collectButton(page).click();
  await page.getByRole('textbox', { name: 'Tutar', exact: true }).fill('500,25');

  // cakisma: bant; panel açık, yazılan tutar yerinde, liste yeniden YÜKLENMEZ.
  const listBefore = fake.listeIstekleri.length;
  await panelGonder(page).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt başka bir işlemle değişti');
  await expect(panel(page)).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('500,25');
  expect(fake.listeIstekleri.length).toBe(listBefore);

  // mukerrer: tek istek, otomatik tekrar yok; panel kapanır, liste yeniden yüklenir, bilgi toast'u.
  await panelGonder(page).click();
  // Sunucunun detail'ı "Kira kaydı değişmiş" uyarısıyla; "Mükerrer işlem" (kaydedildi izlenimi) YOK.
  await expect(page.getByText('Kira kaydı değişmiş')).toBeVisible();
  await expect(page.getByText(STALE_DETAIL, { exact: false })).toBeVisible();
  await expect(page.getByText('Mükerrer işlem')).toHaveCount(0);
  await expect(panel(page)).toHaveCount(0);
  await expect.poll(() => fake.listeIstekleri.length).toBeGreaterThan(listBefore);
  await page.waitForTimeout(300);
  expect(sent).toHaveLength(2);
  expect(sent.map((r) => r.postDataJSON().tahsilatAnahtar)).toEqual([KEY_1, KEY_1]);
  expect(errors).toEqual([]);
});

test('Tahsil Et tutarı (PARA): odaklı açılışta öneri seçili — doğrudan “90” = 90.00; “1,555” alan hatası, istek yok', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await rentalEndpoints(page, () => [withCollection(KEY_1, 1250.5), RENTAL_2]);
  const sent: KayitliIstek[] = [];
  await page.route('**/api/ui/v1/finans/tahsilat', async (route) => {
    sent.push(kaydet(route.request()));
    await route.fulfill({ json: { id: 'f0000000-0000-4000-8000-000000000001' } });
  });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);

  // Adversarial F3: öneri "1250,50" iken doğrudan yazılan "90" SONUNA eklenip 1250.51 gönderiliyordu.
  await collectButton(page).click();
  const amount = page.getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toBeFocused();
  await page.keyboard.type('90');
  await expect(amount).toHaveValue('90');
  await panelGonder(page).click();
  await expect.poll(() => sent.length).toBe(1);
  expect(JSON.parse(sent[0]?.govde ?? '{}')).toMatchObject({
    tutar: '90.00',
    tahsilatAnahtar: KEY_1,
  });

  // 3 ondalık: yuvarlanmaz, alan hatası, istek gitmez.
  await collectButton(page).click();
  await amount.fill('1,555');
  await panelGonder(page).click();
  await expect(page.getByText('En fazla 2 ondalık hane girilebilir.')).toBeVisible();
  await expect(amount).toHaveAttribute('aria-invalid', 'true');
  await expect(amount).toHaveValue('1,555');
  await page.waitForTimeout(300);
  expect(sent).toHaveLength(1);
  expect(await seriousViolations(page)).toEqual([]);
  expect(errors).toEqual([]);
});

test.describe('mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  test('/app/kiralar: 320/390/768 px gövde yatay taşması yok (tahsilat paneli açıkken dahil)', async ({
    page,
  }) => {
    await rentalEndpoints(page, () => [withCollection(KEY_1), RENTAL_2]);
    for (const width of [320, 390, 768]) {
      await page.setViewportSize({ width: width, height: 844 });
      await page.goto(PAGE.yol);
      await waitReady(page, PAGE);
      expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      await collectButton(page).click();
      await expect(panel(page)).toBeVisible();
      expect(await measureOverflow(page), `${width}px panel`).toEqual({ tasma: 0, suclular: [] });
    }
  });
});

test('/app/kiralar: 1440 px gövde yatay taşması yok', async ({ page }) => {
  await rentalEndpoints(page, () => [withCollection(KEY_1), RENTAL_2]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(PAGE.yol);
  await waitReady(page, PAGE);
  expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
});
