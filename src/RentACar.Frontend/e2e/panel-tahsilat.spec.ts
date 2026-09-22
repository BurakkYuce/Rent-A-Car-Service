import { expect, test, type Page } from '@playwright/test';

import { BEN, oturumAc, problem, xsrfYaz } from './ortak';

/**
 * F4.5 Panel "Tahsil Et" — bağımsız SPA adversarial incelemesinin (#259) probe'larından kalıcı testler.
 * Beklenen değerler elle kurulmuş senaryodan (bağımsız oracle): A satırı 1.250,50, B satırı 90,00.
 */
const PANEL = '/app/panel';
const ANAHTAR_A = 'aaaaaaaa-6a1d-5b7e-9c3a-2d4e6f8a0b1c';
const ANAHTAR_B = 'bbbbbbbb-6a1d-5b7e-9c3a-2d4e6f8a0b1c';
const kademe = { yediGun: 0, otuzGun: 0, gecmis: 0 };

function satir(no: string, plaka: string, bakiye: number, anahtar: string, doviz = 'TRY') {
  return {
    rentalId: `kira-${no}`,
    sozlesmeNo: no,
    tarih: '2026-09-21T09:00:00Z',
    musteriAd: `Müşteri ${no}`,
    plaka,
    ofis: 'Merkez',
    bakiye,
    doviz,
    tahsilat: {
      anahtar,
      cariId: `cari-${no}`,
      rentalId: `kira-${no}`,
      doviz,
      varsayilanTutar: bakiye,
    },
  };
}

const A = satir('2026200901001', '34 AAA 01', 1250.5, ANAHTAR_A);
const B = satir('2026200901002', '06 BBB 02', 90, ANAHTAR_B);

function ozet(gecikmis: object[]) {
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
      trafik: kademe,
      kasko: kademe,
      muayene: kademe,
      gecmisUyari: 0,
      yaklasanUyari: 0,
      acikSikayet: 0,
    },
    donusler: { gecikmis, bugun: [], yarin: [], varsayilanSekme: 'gec' },
    cikislar: { gecikmis: [], bugun: [], yarin: [], varsayilanSekme: 'bugun' },
    finans: null,
  };
}

async function sahte(page: Page, satirlar: object[]): Promise<{ ozet: number }> {
  const sayac = { ozet: 0 };
  await page.route('**/api/ui/v1/panel/ozet', (route) => {
    sayac.ozet++;
    return route.fulfill({ json: ozet(satirlar) });
  });
  await page.route('**/api/ui/v1/finans/hesaplar', (route) => route.fulfill({ json: [] }));
  return sayac;
}

interface Gonderim {
  readonly govde: Record<string, unknown>;
  readonly anahtar: string | undefined;
  readonly xsrf: string | undefined;
}

type Karar = 'ok' | 'oturum' | 'bekle';

async function tahsilatYakala(page: Page, karar: (n: number) => Karar) {
  const liste: Gonderim[] = [];
  let birak: () => void = () => undefined;
  const bekle = new Promise<void>((r) => (birak = r));
  await page.route('**/api/ui/v1/finans/tahsilat', async (route) => {
    const h = route.request().headers();
    liste.push({
      govde: route.request().postDataJSON() as Record<string, unknown>,
      anahtar: h['idempotency-key'],
      xsrf: h['x-xsrf-token'],
    });
    const k = karar(liste.length);
    if (k === 'oturum') return problem(route, 401, 'oturum_yok', 'Oturum açık değil.');
    if (k === 'bekle') await bekle;
    return route.fulfill({ json: { id: `islem-${liste.length}` } });
  });
  return { liste, birak: () => birak() };
}

const tahsilDugmesi = (page: Page, plaka: string) =>
  page.getByRole('button', { name: `${plaka} için tahsil et` });
const form = (page: Page) => page.getByRole('form', { name: /Tahsilat/ });

test.beforeEach(async ({ page }) => oturumAc(page));

test('F1: A formu açıkken B "Tahsil Et" → tutar B’nin bakiyesi, gövde B’nin; uçarken tüm düğmeler pasif', async ({
  page,
}) => {
  await sahte(page, [A, B]);
  const { liste, birak } = await tahsilatYakala(page, () => 'bekle');
  await page.goto(PANEL);
  await tahsilDugmesi(page, '34 AAA 01').click();
  await expect(form(page).getByLabel('Tutar')).toHaveValue('1250,50');

  await tahsilDugmesi(page, '06 BBB 02').click();
  await expect(form(page)).toHaveAccessibleName(/06 BBB 02/);
  await expect(form(page).getByLabel('Tutar')).toHaveValue('90,00');
  await form(page).getByRole('button', { name: 'Tahsil et' }).click();
  await expect.poll(() => liste.length).toBe(1);
  expect(liste[0]?.govde).toMatchObject({
    kiraId: 'kira-2026200901002',
    cariId: 'cari-2026200901002',
    tutar: '90.00',
    tahsilatAnahtar: ANAHTAR_B,
  });

  // İstek uçarken: tablodaki "Tahsil Et"ler ve "Yenile" pasif (başka satır açılamaz, bildirim karışmaz).
  await expect(tahsilDugmesi(page, '34 AAA 01')).toBeDisabled();
  await expect(tahsilDugmesi(page, '06 BBB 02')).toBeDisabled();
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
  await sahte(page, [A]);
  const { liste } = await tahsilatYakala(page, () => 'ok');
  await page.goto(PANEL);
  await tahsilDugmesi(page, '34 AAA 01').click();
  const tutar = form(page).getByLabel('Tutar');
  await expect(tutar).toBeFocused();
  await expect
    .poll(() => tutar.evaluate((g: HTMLInputElement) => [g.selectionStart, g.selectionEnd]))
    .toEqual([0, '1250,50'.length]);
  await page.keyboard.type('90');
  await expect(tutar).toHaveValue('90');
  await form(page).getByRole('button', { name: 'Tahsil et' }).click();
  await expect.poll(() => liste.length).toBe(1);
  expect(liste[0]?.govde['tutar']).toBe('90.00');
});

test('Enter + tık + çift tık: TEK istek; Idempotency-Key başlığı yok, gövde anahtarı sunucununki', async ({
  page,
}) => {
  await sahte(page, [A]);
  const { liste, birak } = await tahsilatYakala(page, () => 'bekle');
  await page.goto(PANEL);
  await tahsilDugmesi(page, '34 AAA 01').click();
  const tutar = form(page).getByLabel('Tutar');
  await tutar.fill('100');
  await tutar.press('Enter');
  await tutar.press('Enter');
  const gonder = form(page).getByRole('button', { name: /Tahsil et|Gönderiliyor/ });
  await gonder.click({ force: true });
  await gonder.dblclick({ force: true });
  await expect.poll(() => liste.length).toBe(1);
  birak();
  await expect(page.getByRole('status').filter({ hasText: 'Tahsilat kaydedildi' })).toBeVisible();
  expect(liste).toHaveLength(1);
  expect(liste[0]?.anahtar).toBeUndefined();
  expect(liste[0]?.govde['tahsilatAnahtar']).toBe(ANAHTAR_A);
  expect(liste[0]?.govde['tutar']).toBe('100.00');
});

test('oturum_yok → yerinde giriş → AYNI gövde (tahsilatAnahtar dahil) taze XSRF ile bir kez tekrar', async ({
  page,
}) => {
  await sahte(page, [A]);
  const { liste } = await tahsilatYakala(page, (n) => (n === 1 ? 'oturum' : 'ok'));
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await xsrfYaz(page, 'anonim');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await xsrfYaz(page, 'yeni');
    return route.fulfill({ json: BEN });
  });
  await page.goto(PANEL);
  await tahsilDugmesi(page, '34 AAA 01').click();
  await form(page).getByLabel('Tutar').fill('321,09');
  await form(page).getByRole('button', { name: 'Tahsil et' }).click();
  const diyalog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(diyalog).toBeVisible();
  await diyalog.getByLabel('Parola').fill('rastgele-e2e');
  await diyalog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Tahsilat kaydedildi' })).toBeVisible();
  expect(liste).toHaveLength(2);
  expect(liste[1]?.govde).toEqual(liste[0]?.govde);
  expect(liste[1]?.govde['tahsilatAnahtar']).toBe(ANAHTAR_A);
  expect(liste[1]?.govde['tutar']).toBe('321.09');
  expect(liste[1]?.xsrf).toBe('yeni');
});

test('otomatik tazeleme: form açık ve yazılmışken 130 sn → tazeleme yok, tutar korunur', async ({
  page,
}) => {
  await page.clock.install({ time: new Date('2026-09-22T09:00:00+03:00') });
  const sayac = await sahte(page, [A]);
  await tahsilatYakala(page, () => 'ok');
  await page.goto(PANEL);
  await tahsilDugmesi(page, '34 AAA 01').click();
  await form(page).getByLabel('Tutar').fill('777,77');
  await page.clock.fastForward(130_000);
  expect(sayac.ozet).toBe(1);
  await expect(form(page).getByLabel('Tutar')).toHaveValue('777,77');

  // Form kapanınca ertelenen tazeleme gelir (bileşen zamanlayıcısı; meta-refresh yok).
  await form(page).getByRole('button', { name: 'Vazgeç' }).click();
  await page.locator('body').click({ position: { x: 1, y: 1 } });
  await page.clock.fastForward(20_000);
  await expect.poll(() => sayac.ozet).toBe(2);
});

test('EUR satırı: döviz EUR gider, kur gönderilmez', async ({ page }) => {
  await sahte(page, [satir('2026200901003', '35 EUR 03', 100, ANAHTAR_A, 'EUR')]);
  const { liste } = await tahsilatYakala(page, () => 'ok');
  await page.goto(PANEL);
  await tahsilDugmesi(page, '35 EUR 03').click();
  await form(page).getByRole('button', { name: 'Tahsil et' }).click();
  await expect.poll(() => liste.length).toBe(1);
  expect(liste[0]?.govde['doviz']).toBe('EUR');
  expect(liste[0]?.govde['tutar']).toBe('100.00');
  expect('kur' in (liste[0]?.govde ?? {})).toBe(false);
});
