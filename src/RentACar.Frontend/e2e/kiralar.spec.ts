import { expect, test, type Page, type Request } from '@playwright/test';

import {
  ciddiIhlaller,
  hatalariTopla,
  kaydet,
  type KayitliIstek,
  oturumAc,
  problem,
} from './ortak';
import { hazirBekle, tasmaOlc, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F4.2 kira listesi (`/app/kiralar`), sahte `/api/ui/v1` ile (harness yalnız statik SPA sunar):
 * sunucu sayfalı liste, dışa aktarma/PDF bağlantıları (Blazor GET uçları — SPA'ya yönlenmez), süzgecin
 * URL'e yazılması ve PARA: "Tahsil Et" DTO anahtarını aynen geri gönderir, istek uçarken kilitli,
 * 409 `mukerrer`'de yeniden gönderim yok + liste yenilenir, `cakisma` paneli/değeri silmez.
 */
const AG_HATASI = [/Failed to load resource: the server responded with a status of 4\d\d/];

/** #257: sunucu `tahsilatAnahtar`'ı yeniden hesaplar; bayat/başka kiranın anahtarı bu detail ile 409 mukerrer. */
const BAYAT_DETAY =
  'Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti ya da tahsilat anahtarı bu ' +
  'kiraya ait değil; kaydı yeniden yükleyip tekrar deneyin.';

const ANAHTAR_1 = 'aaaaaaaa-0000-5000-8000-000000000001';
const ANAHTAR_2 = 'aaaaaaaa-0000-5000-8000-000000000002';

function satir(no: string, ek: Record<string, unknown> = {}) {
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
    ...ek,
  };
}

const KIRA_1 = satir('2026220901001');
const tahsilatli = (anahtar: string, bakiye = 1234.5) => ({
  ...KIRA_1,
  bakiye,
  tahsilat: {
    anahtar,
    cariId: KIRA_1.musteriId,
    rentalId: KIRA_1.id,
    doviz: 'TRY',
    varsayilanTutar: bakiye,
  },
});
const KIRA_2 = satir('2026220901002', { durum: 'Tamamlandi', bakiye: 0, faturali: true });

interface Sahte {
  /** Liste ucunun sıradaki yanıtı (her GET'te çağrılır). */
  satirlar: () => unknown[];
  readonly listeIstekleri: URL[];
}

async function kiraUclari(page: Page, satirlar: () => unknown[]): Promise<Sahte> {
  const sahte: Sahte = { satirlar, listeIstekleri: [] };
  await page.route(
    (url) => url.pathname === '/api/ui/v1/kiralar',
    (route) => {
      sahte.listeIstekleri.push(new URL(route.request().url()));
      const kayitlar = sahte.satirlar();
      return route.fulfill({
        json: { kayitlar, toplam: kayitlar.length, sayfaNo: 1, boyut: 50 },
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
  return sahte;
}

const SAYFA: VitrinSayfasi = {
  ad: 'kiralar',
  yol: '/app/kiralar',
  baslik: 'Kira listesi',
  hazir: async (page) => {
    const izgara = page.getByRole('grid', { name: 'Kira sözleşmeleri' });
    await expect(izgara).not.toHaveAttribute('aria-busy', 'true');
    await expect(izgara.getByRole('link', { name: '2026220901001', exact: true })).toBeVisible();
  },
};

const tahsilDugmesi = (page: Page) => page.getByRole('button', { name: 'Tahsil et 2026220901001' });
const panelGonder = (page: Page) => page.getByRole('button', { name: 'Tahsil et', exact: true });
const panel = (page: Page) => page.getByRole('region', { name: 'Tahsilat — 2026220901001' });

test.beforeEach(async ({ page }) => oturumAc(page));

test('liste: axe iki temada ciddi/kritik 0, konsol hatası yok; bağlantılar Blazor uçlarına, süzgeç URL’e', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  const sahte = await kiraUclari(page, () => [tahsilatli(ANAHTAR_1), KIRA_2]);
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);

  await expect(page.getByText('2 sözleşme · 1 kirada · 1 faturasız')).toBeVisible();
  // Para sağa yaslı, tr biçimi, kiranın dövizi.
  const bakiye = page.getByRole('gridcell', { name: '1.234,50 ₺' });
  await expect(bakiye).toBeVisible();
  expect(await bakiye.evaluate((td) => getComputedStyle(td).textAlign)).toMatch(/^(right|end)$/);

  // Sözleşme no → SPA kira formu rotası; PDF ve dışa aktarma → Blazor GET (tam sayfa/yeni sekme).
  await expect(page.getByRole('link', { name: '2026220901001', exact: true })).toHaveAttribute(
    'href',
    `/app/kiralar/${KIRA_1.id}`,
  );
  const pdf = page.getByRole('link', { name: /PDF 2026220901001 sözleşmesi/ });
  await expect(pdf).toHaveAttribute('href', `/kiralar/${KIRA_1.id}/pdf`);
  await expect(pdf).toHaveAttribute('target', '_blank');
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/kiralar?format=excel',
  );
  await expect(tahsilDugmesi(page)).toHaveCount(1); // yalnız sunucunun tahsilat verdiği satır
  await expect(page.getByRole('button', { name: /Tahsil et 2026220901002/ })).toHaveCount(0);

  expect(await ciddiIhlaller(page), 'açık tema').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect
    .poll(() => page.evaluate(() => getComputedStyle(document.body).backgroundColor))
    .toBe('rgb(20, 19, 16)');
  expect(await ciddiIhlaller(page), 'koyu tema').toEqual([]);

  // Süzgeç: URL'e yazılır, API aynı adla çağrılır, dışa aktarma süzgeci taşır.
  await page.getByRole('searchbox', { name: 'Ara', exact: true }).fill('Yılmaz');
  await page.getByRole('button', { name: 'Filtrele', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/kiralar\?q=Y%C4%B1lmaz$/);
  await expect.poll(() => sahte.listeIstekleri.at(-1)?.searchParams.get('q')).toBe('Yılmaz');
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/kiralar?format=excel&q=Y%C4%B1lmaz',
  );
  expect(await ciddiIhlaller(page), 'filtre açık').toEqual([]);
  expect(hatalar).toEqual([]);
});

test('Tahsil Et (PARA): DTO anahtarı aynen gider, uçarken kilitli, 2xx → liste yeni anahtarla yenilenir', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  let bakiye = 1234.5;
  let anahtar = ANAHTAR_1;
  const sahte = await kiraUclari(page, () => [tahsilatli(anahtar, bakiye), KIRA_2]);
  const gonderilen: KayitliIstek[] = [];
  let birak: () => void = () => undefined;
  const bekleyen = new Promise<void>((r) => (birak = r));
  await page.route('**/api/ui/v1/finans/tahsilat', async (route) => {
    gonderilen.push(kaydet(route.request()));
    await bekleyen;
    bakiye = 234.5;
    anahtar = ANAHTAR_2;
    await route.fulfill({ json: { id: 'f0000000-0000-4000-8000-000000000001' } });
  });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);

  await tahsilDugmesi(page).click();
  await expect(panel(page)).toBeVisible();
  const tutar = page.getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toBeFocused();
  await tutar.fill('1000');
  await panelGonder(page).click();

  // Uçarken: gönder ve satır düğmesi kilitli; ikinci tık yeni istek üretmez.
  await expect(page.getByRole('button', { name: 'Gönderiliyor…' })).toBeDisabled();
  await expect(tahsilDugmesi(page)).toBeDisabled();
  await page.getByRole('button', { name: 'Gönderiliyor…' }).click({ force: true });
  const listeOnce = sahte.listeIstekleri.length;
  birak();

  await expect(page.getByText('2026220901001: 1.000,00 ₺ tahsil edildi.')).toBeVisible();
  await expect(panel(page)).toHaveCount(0);
  expect(gonderilen).toHaveLength(1);
  const govde = JSON.parse(gonderilen[0]?.govde ?? '{}') as Record<string, unknown>;
  expect(govde).toEqual({
    cariId: KIRA_1.musteriId,
    tutar: '1000.00',
    hesap: 'Kasa',
    kiraId: KIRA_1.id,
    doviz: 'TRY',
    hesapId: null,
    kanal: 'Masaüstü',
    aciklama: 'Hızlı tahsilat (liste) — 2026220901001',
    tahsilatAnahtar: ANAHTAR_1,
  });
  expect(gonderilen[0]?.anahtar).toBe(ANAHTAR_1);
  expect(gonderilen[0]?.xsrf).toBe('eski-belirtec');

  // 2xx sonrası liste tazelendi; yeni bakiye ve yeni anahtarla tekrar tahsil edilebilir.
  await expect.poll(() => sahte.listeIstekleri.length).toBeGreaterThan(listeOnce);
  await expect(page.getByRole('gridcell', { name: '234,50 ₺' })).toBeVisible();
  await tahsilDugmesi(page).click();
  await expect(page.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('234,50');
  expect(hatalar).toEqual([]);
});

test('409 mukerrer (bayat anahtar): yeniden gönderim YOK, liste yenilenir + sunucu detayı; 409 cakisma paneli ve değeri silmez', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  const sahte = await kiraUclari(page, () => [tahsilatli(ANAHTAR_1), KIRA_2]);
  const gonderilen: Request[] = [];
  await page.route('**/api/ui/v1/finans/tahsilat', (route) => {
    gonderilen.push(route.request());
    return gonderilen.length === 1
      ? problem(route, 409, 'cakisma', 'Kayıt başka bir işlemle değişti; kontrol edin.')
      : problem(route, 409, 'mukerrer', BAYAT_DETAY);
  });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);

  await tahsilDugmesi(page).click();
  await page.getByRole('textbox', { name: 'Tutar', exact: true }).fill('500,25');

  // cakisma: bant; panel açık, yazılan tutar yerinde, liste yeniden YÜKLENMEZ.
  const listeOnce = sahte.listeIstekleri.length;
  await panelGonder(page).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt başka bir işlemle değişti');
  await expect(panel(page)).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('500,25');
  expect(sahte.listeIstekleri.length).toBe(listeOnce);

  // mukerrer: tek istek, otomatik tekrar yok; panel kapanır, liste yeniden yüklenir, bilgi toast'u.
  await panelGonder(page).click();
  // Sunucunun detail'ı "Kira kaydı değişmiş" uyarısıyla; "Mükerrer işlem" (kaydedildi izlenimi) YOK.
  await expect(page.getByText('Kira kaydı değişmiş')).toBeVisible();
  await expect(page.getByText(BAYAT_DETAY, { exact: false })).toBeVisible();
  await expect(page.getByText('Mükerrer işlem')).toHaveCount(0);
  await expect(panel(page)).toHaveCount(0);
  await expect.poll(() => sahte.listeIstekleri.length).toBeGreaterThan(listeOnce);
  await page.waitForTimeout(300);
  expect(gonderilen).toHaveLength(2);
  expect(gonderilen.map((r) => r.postDataJSON().tahsilatAnahtar)).toEqual([ANAHTAR_1, ANAHTAR_1]);
  expect(hatalar).toEqual([]);
});

test('Tahsil Et tutarı (PARA): odaklı açılışta öneri seçili — doğrudan “90” = 90.00; “1,555” alan hatası, istek yok', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await kiraUclari(page, () => [tahsilatli(ANAHTAR_1, 1250.5), KIRA_2]);
  const gonderilen: KayitliIstek[] = [];
  await page.route('**/api/ui/v1/finans/tahsilat', async (route) => {
    gonderilen.push(kaydet(route.request()));
    await route.fulfill({ json: { id: 'f0000000-0000-4000-8000-000000000001' } });
  });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);

  // Adversarial F3: öneri "1250,50" iken doğrudan yazılan "90" SONUNA eklenip 1250.51 gönderiliyordu.
  await tahsilDugmesi(page).click();
  const tutar = page.getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toBeFocused();
  await page.keyboard.type('90');
  await expect(tutar).toHaveValue('90');
  await panelGonder(page).click();
  await expect.poll(() => gonderilen.length).toBe(1);
  expect(JSON.parse(gonderilen[0]?.govde ?? '{}')).toMatchObject({
    tutar: '90.00',
    tahsilatAnahtar: ANAHTAR_1,
  });

  // 3 ondalık: yuvarlanmaz, alan hatası, istek gitmez.
  await tahsilDugmesi(page).click();
  await tutar.fill('1,555');
  await panelGonder(page).click();
  await expect(page.getByText('En fazla 2 ondalık hane girilebilir.')).toBeVisible();
  await expect(tutar).toHaveAttribute('aria-invalid', 'true');
  await expect(tutar).toHaveValue('1,555');
  await page.waitForTimeout(300);
  expect(gonderilen).toHaveLength(1);
  expect(await ciddiIhlaller(page)).toEqual([]);
  expect(hatalar).toEqual([]);
});

test.describe('mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  test('/app/kiralar: 320/390/768 px gövde yatay taşması yok (tahsilat paneli açıkken dahil)', async ({
    page,
  }) => {
    await kiraUclari(page, () => [tahsilatli(ANAHTAR_1), KIRA_2]);
    for (const genislik of [320, 390, 768]) {
      await page.setViewportSize({ width: genislik, height: 844 });
      await page.goto(SAYFA.yol);
      await hazirBekle(page, SAYFA);
      expect(await tasmaOlc(page), `${genislik}px`).toEqual({ tasma: 0, suclular: [] });
      await tahsilDugmesi(page).click();
      await expect(panel(page)).toBeVisible();
      expect(await tasmaOlc(page), `${genislik}px panel`).toEqual({ tasma: 0, suclular: [] });
    }
  });
});

test('/app/kiralar: 1440 px gövde yatay taşması yok', async ({ page }) => {
  await kiraUclari(page, () => [tahsilatli(ANAHTAR_1), KIRA_2]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);
  expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
});
