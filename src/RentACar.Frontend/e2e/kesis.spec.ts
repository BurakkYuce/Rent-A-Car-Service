import { expect, test, type Page, type Route } from '@playwright/test';

import {
  hizli,
  ARAC_ID as KIRA_ARAC,
  KIRA_ID,
  MUSTERI_ID as KIRA_MUSTERI,
  sahteKiraApi,
} from './kira-sahte';
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

test('pilot: Blazor "Kirala" bağlantısı (/kiralar/yeni?varac=…&vfrom=…&vto=…&musteriId=…) SPA formunu DOLU açar', async ({
  page,
}) => {
  await oturumAc(page);
  await sahteKiraApi(page);
  await sunucuYonlendirmesi(page, '/kiralar/yeni', '/app/kiralar/yeni');
  // Blazor müsaitlik/araç durumu "Kirala" bağlantısının biçimi (kira sorgu sözleşmesi).
  const sorgu = `?varac=${KIRA_ARAC}&vfrom=2026-10-01&vto=2026-10-04&musteriId=${KIRA_MUSTERI}`;

  await page.goto(`/kiralar/yeni${sorgu}#sekme=fiyat`);
  await expect(page).toHaveURL(
    (url) =>
      url.pathname === '/app/kiralar/yeni' && url.search === sorgu && url.hash === '#sekme=fiyat',
  );
  // #sekme= fragment'ı 302'den sonra da çalışır: Fiyat sekmesi açık (Hızlı Giriş paneli gizli ama DOLU).
  await expect(page.getByRole('tab', { name: 'Fiyat/Toplam' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  const panel = hizli(page);
  await expect(panel.getByLabel('Araç', { exact: true })).toHaveValue('34 ABC 123 — Fiat Egea');
  await expect(panel.getByLabel('Müşteri', { exact: true })).toHaveValue('Ayşe Yılmaz');
  await expect(panel.getByLabel('Başlangıç', { exact: true })).toHaveValue('01.10.2026');
  await expect(panel.getByLabel('Bitiş (beklenen)', { exact: true })).toHaveValue('04.10.2026');
});

test('pilot: eski yazdırma adresi (/kiralar/{id}/yazdir) → /app yazdırma rotası → sunucunun PDF ucu (döngü yok)', async ({
  page,
}) => {
  await oturumAc(page);
  await sunucuYonlendirmesi(page, `/kiralar/${KIRA_ID}/yazdir`, `/app/kiralar/${KIRA_ID}/yazdir`);
  const pdfIstekleri: string[] = [];
  await page.route(`**/kiralar/${KIRA_ID}/pdf`, (route) => {
    pdfIstekleri.push(route.request().url());
    return route.fulfill({ contentType: 'text/plain', body: 'PDF' });
  });

  await page.goto(`/kiralar/${KIRA_ID}/yazdir`);
  await expect(page).toHaveURL((url) => url.pathname === `/kiralar/${KIRA_ID}/pdf`);
  // PDF ucu haritada yok: sunucu yönlendirmez, SPA'ya dönülmez; tek istek.
  expect(pdfIstekleri).toHaveLength(1);
});

/** Sabit finans panelinin okuma uçları (yazma yok — bu test gezinmeyi ölçer, para akışı `kira-finans.spec.ts`'te). */
async function finansOkumalariSahtele(page: Page): Promise<void> {
  await page.route(/\/api\/ui\/v1\/finans\//, (route) =>
    route.request().method() === 'GET'
      ? route.fulfill({
          json: [
            {
              id: '0b0e7c1a-5555-4aaa-8bbb-000000000005',
              etiket: 'Kasa · Merkez Kasa (MRK · TRY)',
              kod: 'MRK',
              ad: 'Merkez Kasa',
              tur: 'Kasa',
              doviz: 'TRY',
            },
          ],
        })
      : route.fulfill({ status: 500 }),
  );
  await page.route(
    /\/api\/ui\/v1\/kiralar\/[^/]+\/(faturalar|cezalar|donem-plani|dis-hizmetler)/,
    (route) => {
      const yol = new URL(route.request().url()).pathname;
      if (yol.endsWith('/cezalar'))
        return route.fulfill({ json: { cezalar: [], hgsGecisleri: [] } });
      return route.fulfill({ json: [] });
    },
  );
}

/**
 * SAYFA İÇERİĞİNDEKİ (kabuk menüsü hariç — o sunucunun menü kaydından gelir, e2e'de sahte) bağlantılardan
 * hangileri kesiş haritasındaki Blazor sayfasına, yani sunucu üzerinden SPA'ya geri düşüyor.
 */
async function haritayaDusenBaglantilar(page: Page): Promise<string[]> {
  return page.locator('main a[href]').evaluateAll((ogeler) =>
    ogeler
      .map((o) => new URL((o as HTMLAnchorElement).href, location.href))
      .filter((u) => u.origin === location.origin)
      .map((u) => u.pathname)
      .filter((yol) =>
        /^\/(kiralar(\/(yeni|[0-9a-f-]{36}(\/yazdir)?))?)?$/i.test(yol.replace(/\/$/, '') || '/'),
      ),
  );
}

test('pilot: kira formu SPA içinde açılır — sabit finans paneli çalışır, hiçbir bağlantı Blazor kira sayfasına düşmez', async ({
  page,
}) => {
  await oturumAc(page);
  await sahteKiraApi(page);
  await finansOkumalariSahtele(page); // sonra kaydedilen rota önce eşleşir
  await sunucuYonlendirmesi(page, `/kiralar/${KIRA_ID}`, `/app/kiralar/${KIRA_ID}`);

  await page.goto(`/kiralar/${KIRA_ID}`);
  await expect(page).toHaveURL((url) => url.pathname === `/app/kiralar/${KIRA_ID}`);

  // M1 (güvenlik incelemesi): finans işlemleri SPA'nın kendi panelinde — "Mevcut ekranda aç" yer tutucusu yok.
  const panel = page.getByTestId('finans-paneli');
  await expect(panel).toBeVisible();
  for (const ad of ['Nakit', 'Kart/Havale', 'Faturalar', 'Dönemler', 'Dış hizmet', 'Ceza/HGS']) {
    await panel.getByRole('tab', { name: ad, exact: true }).click();
    await expect(panel.getByRole('tab', { name: ad, exact: true })).toHaveAttribute(
      'aria-selected',
      'true',
    );
  }
  await expect(page.getByText('Mevcut ekranda aç')).toHaveCount(0);

  // Kira listesi bağlantısı SPA rotası (tam sayfa dönüş + sunucu yönlendirmesi yok).
  expect(await haritayaDusenBaglantilar(page)).toEqual([]);
  await page.getByRole('link', { name: 'Kira listesi', exact: true }).click();
  await expect(page).toHaveURL((url) => url.pathname === '/app/kiralar');
});
