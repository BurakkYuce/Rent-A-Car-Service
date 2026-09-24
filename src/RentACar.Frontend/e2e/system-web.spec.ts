import { expect, test, type Page } from '@playwright/test';

import { ciddiIhlaller, hatalariTopla, oturumAc } from './ortak';
import { ADMIN_BEN, record, settingsEndpoints, usersEndpoints, type Write } from './system-fakes';
import { tasmaOlc } from './vitrin-sayfalari';

/**
 * F11.2b web sitesi (ilan sihirbazı 3 adım, site içeriği, blog önizleme), gelen talepler, bildirimler, arama, parola;
 * 320/390/768/1440 taşma.
 */
const body = (w: Write | undefined) => JSON.parse(w?.govde ?? '{}') as Record<string, unknown>;
const LISTING = '44444444-0000-4000-8000-00000000f001';
const REQUEST = '55555555-0000-4000-8000-00000000a001';

const detail = (extra: Record<string, unknown> = {}) => ({
  id: LISTING,
  baslik: 'Fiat Egea 1.4',
  slug: 'fiat-egea',
  durum: 'Taslak',
  gunlukFiyat: 0,
  haftalikToplam: null,
  aylikToplam: null,
  kdvDahil: true,
  kardesTaslakSayisi: 0,
  araclar: [{ id: 'v1', plaka: '34 ABC 01', sube: 'Merkez' }],
  ozellikler: [{ etiket: 'Yakıt', deger: 'Benzin', gorunur: true }],
  fotograflar: [],
  surum: 'l-1',
  ...extra,
});

async function websiteEndpoints(page: Page): Promise<Write[]> {
  const writes: Write[] = [];
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/web-sitesi'),
    (r) => {
      const path = new URL(r.request().url()).pathname;
      if (r.request().method() !== 'GET') {
        writes.push(record(r));
        if (path.endsWith('/ilanlar')) return r.fulfill({ status: 201, json: { id: LISTING } });
        if (path.endsWith('/fiyat'))
          return r.fulfill({
            json: { kardesKopyalanan: 0, ilan: detail({ gunlukFiyat: 1250.5, surum: 'l-2' }) },
          });
        return r.fulfill({ json: { yayinda: false, ilan: detail({ surum: 'l-3' }) } });
      }
      if (path.endsWith('/ozet'))
        return r.fulfill({ json: { ilansizAracSayisi: 2, ilanSayisi: 1, yayindakiIlanSayisi: 0 } });
      if (path.endsWith('/havuz'))
        return r.fulfill({
          json: [
            {
              imza: 'fiat|egea|2024',
              baslik: 'Fiat Egea 1.4',
              yilAralik: '2024',
              araclar: [{ id: 'v1', plaka: '34 ABC 01', sube: 'Merkez' }],
            },
          ],
        });
      if (path.endsWith('/ilanlar'))
        return r.fulfill({
          json: {
            kayitlar: [
              {
                id: LISTING,
                baslik: 'Fiat Egea 1.4',
                durum: 'Taslak',
                yayinda: false,
                aracSayisi: 1,
                adet: 1,
                gunlukFiyat: 0,
                kdvDahil: true,
                eksikler: ['Foto yok', 'Fiyat yok'],
                ozellikBayat: false,
              },
            ],
            toplam: 1,
            sayfaNo: 1,
            boyut: 200,
          },
        });
      return r.fulfill({ json: detail() });
    },
  );
  return writes;
}

test('ilan sihirbazı: havuzdan seç → oluştur → fiyat (surum) → özellikler (surum); axe', async ({
  page,
}) => {
  const errors = hatalariTopla(page);
  await oturumAc(page, ADMIN_BEN);
  const writes = await websiteEndpoints(page);
  await page.goto('/app/web-sitesi');
  await expect(page.getByRole('link', { name: 'Foto yok → ekle' })).toBeVisible();
  await expect(page.getByText('Sitede yayınlanmamış 2 araç var')).toBeVisible();
  expect(await ciddiIhlaller(page), 'ilanlar').toEqual([]);

  await page.getByRole('link', { name: 'Araç ekle' }).click();
  await page.getByRole('checkbox', { name: 'Fiat Egea 1.4' }).check();
  expect(await ciddiIhlaller(page), 'havuz').toEqual([]);
  await page.getByRole('button', { name: 'İleri →' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(body(writes[0])).toEqual({ mod: 'beraber', imzalar: ['fiat|egea|2024'] });

  await expect(page).toHaveURL(new RegExp(`/app/web-sitesi/ilan/${LISTING}/fiyat$`));
  await page.getByRole('textbox', { name: 'Günlük fiyat' }).fill('1.250,50');
  await page.getByRole('button', { name: 'Kaydet ve ileri →' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(body(writes[1])).toEqual({
    gunlukFiyat: '1250.50',
    haftalikToplam: null,
    aylikToplam: null,
    kdvDahil: true,
    surum: 'l-1',
  });

  await expect(page).toHaveURL(new RegExp(`/app/web-sitesi/ilan/${LISTING}/ozellikler$`));
  await expect(page.getByText('Fotoğraf yok — fotoğrafsız ilan sitede görünmez.')).toBeVisible();
  await page.getByRole('textbox', { name: '1. satır değer' }).fill('Benzin (95)');
  await page.getByRole('button', { name: 'Kaydet ve yayınla' }).click();
  await expect.poll(() => writes.length).toBe(3);
  expect(body(writes[2])).toEqual({
    satirlar: [{ etiket: 'Yakıt', deger: 'Benzin (95)', gorunur: true }],
    surum: 'l-1',
  });
  await expect(page.getByText('fotoğraf olmadığı için ilan taslakta kaldı')).toBeVisible();
  expect(errors).toEqual([]);
});

test('web sitesi modülü yoksa ilan ekranı yalnız bilgi verir, uç çağrılmaz', async ({ page }) => {
  await oturumAc(page, { ...ADMIN_BEN, moduller: { webSitesi: false } });
  const writes = await websiteEndpoints(page);
  let calls = 0;
  page.on('request', (r) => {
    if (r.url().includes('/api/ui/v1/web-sitesi')) calls++;
  });
  await page.goto('/app/web-sitesi');
  await expect(page.getByText('Web sitesi modülü bu firmada açık değil.')).toBeVisible();
  expect(calls).toBe(0);
  expect(writes).toHaveLength(0);
});

test('site içeriği: sürümsüz liste satırı tekil okunur, çok satırlı gövdeyle PUT surum taşır', async ({
  page,
}) => {
  const errors = hatalariTopla(page);
  await oturumAc(page, ADMIN_BEN);
  const writes: Write[] = [];
  const pageRow = { id: 'p1', slug: 'hakkimizda', baslik: 'Hakkımızda', sira: 1, yayinda: true };
  const pageDetail = {
    ...pageRow,
    govde: 'Paragraf 1\n\nParagraf 2',
    metaAciklama: null,
    surum: 'c-1',
  };
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/site-icerik'),
    (r) => {
      const path = new URL(r.request().url()).pathname;
      if (r.request().method() !== 'GET') {
        writes.push(record(r));
        return r.fulfill({ json: pageDetail });
      }
      if (path.endsWith('/sss')) return r.fulfill({ json: [] });
      if (path.endsWith('/sayfalar'))
        return r.fulfill({ json: { kayitlar: [pageRow], toplam: 1, sayfaNo: 1, boyut: 200 } });
      return r.fulfill({ json: pageDetail });
    },
  );
  await page.goto('/app/site-icerik');
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const content = page.getByRole('textbox', { name: 'İçerik' });
  await expect(content).toHaveValue('Paragraf 1\n\nParagraf 2');
  expect(await ciddiIhlaller(page)).toEqual([]);
  await content.fill('## Biz kimiz\n\nYeni paragraf');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0]?.path).toBe('/api/ui/v1/site-icerik/sayfalar/p1');
  expect(body(writes[0])).toEqual({
    baslik: 'Hakkımızda',
    slug: 'hakkimizda',
    sira: 1,
    yayinda: true,
    metaAciklama: null,
    govde: '## Biz kimiz\n\nYeni paragraf',
    surum: 'c-1',
  });
  expect(errors).toEqual([]);
});

test('blog önizleme: içerik düz metin — <script> işaretleme olarak yorumlanmaz', async ({
  page,
}) => {
  const errors = hatalariTopla(page);
  await oturumAc(page, ADMIN_BEN);
  await page.route('**/api/ui/v1/blog-yonetim/*/onizleme', (r) =>
    r.fulfill({
      json: {
        id: 'p1',
        durum: 'Taslak',
        adres: '/blog/kis-lastigi',
        baslik: 'Kış lastiği rehberi',
        altBaslik: null,
        aramaBasligi: 'Kış lastiği rehberi',
        aramaAciklamasi: 'Özet',
        anahtarKelimeler: ['lastik'],
        aramaDisi: false,
        bloklar: [
          { tur: 'Baslik2', metin: 'Ne zaman takılır?' },
          { tur: 'Paragraf', metin: '<script>window.__xss = 1</script> Aralık ayında.' },
        ],
      },
    }),
  );
  await page.goto('/app/blog-yonetim/p1/onizleme');
  await expect(page.getByRole('heading', { name: 'Ne zaman takılır?', level: 2 })).toBeVisible();
  await expect(page.getByText('<script>window.__xss = 1</script> Aralık ayında.')).toBeVisible();
  expect(
    await page.evaluate(() => (window as unknown as { __xss?: number }).__xss),
  ).toBeUndefined();
  await expect(page.getByText('Bu yazı taslak')).toBeVisible();
  expect(errors).toEqual([]);
});

test('gelen talepler: ayrıntı → not ekle, aday araçla dönüştür', async ({ page }) => {
  await oturumAc(page, ADMIN_BEN);
  const writes: Write[] = [];
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/gelen-talepler'),
    (r) => {
      const path = new URL(r.request().url()).pathname;
      if (r.request().method() !== 'GET') {
        writes.push(record(r));
        if (path.endsWith('/notlar'))
          return r.fulfill({
            status: 201,
            json: [
              { id: 'n1', metin: 'Arandı', kullanici: 'ayse', zamanUtc: '2026-09-20T10:00:00Z' },
            ],
          });
        return r.fulfill({ json: { rezervasyonId: 'r1' } });
      }
      if (path.endsWith('/notlar')) return r.fulfill({ json: [] });
      if (path.endsWith('/aday-araclar'))
        return r.fulfill({
          json: [
            {
              id: 'v9',
              plaka: '34 XYZ 99',
              marka: 'Fiat',
              tip: 'Egea',
              grup: 'C',
              sube: 'Merkez',
              ilanAraci: true,
            },
          ],
        });
      return r.fulfill({
        json: {
          kayitlar: [
            {
              id: REQUEST,
              adSoyad: 'Ali Veli',
              telefon: '+905321112233',
              email: null,
              ilanId: null,
              ilanBaslik: 'Fiat Egea',
              aracGrupKod: null,
              basTar: '2026-10-01T09:00:00Z',
              bitTar: '2026-10-04T09:00:00Z',
              sube: 'Merkez',
              not: null,
              gosterilenGunlukUcret: 1250,
              gosterilenKdvDahil: true,
              durum: 'Yeni',
              durumEtiket: 'Yeni',
              aktif: true,
              ilerlemeler: ['Iletisimde', 'Kayip'],
              donusenRezervasyonId: null,
              atananKullaniciId: null,
              atananAd: null,
              olusturmaTarihi: '2026-09-20T08:00:00Z',
              notSayisi: 0,
              bekleyenGun: 2,
            },
          ],
          toplam: 1,
          sayfaNo: 1,
          boyut: 25,
          ozet: { yeni: 1, enEskiGun: 2 },
        },
      });
    },
  );
  await page.goto('/app/gelen-talepler');
  await page.getByRole('button', { name: 'Ayrıntı (0 not)' }).click();
  expect(await ciddiIhlaller(page)).toEqual([]);
  await page.getByRole('textbox', { name: 'Yeni not' }).fill('Arandı');
  await page.getByRole('button', { name: 'Not ekle' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(body(writes[0])).toEqual({ metin: 'Arandı' });
  await page.getByRole('combobox', { name: 'Araç' }).selectOption({ index: 1 });
  await page.getByRole('button', { name: 'Dönüştür', exact: true }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1]?.path).toBe(`/api/ui/v1/gelen-talepler/${REQUEST}/donustur`);
  expect(body(writes[1])).toEqual({ aracId: 'v9' });
});

test('bildirimler, arama ve parola: oturumla açılır; parola alanları current/new-password', async ({
  page,
}) => {
  await oturumAc(page, ADMIN_BEN);
  const writes: Write[] = [];
  await page.route('**/api/ui/v1/bildirimler?*', (r) =>
    r.fulfill({
      json: {
        vadeGecmis: 1,
        vadeYakin: 0,
        acikSikayet: 0,
        donemKapanis: null,
        vadeler: [],
        sikayetler: [],
        okunmamis: 1,
        bildirimler: [
          {
            id: 'n1',
            tur: 'Sigorta',
            mesaj: '34 ABC 01 sigorta bitiyor',
            aracId: 'v1',
            vadeTarihiUtc: '2026-10-01T00:00:00Z',
            okundu: false,
            olusturmaUtc: '2026-09-20T08:00:00Z',
          },
        ],
      },
    }),
  );
  await page.route('**/api/ui/v1/bildirimler/*/oku', (r) => {
    writes.push(record(r));
    return r.fulfill({ status: 204 });
  });
  await page.goto('/app/bildirimler');
  await page.getByRole('button', { name: 'Okundu', exact: true }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0]?.path).toBe('/api/ui/v1/bildirimler/n1/oku');

  await page.route('**/api/ui/v1/ara?*', (r) =>
    r.fulfill({ json: [{ tur: 'Kira', baslik: 'RZ-1', alt: null, url: '//kotu.test/x' }] }),
  );
  await page.goto('/app/ara');
  await page.getByRole('searchbox', { name: 'Aranacak metin' }).fill('RZ-1');
  await page.getByRole('button', { name: 'Ara', exact: true }).click();
  await expect(page.getByText('RZ-1')).toBeVisible();
  await expect(page.getByRole('link', { name: 'RZ-1' })).toHaveCount(0);

  await page.goto('/app/profil/sifre-degistir');
  await expect(page.getByLabel('Mevcut parola')).toHaveAttribute(
    'autocomplete',
    'current-password',
  );
  await expect(page.getByLabel('Yeni parola').first()).toHaveAttribute(
    'autocomplete',
    'new-password',
  );
});

const OVERFLOW_PAGES = [
  '/app/ayarlar',
  '/app/kullanicilar',
  '/app/web-sitesi',
  '/app/gelen-talepler',
];

async function overflowFakes(page: Page) {
  await settingsEndpoints(page);
  await usersEndpoints(page);
  await websiteEndpoints(page);
  await page.route('**/api/ui/v1/gelen-talepler?*', (r) =>
    r.fulfill({
      json: { kayitlar: [], toplam: 0, sayfaNo: 1, boyut: 25, ozet: { yeni: 0, enEskiGun: null } },
    }),
  );
}

test.describe('sistem ekranları: mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
  for (const path of OVERFLOW_PAGES) {
    test(`${path}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await oturumAc(page, ADMIN_BEN);
      await overflowFakes(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(path);
        await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
        await page.waitForLoadState('networkidle');
        expect(await tasmaOlc(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  }
});

for (const path of OVERFLOW_PAGES) {
  test(`${path}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await oturumAc(page, ADMIN_BEN);
    await overflowFakes(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await page.waitForLoadState('networkidle');
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
