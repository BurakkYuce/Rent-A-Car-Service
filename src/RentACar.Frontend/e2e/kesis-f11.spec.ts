import { expect, test, type Page, type Route } from '@playwright/test';

import { BEN, oturumAc, problem } from './ortak';

/**
 * F11.3 tanım/sistem/web sitesi kesişi (sahte `/api/ui/v1`, üretim derlemesi + CSP). Harness yalnız statik SPA
 * sunar; Blazor sunucusunun 302'si (`IlkKesisMiddleware`, backend `IlkKesisTests` birebir Location'ı kilitler)
 * Playwright ile taklit edilir: yol haritadan, sorgu AYNEN, fragment YOK (tarayıcı özgününü korur). F11
 * şablonlarının SPA adı Blazor adıyla aynıdır. Beklenen başlıklar elle yazılmıştır (rota dosyasından okunmaz).
 */
async function serverRedirect(page: Page, source: string, target: string): Promise<void> {
  await page.route(
    (url) => url.pathname === source,
    (route: Route) => {
      const url = new URL(route.request().url());
      return route.fulfill({ status: 302, headers: { Location: target + url.search } });
    },
  );
}

/** Sahtesi olmayan okuma uçları 404 problem döner (Playwright'ta SON kaydedilen önce eşleşir). */
async function remainingEndpoints(page: Page): Promise<void> {
  await page.route('**/api/ui/v1/**', (route) => problem(route, 404, 'bulunamadi', 'Yok.'));
}

const ALL_PERMISSIONS = {
  ...BEN,
  izinler: [...BEN.izinler, 'ManageUsers', 'OperationsDelete'],
  moduller: { webSitesi: true },
};

async function fakes(page: Page): Promise<void> {
  await remainingEndpoints(page);
  await oturumAc(page, ALL_PERMISSIONS);
}

const ID = '7a1c2c8e-0000-4000-8000-0000000000f1';

/** [Blazor yolu (= SPA yolu /app öneksiz), sekme başlığı] — F11 envanterinin 47 sayfası. */
const PAGES: readonly (readonly [string, string])[] = [
  ['/aksesuarlar', 'Aksesuar Tanımları'],
  ['/arac-gruplari', 'Araç Grupları'],
  ['/bankalar', 'Banka Tanımları'],
  ['/belge-sablonlari', 'Belge Şablonları'],
  ['/ceza-turleri', 'Ceza Türleri'],
  ['/departmanlar', 'Departman Tanımları'],
  ['/doluluk-kurallari', 'Doluluk Fiyat Kuralları'],
  ['/dovizler', 'Döviz Tanımları'],
  ['/drop-tanimlari', 'Drop Matris'],
  ['/gider-turleri', 'Gider Türü Tanımları'],
  ['/hesap-kodlari', 'Hesap Kodları'],
  ['/hesaplar', 'Kasa/Banka Hesapları'],
  ['/iptal-sebepleri', 'İptal Sebebi Tanımları'],
  ['/kdv-oranlari', 'KDV Oranları'],
  ['/lokasyonlar', 'Lokasyonlar'],
  ['/markalar', 'Marka Tanımları'],
  ['/musteri-gruplari', 'Müşteri Grubu Tanımları'],
  ['/odeme-tipleri', 'Ödeme Tipleri'],
  ['/ozel-kodlar', 'Özel Kod Tanımları'],
  ['/personel', 'Personel'],
  ['/renkler', 'Renkler'],
  ['/rezervasyon-kaynaklari', 'Rezervasyon Kaynakları'],
  ['/sigorta-sirketleri', 'Sigorta Şirketleri'],
  ['/subeler', 'Şube Tanımları'],
  ['/ulkeler', 'Ülke Tanımları'],
  ['/vites-turleri', 'Vites Türleri'],
  ['/yakit-turleri', 'Yakıt Türleri'],
  ['/dokumanlar', 'Dokümanlar'],
  ['/firma-belgeleri', 'Firma Belgeleri'],
  ['/takvim-abonelik', 'Takvim Aboneliği'],
  ['/ice-aktar', 'Veri İçe Aktar'],
  ['/kullanicilar', 'Kullanıcılar'],
  ['/yetki', 'Ekran Yetkileri'],
  ['/ayarlar', 'Ayarlar'],
  ['/mesaj-sablonlari', 'Mesaj Şablonları'],
  ['/denetim', 'Denetim'],
  ['/bildirimler', 'Bildirimler'],
  ['/ara', 'Arama'],
  ['/profil/sifre-degistir', 'Parola Değiştir'],
  ['/web-sitesi', 'Web Sitesi'],
  ['/web-sitesi/arac-ekle', 'İlan: Araç Ekle'],
  [`/web-sitesi/ilan/${ID}/fiyat`, 'İlan Fiyatı'],
  [`/web-sitesi/ilan/${ID}/ozellikler`, 'İlan Özellikleri'],
  ['/site-icerik', 'Site İçeriği'],
  ['/blog-yonetim', 'Blog'],
  [`/blog-yonetim/${ID}/onizleme`, 'Blog Önizleme'],
  ['/gelen-talepler', 'Gelen Talepler'],
];

test('envanter: 47 F11 sayfası', () => {
  expect(PAGES).toHaveLength(47);
});

for (const [blazor, title] of PAGES) {
  test(`pilot: eski ${blazor} adresi → /app${blazor} (sorgu AYNEN, fragment korunur, SPA sayfası açılır)`, async ({
    page,
  }) => {
    const spa = `/app${blazor}`;
    await fakes(page);
    await serverRedirect(page, blazor, spa);

    await page.goto(`${blazor}?bilgi=x#iz`);
    await expect(page).toHaveURL(
      (url) => url.pathname === spa && url.search === '?bilgi=x' && url.hash === '#iz',
    );
    await expect(page).toHaveTitle(`${title} — RentACar`);
  });
}

/** Blazor F11 sayfa yolları (SPA'nın `/app/...` bağlantıları buna uymaz; uyan bağlantı eski arayüze düşer). */
const BLAZOR_F11 = new RegExp(
  `^(${PAGES.map(([p]) => p.replace(ID, '[0-9a-f-]{36}')).join('|')})$`,
  'i',
);

async function fallenLinks(page: Page, scope: string): Promise<string[]> {
  return page.locator(`${scope} a[href]`).evaluateAll(
    (links, pattern) =>
      links
        .map((a) => new URL((a as HTMLAnchorElement).href, location.href))
        .filter((u) => u.origin === location.origin)
        .map((u) => u.pathname.replace(/\/$/, ''))
        .filter((path) => new RegExp(pattern, 'i').test(path)),
    BLAZOR_F11.source,
  );
}

test('pilot: F11 ekranlarının sayfa içeriğindeki hiçbir bağlantı Blazor tanım/sistem/web sayfasına düşmez', async ({
  page,
}) => {
  test.setTimeout(120_000);
  await fakes(page);

  for (const [path, title] of PAGES) {
    await page.goto(`/app${path}`);
    await expect(page).toHaveTitle(`${title} — RentACar`);
    expect(await fallenLinks(page, 'main'), path).toEqual([]);
  }
});

/** Tam sayfa yüklemesi olursa pencere nesnesi yenilenir ve işaret kaybolur. */
async function markWindow(page: Page): Promise<void> {
  await page.evaluate(() => {
    (window as unknown as { rcKesisIzi?: number }).rcKesisIzi = 1;
  });
}

async function windowMarked(page: Page): Promise<boolean> {
  return page.evaluate(() => (window as unknown as { rcKesisIzi?: number }).rcKesisIzi === 1);
}

test('arama: eski arama kutusunun ?q= sorgusu bir kez aranır, adresten silinir; sonuçlar router ile gider', async ({
  page,
}) => {
  await fakes(page);
  const queries: (string | null)[] = [];
  const VEHICLE = '7a1c2c8e-0000-4000-8000-0000000000a1';
  const CUSTOMER = '7a1c2c8e-0000-4000-8000-0000000000c1';
  await page.route(
    (u) => u.pathname === '/api/ui/v1/ara',
    (route) => {
      queries.push(new URL(route.request().url()).searchParams.get('q'));
      return route.fulfill({
        json: [
          { tur: 'Araç', baslik: '34 ABC 123', alt: 'Fiat', url: `/araclar/${VEHICLE}` },
          { tur: 'Cari', baslik: 'Ayşe Yılmaz', alt: null, url: `/cariler/${CUSTOMER}` },
          { tur: 'Fatura', baslik: 'RNT2026000000001', alt: null, url: '/faturalar' },
        ],
      });
    },
  );
  await serverRedirect(page, '/ara', '/app/ara');

  await page.goto('/ara?q=Y%C4%B1lmaz');
  await expect(page.getByRole('link', { name: '34 ABC 123' })).toHaveAttribute(
    'href',
    `/app/araclar/${VEHICLE}/detay`,
  );
  await expect(page).toHaveURL((url) => url.pathname === '/app/ara' && url.search === '');
  expect(queries).toEqual(['Yılmaz']);
  await expect(page.getByRole('link', { name: 'Ayşe Yılmaz' })).toHaveAttribute(
    'href',
    `/app/cariler/${CUSTOMER}`,
  );
  await expect(page.getByRole('link', { name: 'RNT2026000000001' })).toHaveAttribute(
    'href',
    '/app/faturalar',
  );

  await markWindow(page);
  await page.getByRole('link', { name: '34 ABC 123' }).click();
  await expect(page).toHaveURL((url) => url.pathname === `/app/araclar/${VEHICLE}/detay`);
  expect(await windowMarked(page)).toBe(true);
});

test('gelen talepler: eski ?durum=0 (Yeni) süzgeci korunur ve uca durum adıyla gider', async ({
  page,
}) => {
  await fakes(page);
  const statuses: (string | null)[] = [];
  await page.route(
    (u) => u.pathname === '/api/ui/v1/gelen-talepler',
    (route) => {
      statuses.push(new URL(route.request().url()).searchParams.get('durum'));
      return route.fulfill({
        json: {
          kayitlar: [],
          toplam: 0,
          sayfaNo: 1,
          boyut: 25,
          ozet: { yeni: 0, enEskiGun: null },
        },
      });
    },
  );
  await serverRedirect(page, '/gelen-talepler', '/app/gelen-talepler');

  await page.goto('/gelen-talepler?durum=0');
  await expect(page).toHaveTitle('Gelen Talepler — RentACar');
  await expect.poll(() => statuses).toEqual(['Yeni']);
  await expect(page.getByRole('combobox', { name: 'Durum' }).locator('option:checked')).toHaveText(
    'Yeni',
  );
});
