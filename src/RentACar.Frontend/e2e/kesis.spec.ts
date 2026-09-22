import { expect, test, type Page, type Route } from '@playwright/test';

import { BEN, oturumAc, problem } from './ortak';

/**
 * F4.6 ilk kesiş (sahte `/api/ui/v1`, üretim derlemesi + CSP). Harness yalnız statik SPA sunar; Blazor
 * sunucusunun 302'si (`IlkKesisMiddleware`, backend `IlkKesisTests` birebir Location'ı kilitler) Playwright
 * ile taklit edilir: Location FRAGMENT'SIZ yazılır — tarayıcı özgün fragment'ı korur (üretimdeki gibi).
 */
const ARAC_ID = '7b3e1a2c-0000-4000-8000-00000000a001';
const MUSTERI_ID = '7b3e1a2c-0000-4000-8000-00000000c001';
const SORGU = `?varac=${ARAC_ID}&vfrom=2026-10-01&vto=2026-10-04&musteriId=${MUSTERI_ID}`;

/** Sunucunun pilot kiracıya verdiği 302: yol haritadan, sorgu AYNEN, fragment YOK. */
async function sunucuYonlendirmesi(page: Page, kaynak: string, hedef: string): Promise<void> {
  await page.route(
    (url) => url.pathname === kaynak,
    (route: Route) => {
      const url = new URL(route.request().url());
      return route.fulfill({ status: 302, headers: { Location: hedef + url.search } });
    },
  );
}

async function kiraListesiSahtele(page: Page): Promise<void> {
  await page.route(
    (url) => url.pathname === '/api/ui/v1/kiralar',
    (route) => route.fulfill({ json: { kayitlar: [], toplam: 0, sayfaNo: 1, boyut: 50 } }),
  );
  await page.route(
    (url) => url.pathname === '/api/ui/v1/kiralar/ozet',
    (route) => route.fulfill({ json: { toplam: 0, kirada: 0, faturasiz: 0 } }),
  );
  await page.route('**/api/ui/v1/kiralar/filtre-secenekleri', (route) =>
    route.fulfill({ json: { sahipler: [], gruplar: [] } }),
  );
  await page.route('**/api/ui/v1/tablo-duzenleri/**', (route) =>
    route.fulfill({ json: { tabloKodu: 'kiralar.liste', duzen: null, guncellemeUtc: null } }),
  );
}

async function girisFormunuGonder(page: Page): Promise<void> {
  await page.getByLabel('Firma kodu').fill('pilot');
  await page.getByLabel('Kullanıcı adı').fill('ayse');
  await page.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await page.getByRole('button', { name: 'Giriş yap' }).click();
}

async function girisUclari(page: Page, ben: object): Promise<void> {
  let oturumVar = false;
  await page.route('**/api/ui/v1/oturum/ben', (route) =>
    oturumVar ? route.fulfill({ json: ben }) : problem(route, 401, 'oturum_yok', 'Oturum yok.'),
  );
  await page.route('**/api/ui/v1/oturum/xsrf', (route) => route.fulfill({ status: 204 }));
  await page.route('**/api/ui/v1/oturum/giris', (route) => {
    oturumVar = true;
    return route.fulfill({ json: ben });
  });
}

test('eski kira listesi adresi: 302 sonrası sorgu AYNEN, #sekme= tarayıcıda korunur', async ({
  page,
}) => {
  await oturumAc(page);
  await kiraListesiSahtele(page);
  await sunucuYonlendirmesi(page, '/kiralar', '/app/kiralar');

  await page.goto(`/kiralar${SORGU}#sekme=odeme`);
  await expect(page).toHaveURL(
    (url) => url.pathname === '/app/kiralar' && url.search === SORGU && url.hash === '#sekme=odeme',
  );
  await expect(page.getByRole('heading', { level: 1, name: 'Kira Sözleşmeleri' })).toBeVisible();
});

test('pilot girişi: dönüş yoksa SPA Panel’e iner', async ({ page }) => {
  await girisUclari(page, BEN);
  await page.route('**/api/ui/v1/menu', (route) =>
    route.fulfill({ json: { ogeler: [], rozetler: {} } }),
  );
  await page.route('**/api/ui/v1/panel/ozet', (route) =>
    problem(route, 403, 'yetki_yok', 'Bu işlem için yetkiniz yok.'),
  );
  await page.goto('/app/giris');
  await girisFormunuGonder(page);
  await expect(page).toHaveURL(/\/app\/panel$/);
});

test('pilot OLMAYAN firma girişi: Blazor Panel’e tam sayfa geçer (yeni arayüzde kalmaz)', async ({
  page,
}) => {
  await girisUclari(page, { ...BEN, pilot: false });
  await page.goto('/app/giris?returnUrl=%2Fapp%2Fkiralar');
  await girisFormunuGonder(page);
  await expect(page).toHaveURL(/^http:\/\/127\.0\.0\.1:\d+\/$/);
});

test('Blazor dönüş adresi sunucunun /login kapısına verilir (açık yönlendirme çiti sunucuda)', async ({
  page,
}) => {
  await girisUclari(page, BEN);
  await page.goto(`/app/giris?returnUrl=${encodeURIComponent('/vehicles?x=1')}`);
  await girisFormunuGonder(page);
  await expect(page).toHaveURL(/\/login\?ReturnUrl=%2Fvehicles%3Fx%3D1$/);
});

// F4.3 (#261: `/app/kiralar/yeni` formu) main'e girince fixme kaldırılır; sahte uçlar F4.3'ün
// `kira-formu.spec.ts` kurulumundan (form varsayılanları, araç/müşteri seçimi, canlı hesap) alınır.
test.fixme('pilot: Blazor "Kirala" bağlantısı (/kiralar/yeni?varac=…&vfrom=…&vto=…&musteriId=…) SPA formunu DOLU açar', async ({
  page,
}) => {
  await oturumAc(page);
  await sunucuYonlendirmesi(page, '/kiralar/yeni', '/app/kiralar/yeni');

  await page.goto(`/kiralar/yeni${SORGU}#sekme=arac`);
  await expect(page).toHaveURL(/\/app\/kiralar\/yeni\?varac=.*#sekme=arac$/);
  const panel = page.getByRole('tabpanel', { name: 'Araç' });
  await expect(panel.getByLabel('Araç', { exact: true })).not.toHaveValue('');
  await expect(page.getByLabel('Başlangıç', { exact: true })).toHaveValue('01.10.2026');
  await expect(page.getByLabel('Bitiş (beklenen)', { exact: true })).toHaveValue('04.10.2026');
});
