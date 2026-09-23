import { expect, test, type Page } from '@playwright/test';

import { BEN, ciddiIhlaller, hatalariTopla, oturumAc, problem, xsrfYaz } from './ortak';
import {
  KIRA_ID,
  MUSTERI_ID,
  ARAC_ID,
  REZ_ID,
  TEKLIF_ID,
  rezDetayi,
  rezSatiri,
  sahteRezervasyonApi,
  teklifDetayi,
  teklifSatiri,
} from './rezervasyon-sahte';
import { tasmaOlc } from './vitrin-sayfalari';

/**
 * F5.2a rezervasyon + teklif ekranları (sahte `/api/ui/v1`, üretim derlemesi + CSP). Faz çıkışı senaryoları:
 * "doğrulama hatasında form korunur", "oturum düşünce form kaybolmaz", "`cakisma` formu silmez" + teklif kabul
 * tekrarı (409 → yeniden yükle, oluşan rezervasyon görünür). axe iki temada, taşma 320/390/768/1440.
 */
const AG_HATASI = [/Failed to load resource: the server responded with a status of 4\d\d/];
const YENI = '/app/rezervasyonlar/yeni';
const DETAY = `/app/rezervasyonlar/${REZ_ID}`;

async function listeHazir(page: Page, etiket: string, no: string): Promise<void> {
  const izgara = page.getByRole('grid', { name: etiket });
  await expect(izgara).not.toHaveAttribute('aria-busy', 'true');
  await expect(izgara.getByRole('link', { name: no, exact: true })).toBeVisible();
}

async function sec(page: Page, alan: string, yazi: string, secenek: string): Promise<void> {
  const kutu = page.getByRole('combobox', { name: alan, exact: true });
  await kutu.click();
  await kutu.fill(yazi);
  await page.getByRole('option', { name: secenek }).click();
}

/** Yeni form: müşteri + araç + proje adı + günlük ücret. */
async function doldur(page: Page): Promise<void> {
  await sec(page, 'Müşteri', 'Ay', 'Ayşe Yılmaz');
  await sec(page, 'Araç', '34', '34 ABC 123');
  await page.getByLabel('Günlük ücret').fill('1250,50');
  await page.getByLabel('Proje adı').fill('Fuar');
  await page.getByLabel('Proje adı').blur();
}

/** `rc-alan` etiketiyle başlayan alanın girdisi. */
const girdi = (page: Page, etiket: string) =>
  page
    .locator('rc-alan')
    .filter({ has: page.locator('label', { hasText: new RegExp(`^\\s*${etiket}`) }) })
    .locator('input')
    .first();

async function formKorunduMu(page: Page): Promise<void> {
  await expect(page).toHaveURL(/\/app\/rezervasyonlar\/yeni$/);
  // DOM ile (rol/etiket değil): yeniden giriş diyaloğu açıkken sayfa erişilebilirlik ağacından gizlenir.
  await expect(girdi(page, 'Müşteri')).toHaveValue('Ayşe Yılmaz');
  await expect(girdi(page, 'Araç')).toHaveValue('34 ABC 123');
  await expect(girdi(page, 'Günlük ücret')).toHaveValue(/^1\.?250,50$/);
  await expect(girdi(page, 'Proje adı')).toHaveValue('Fuar');
}

const kaydetDugmesi = (page: Page) => page.getByRole('button', { name: 'Kaydet', exact: true });

test.beforeEach(async ({ page }) => oturumAc(page));

test('rezervasyon listesi: axe iki tema, bağlantılar, dışa aktarma süzgeci Blazor adlarıyla, Onayla', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  let durum = 'Rezerv';
  const sahte = await sahteRezervasyonApi(page, {
    rezSatirlari: () => [rezSatiri('RZ-000042', { durum })],
  });
  await page.route(`**/api/ui/v1/rezervasyonlar/${REZ_ID}/onayla`, (route) => {
    durum = 'Onayli';
    return route.fulfill({ json: rezDetayi({ durum: 'Onayli' }) });
  });
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto('/app/rezervasyonlar');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Rezervasyonlar');
  await listeHazir(page, 'Rezervasyonlar', 'RZ-000042');

  await expect(page.getByRole('link', { name: 'RZ-000042', exact: true })).toHaveAttribute(
    'href',
    DETAY,
  );
  await expect(page.getByRole('gridcell', { name: '3.751,50 ₺' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/rezervasyonlar?format=excel',
  );
  expect(await ciddiIhlaller(page), 'açık tema').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await ciddiIhlaller(page), 'koyu tema').toEqual([]);

  // Süzgeç: URL = API adları; dışa aktarma Blazor export adlarıyla (ara/durum).
  await page.getByRole('button', { name: /Filtreler/ }).click();
  await page.getByRole('searchbox', { name: 'Ara', exact: true }).fill('Yılmaz');
  await page.getByRole('combobox', { name: 'Durum' }).selectOption({ label: 'Rezerv' });
  await page.getByRole('button', { name: 'Filtrele', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/rezervasyonlar\?q=Y%C4%B1lmaz&durum=Rezerv$/);
  await expect.poll(() => sahte.listeIstekleri.at(-1)?.searchParams.get('durum')).toBe('Rezerv');
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/rezervasyonlar?format=excel&ara=Y%C4%B1lmaz&durum=Rezerv',
  );
  expect(await ciddiIhlaller(page), 'filtre açık').toEqual([]);

  // Onayla: istek → bildirim → liste yenilenir, satır Onaylı.
  await page.getByRole('button', { name: 'Onayla RZ-000042' }).click();
  await expect(page.getByText('RZ-000042 onaylandı.')).toBeVisible();
  await expect(page.getByRole('gridcell', { name: 'Onaylı' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Onayla RZ-000042' })).toHaveCount(0);
  expect(hatalar).toEqual([]);
});

test('kiraya çevir: onay sorulur → SPA kira formuna gidilir (F4.3 rota sözleşmesi)', async ({
  page,
}) => {
  await sahteRezervasyonApi(page);
  await page.route(`**/api/ui/v1/kiralar/${KIRA_ID}`, (route) =>
    route.fulfill({ status: 404, json: { status: 404, detail: 'yok' } }),
  );
  await page.goto('/app/rezervasyonlar');
  await listeHazir(page, 'Rezervasyonlar', 'RZ-000042');
  await page.getByRole('button', { name: 'Kiraya çevir RZ-000042' }).click();
  const diyalog = page.getByRole('alertdialog');
  await expect(diyalog).toContainText('kira sözleşmesine çevrilsin mi');
  await diyalog.getByRole('button', { name: 'Kiraya çevir' }).click();
  await expect(page).toHaveURL(new RegExp(`/app/kiralar/${KIRA_ID}$`));
  await expect(page.getByText('RZ-000042 kiraya çevrildi: sözleşme 2026230901001.')).toBeVisible();
});

test('doğrulama hatasında form korunur: alan işaretlenir, gezinme yok', async ({ page }) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await sahteRezervasyonApi(page, {
    yazma: (route) =>
      problem(route, 400, 'dogrulama', 'Günlük ücret negatif olamaz.', {
        errors: { gunlukUcret: ['Günlük ücret negatif olamaz.'] },
      }),
  });
  await page.goto(YENI);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni Rezervasyon');
  await doldur(page);
  await kaydetDugmesi(page).click();

  await expect(page.getByLabel('Günlük ücret')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByText('Günlük ücret negatif olamaz.')).toBeVisible();
  await formKorunduMu(page);
  expect(await ciddiIhlaller(page)).toEqual([]);
  expect(hatalar).toEqual([]);
});

test('oturum düşünce form kaybolmaz: yerinde giriş → AYNI istek tekrarlanır → kayıt açılır', async ({
  page,
}) => {
  const govdeler: (string | null)[] = [];
  await sahteRezervasyonApi(page, {
    yazma: (route, istek) => {
      govdeler.push(istek.postData());
      if (govdeler.length === 1) return problem(route, 401, 'oturum_yok', 'Oturum açık değil.');
      return route.fulfill({ status: 201, json: { id: REZ_ID, no: 'RZ-000042' } });
    },
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await xsrfYaz(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await xsrfYaz(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });

  await page.goto(YENI);
  await doldur(page);
  await kaydetDugmesi(page).click();

  const diyalog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(diyalog).toBeVisible();
  await formKorunduMu(page);
  await diyalog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await diyalog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();

  await expect(page).toHaveURL(new RegExp(`${DETAY}$`));
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Rezervasyon RZ-000042');
  expect(govdeler).toHaveLength(2);
  expect(govdeler[1]).toBe(govdeler[0]);
  expect(JSON.parse(govdeler[0] ?? '{}')).toMatchObject({
    musteriId: MUSTERI_ID,
    vehicleId: ARAC_ID,
    gunlukUcret: '1250.50',
    projeAdi: 'Fuar',
  });
});

test('`cakisma` formu silmez: PUT sürümü taşır, 409 → bant + güncel kayıt birleşir, yeniden kayıt yeni sürümle', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  const putlar: Record<string, unknown>[] = [];
  let sunucu = rezDetayi();
  await sahteRezervasyonApi(page, {
    detay: () => sunucu,
    yazma: (route, istek) => {
      putlar.push(istek.postDataJSON() as Record<string, unknown>);
      if (putlar.length === 1) {
        // Başka oturum bu arada onay kodunu değiştirdi (sürüm 813).
        sunucu = rezDetayi({ surum: '813', onayKodu: 'ONY-2' });
        return problem(route, 409, 'cakisma', 'Rezervasyon başka bir oturumda değişti.');
      }
      sunucu = rezDetayi({ surum: '814', onayKodu: 'ONY-2', projeAdi: 'Kongre' });
      return route.fulfill({ json: sunucu });
    },
  });
  await page.goto(DETAY);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Rezervasyon RZ-000042');
  await expect(page.getByTestId('rez-ozet')).toContainText('3.751,50 ₺');
  await expect(page.getByLabel('Proje adı')).toHaveValue('Fuar');
  expect(await ciddiIhlaller(page)).toEqual([]);

  await page.getByLabel('Proje adı').fill('Kongre');
  await kaydetDugmesi(page).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText(
    'Rezervasyon başka bir oturumda değişti.',
  );
  await expect(page.getByLabel('Proje adı')).toHaveValue('Kongre');
  await expect(page.getByLabel('Onay kodu')).toHaveValue('ONY-2'); // dokunulmamış → güncel
  expect(putlar[0]).toMatchObject({ surum: '812', projeAdi: 'Kongre', gunlukUcret: 1250.5 });
  expect(Object.keys(putlar[0] ?? {})).toHaveLength(33);

  await expect(kaydetDugmesi(page)).toBeEnabled();
  await kaydetDugmesi(page).click();
  await expect(page.getByText('Rezervasyon RZ-000042 kaydedildi.')).toBeVisible();
  expect(putlar[1]).toMatchObject({ surum: '813', projeAdi: 'Kongre', onayKodu: 'ONY-2' });
  expect(hatalar).toEqual([]);
});

test('kiraya çevrilmiş rezervasyon: form salt okunur, eylem yok, kira bağlantısı', async ({
  page,
}) => {
  await sahteRezervasyonApi(page, {
    detay: () =>
      rezDetayi(
        { durum: 'KirayaCevrildi', kiraId: KIRA_ID },
        { duzenle: false, onayla: false, kirayaCevir: false, iptal: false },
      ),
  });
  await page.goto(DETAY);
  await expect(page.getByTestId('rez-durum')).toHaveText('Kiraya çevrildi');
  await expect(page.getByLabel('Proje adı')).toBeDisabled();
  await expect(kaydetDugmesi(page)).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'İptal' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Sözleşmeyi aç' })).toHaveAttribute(
    'href',
    `/app/kiralar/${KIRA_ID}`,
  );
  expect(await ciddiIhlaller(page)).toEqual([]);
});

test('teklif kabul TEKRARI: 409 cakisma → yeniden gönderim yok, teklif yeniden yüklenir, oluşan rezervasyon görünür', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let kabulSayisi = 0;
  let kabulEdildi = false;
  const sahte = await sahteRezervasyonApi(page, {
    teklifDetay: () =>
      kabulEdildi ? teklifDetayi({ durum: 'Kabul', rezervasyonId: REZ_ID }) : teklifDetayi(),
    teklifKabul: (route) => {
      kabulSayisi++;
      // Başka sekmede zaten kabul edildi (ya da ilk yanıt kayboldu): sunucu ikinci rezervasyon AÇMAZ.
      kabulEdildi = true;
      return problem(route, 409, 'cakisma', 'Teklif zaten kabul edilmiş.');
    },
  });
  await page.goto(`/app/teklifler/${TEKLIF_ID}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Teklif TK-000007');
  await expect(page.getByTestId('teklif-ozet')).toContainText('3.600,00 ₺');
  expect(await ciddiIhlaller(page)).toEqual([]);
  const detayOnce = sahte.teklifDetayIstekleri.length;

  await page.getByRole('button', { name: 'Kabul → Rezervasyon' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Kabul et' }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Teklif zaten kabul edilmiş.');
  await expect(page.getByTestId('teklif-durum')).toHaveText('Kabul');
  await expect(page.getByRole('link', { name: 'Rezervasyonu aç' })).toHaveAttribute('href', DETAY);
  await expect(page.getByRole('button', { name: 'Kabul → Rezervasyon' })).toHaveCount(0);
  expect(sahte.teklifDetayIstekleri.length).toBeGreaterThan(detayOnce);
  await page.waitForTimeout(300);
  expect(kabulSayisi).toBe(1);
  expect(hatalar).toEqual([]);
});

test('teklif listesi + yeni teklif: axe, kabul edilen teklif rezervasyona bağlanır, gövde Blazor alanları', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  const postlar: Record<string, unknown>[] = [];
  await sahteRezervasyonApi(page, {
    teklifSatirlari: () => [
      teklifSatiri('TK-000007'),
      { ...teklifSatiri('TK-000006', { durum: 'Kabul', rezervasyonId: REZ_ID }), id: 'x-6' },
    ],
  });
  await page.route(
    (url) => url.pathname === '/api/ui/v1/teklifler',
    (route, istek) => {
      if (istek.method() !== 'POST') return route.fallback();
      postlar.push(istek.postDataJSON() as Record<string, unknown>);
      return route.fulfill({ status: 201, json: { id: TEKLIF_ID, no: 'TK-000007' } });
    },
  );
  await page.goto('/app/teklifler');
  await listeHazir(page, 'Teklifler', 'TK-000007');
  await expect(page.getByRole('link', { name: 'Kabul', exact: true })).toHaveAttribute(
    'href',
    DETAY,
  );
  await expect(page.getByRole('button', { name: 'Reddet TK-000007' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Reddet TK-000006' })).toHaveCount(0);
  expect(await ciddiIhlaller(page)).toEqual([]);

  await page.getByRole('link', { name: 'Yeni teklif' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni Teklif');
  await expect(
    page.getByRole('combobox', { name: 'Fiyat türü' }).locator('option:checked'),
  ).toHaveText('Otomatik');
  await sec(page, 'Müşteri', 'Ay', 'Ayşe Yılmaz');
  await sec(page, 'Araç', '34', '34 ABC 123');
  expect(await ciddiIhlaller(page)).toEqual([]);
  await page.getByRole('button', { name: 'Teklif oluştur' }).click();
  await expect(page).toHaveURL(new RegExp(`/app/teklifler/${TEKLIF_ID}$`));
  expect(postlar).toHaveLength(1);
  expect(postlar[0]).toMatchObject({
    musteriId: MUSTERI_ID,
    vehicleId: ARAC_ID,
    fiyatTuru: 'Otomatik',
    gunlukUcret: null,
    gecerlilikTarihi: null,
  });
  expect(hatalar).toEqual([]);
});

const SAYFALAR = [
  { yol: '/app/rezervasyonlar', baslik: 'Rezervasyonlar' },
  { yol: YENI, baslik: 'Yeni Rezervasyon' },
  { yol: DETAY, baslik: 'Rezervasyon RZ-000042' },
  { yol: '/app/teklifler', baslik: 'Teklifler' },
  { yol: '/app/teklifler/yeni', baslik: 'Yeni Teklif' },
  { yol: `/app/teklifler/${TEKLIF_ID}`, baslik: 'Teklif TK-000007' },
];

test.describe('mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  test('rezervasyon/teklif sayfaları: 320/390/768 px gövde yatay taşması yok', async ({ page }) => {
    await sahteRezervasyonApi(page);
    for (const genislik of [320, 390, 768]) {
      await page.setViewportSize({ width: genislik, height: 844 });
      for (const s of SAYFALAR) {
        await page.goto(s.yol);
        await expect(page.getByRole('heading', { level: 1 })).toHaveText(s.baslik);
        await page.evaluate(() => document.fonts.ready.then(() => undefined));
        expect(await tasmaOlc(page), `${genislik}px ${s.yol}`).toEqual({ tasma: 0, suclular: [] });
      }
    }
  });
});

test('rezervasyon/teklif sayfaları: 1440 px gövde yatay taşması yok', async ({ page }) => {
  await sahteRezervasyonApi(page);
  await page.setViewportSize({ width: 1440, height: 900 });
  for (const s of SAYFALAR) {
    await page.goto(s.yol);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(s.baslik);
    expect(await tasmaOlc(page), s.yol).toEqual({ tasma: 0, suclular: [] });
  }
});
