import { expect, test, type Page } from '@playwright/test';

import { BEN, logIn, problem, writeXsrf } from './ortak';

/**
 * F4.5 Panel "Tahsil Et" — bağımsız SPA adversarial incelemesinin (#259) probe'larından kalıcı testler.
 * Beklenen değerler elle kurulmuş senaryodan (bağımsız oracle): A satırı 1.250,50, B satırı 90,00.
 */
const PANEL = '/app/panel';
const KEY_A = 'aaaaaaaa-6a1d-5b7e-9c3a-2d4e6f8a0b1c';
const KEY_B = 'bbbbbbbb-6a1d-5b7e-9c3a-2d4e6f8a0b1c';
const tier = { yediGun: 0, otuzGun: 0, gecmis: 0 };

function satir(no: string, plate: string, balance: number, key: string, currency = 'TRY') {
  return {
    rentalId: `kira-${no}`,
    sozlesmeNo: no,
    tarih: '2026-09-21T09:00:00Z',
    musteriAd: `Müşteri ${no}`,
    plaka: plate,
    ofis: 'Merkez',
    bakiye: balance,
    doviz: currency,
    tahsilat: {
      anahtar: key,
      cariId: `cari-${no}`,
      rentalId: `kira-${no}`,
      doviz: currency,
      varsayilanTutar: balance,
    },
  };
}

const A = satir('2026200901001', '34 AAA 01', 1250.5, KEY_A);
const B = satir('2026200901002', '06 BBB 02', 90, KEY_B);

function ozet(overdue: object[]) {
  return {
    bugun: '2026-09-22',
    kpi: {
      toplamArac: 10,
      kirada: 5,
      musait: 5,
      serviste: 0,
      acikRezervasyon: 0,
      kmGecenBakim: 0,
      gorulmeyenRezervasyon: 0,
      siteTalebi: null,
    },
    vade: {
      trafik: tier,
      kasko: tier,
      muayene: tier,
      gecmisUyari: 0,
      yaklasanUyari: 0,
      acikSikayet: 0,
    },
    donusler: { gecikmis: overdue, bugun: [], yarin: [], varsayilanSekme: 'gec' },
    cikislar: { gecikmis: [], bugun: [], yarin: [], varsayilanSekme: 'bugun' },
    finans: null,
  };
}

async function fake(page: Page, rows: object[]): Promise<{ ozet: number }> {
  const counter = { ozet: 0 };
  await page.route('**/api/ui/v1/panel/ozet', (route) => {
    counter.ozet++;
    return route.fulfill({ json: ozet(rows) });
  });
  await page.route('**/api/ui/v1/finans/hesaplar', (route) => route.fulfill({ json: [] }));
  return counter;
}

interface Gonderim {
  readonly govde: Record<string, unknown>;
  readonly anahtar: string | undefined;
  readonly xsrf: string | undefined;
}

type Decision = 'ok' | 'oturum' | 'bekle';

async function captureCollection(page: Page, decision: (n: number) => Decision) {
  const list: Gonderim[] = [];
  let release: () => void = () => undefined;
  const wait = new Promise<void>((r) => (release = r));
  await page.route('**/api/ui/v1/finans/tahsilat', async (route) => {
    const h = route.request().headers();
    list.push({
      govde: route.request().postDataJSON() as Record<string, unknown>,
      anahtar: h['idempotency-key'],
      xsrf: h['x-xsrf-token'],
    });
    const k = decision(list.length);
    if (k === 'oturum') return problem(route, 401, 'oturum_yok', 'Oturum açık değil.');
    if (k === 'bekle') await wait;
    return route.fulfill({ json: { id: `islem-${list.length}` } });
  });
  return { liste: list, birak: () => release() };
}

const collectButton = (page: Page, plate: string) =>
  page.getByRole('button', { name: `${plate} için tahsil et` });
const form = (page: Page) => page.getByRole('form', { name: /Tahsilat/ });

test.beforeEach(async ({ page }) => logIn(page));

test('F1: A formu açıkken B "Tahsil Et" → tutar B’nin bakiyesi, gövde B’nin; uçarken tüm düğmeler pasif', async ({
  page,
}) => {
  await fake(page, [A, B]);
  const { liste, birak } = await captureCollection(page, () => 'bekle');
  await page.goto(PANEL);
  await collectButton(page, '34 AAA 01').click();
  await expect(form(page).getByLabel('Tutar')).toHaveValue('1250,50');

  await collectButton(page, '06 BBB 02').click();
  await expect(form(page)).toHaveAccessibleName(/06 BBB 02/);
  await expect(form(page).getByLabel('Tutar')).toHaveValue('90,00');
  await form(page).getByRole('button', { name: 'Tahsil et' }).click();
  await expect.poll(() => liste.length).toBe(1);
  expect(liste[0]?.govde).toMatchObject({
    kiraId: 'kira-2026200901002',
    cariId: 'cari-2026200901002',
    tutar: '90.00',
    tahsilatAnahtar: KEY_B,
  });

  // İstek uçarken: tablodaki "Tahsil Et"ler ve "Yenile" pasif (başka satır açılamaz, bildirim karışmaz).
  await expect(collectButton(page, '34 AAA 01')).toBeDisabled();
  await expect(collectButton(page, '06 BBB 02')).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Yenile' })).toBeDisabled();
  birak();
  await expect(
    page.getByRole('status').filter({ hasText: 'Tahsilat kaydedildi: 90,00 ₺ (06 BBB 02).' }),
  ).toBeVisible();
  expect(liste).toHaveLength(1);
});

test('F3: otomatik odakta ön dolu tutar SEÇİLİ — doğrudan "90" yazan 90,00 gönderir', async ({
  page,
}) => {
  await fake(page, [A]);
  const { liste } = await captureCollection(page, () => 'ok');
  await page.goto(PANEL);
  await collectButton(page, '34 AAA 01').click();
  const amount = form(page).getByLabel('Tutar');
  await expect(amount).toBeFocused();
  await expect
    .poll(() => amount.evaluate((g: HTMLInputElement) => [g.selectionStart, g.selectionEnd]))
    .toEqual([0, '1250,50'.length]);
  await page.keyboard.type('90');
  await expect(amount).toHaveValue('90');
  await form(page).getByRole('button', { name: 'Tahsil et' }).click();
  await expect.poll(() => liste.length).toBe(1);
  expect(liste[0]?.govde['tutar']).toBe('90.00');
});

test('F3/P259-8b: Tab ile tutara dönen kullanıcı da seçili metnin yerine yazar ("90" → 90.00)', async ({
  page,
}) => {
  await fake(page, [A]);
  const { liste } = await captureCollection(page, () => 'ok');
  await page.goto(PANEL);
  await collectButton(page, '34 AAA 01').click();
  const amount = form(page).getByLabel('Tutar');
  await expect(amount).toBeFocused();
  await form(page).getByLabel('Hesap türü').focus();
  await page.keyboard.press('Shift+Tab');
  await expect(amount).toBeFocused();
  await page.keyboard.type('90');
  await expect(amount).toHaveValue('90');
  await form(page).getByRole('button', { name: 'Tahsil et' }).click();
  await expect.poll(() => liste.length).toBe(1);
  expect(liste[0]?.govde['tutar']).toBe('90.00');
});

test('F3: fazla ondalık YUVARLANMAZ — "1,555" alan hatası, istek gitmez', async ({ page }) => {
  await fake(page, [A]);
  const { liste } = await captureCollection(page, () => 'ok');
  await page.goto(PANEL);
  await collectButton(page, '34 AAA 01').click();
  await form(page).getByLabel('Tutar').fill('1,555');
  await form(page).getByRole('button', { name: 'Tahsil et' }).click();
  await expect(form(page).getByLabel('Tutar')).toHaveAttribute('aria-invalid', 'true');
  await expect(form(page).getByLabel('Tutar')).toHaveValue('1,555');
  expect(liste).toHaveLength(0);
});

test('Enter + tık + çift tık: TEK istek; Idempotency-Key başlığı yok, gövde anahtarı sunucununki', async ({
  page,
}) => {
  await fake(page, [A]);
  const { liste, birak } = await captureCollection(page, () => 'bekle');
  await page.goto(PANEL);
  await collectButton(page, '34 AAA 01').click();
  const amount = form(page).getByLabel('Tutar');
  await amount.fill('100');
  await amount.press('Enter');
  await amount.press('Enter');
  const gonder = form(page).getByRole('button', { name: /Tahsil et|Gönderiliyor/ });
  await gonder.click({ force: true });
  await gonder.dblclick({ force: true });
  await expect.poll(() => liste.length).toBe(1);
  birak();
  await expect(page.getByRole('status').filter({ hasText: 'Tahsilat kaydedildi' })).toBeVisible();
  expect(liste).toHaveLength(1);
  expect(liste[0]?.anahtar).toBeUndefined();
  expect(liste[0]?.govde['tahsilatAnahtar']).toBe(KEY_A);
  expect(liste[0]?.govde['tutar']).toBe('100.00');
});

test('oturum_yok → yerinde giriş → AYNI gövde (tahsilatAnahtar dahil) taze XSRF ile bir kez tekrar', async ({
  page,
}) => {
  await fake(page, [A]);
  const { liste } = await captureCollection(page, (n) => (n === 1 ? 'oturum' : 'ok'));
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await writeXsrf(page, 'anonim');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await writeXsrf(page, 'yeni');
    return route.fulfill({ json: BEN });
  });
  await page.goto(PANEL);
  await collectButton(page, '34 AAA 01').click();
  await form(page).getByLabel('Tutar').fill('321,09');
  await form(page).getByRole('button', { name: 'Tahsil et' }).click();
  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Parola').fill('rastgele-e2e');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Tahsilat kaydedildi' })).toBeVisible();
  expect(liste).toHaveLength(2);
  expect(liste[1]?.govde).toEqual(liste[0]?.govde);
  expect(liste[1]?.govde['tahsilatAnahtar']).toBe(KEY_A);
  expect(liste[1]?.govde['tutar']).toBe('321.09');
  expect(liste[1]?.xsrf).toBe('yeni');
});

test('otomatik tazeleme: form açık ve yazılmışken 130 sn → tazeleme yok, tutar korunur', async ({
  page,
}) => {
  await page.clock.install({ time: new Date('2026-09-22T09:00:00+03:00') });
  const counter = await fake(page, [A]);
  await captureCollection(page, () => 'ok');
  await page.goto(PANEL);
  await collectButton(page, '34 AAA 01').click();
  await form(page).getByLabel('Tutar').fill('777,77');
  await page.clock.fastForward(130_000);
  expect(counter.ozet).toBe(1);
  await expect(form(page).getByLabel('Tutar')).toHaveValue('777,77');

  // Form kapanınca ertelenen tazeleme gelir (bileşen zamanlayıcısı; meta-refresh yok).
  await form(page).getByRole('button', { name: 'Vazgeç' }).click();
  await page.locator('body').click({ position: { x: 1, y: 1 } });
  await page.clock.fastForward(20_000);
  await expect.poll(() => counter.ozet).toBe(2);
});

test('EUR satırı: döviz EUR gider, kur gönderilmez', async ({ page }) => {
  await fake(page, [satir('2026200901003', '35 EUR 03', 100, KEY_A, 'EUR')]);
  const { liste } = await captureCollection(page, () => 'ok');
  await page.goto(PANEL);
  await collectButton(page, '35 EUR 03').click();
  await form(page).getByRole('button', { name: 'Tahsil et' }).click();
  await expect.poll(() => liste.length).toBe(1);
  expect(liste[0]?.govde['doviz']).toBe('EUR');
  expect(liste[0]?.govde['tutar']).toBe('100.00');
  expect('kur' in (liste[0]?.govde ?? {})).toBe(false);
});
