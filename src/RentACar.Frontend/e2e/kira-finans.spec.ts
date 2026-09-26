import { expect, test, type Locator, type Page, type Request, type Route } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, logIn, problem, writeXsrf } from './ortak';
import { measureOverflow } from './vitrin-sayfalari';

/**
 * F4.4 kira formu II — sabit panel finans işlemleri (sahte `/api/ui/v1`, üretim derlemesi + CSP).
 * Para kuralları (docs/api/idempotency-envanteri.md "SPA sözleşmesi"): deterministik tahsilat anahtarı
 * DETAYDAN ve başlıksız; ikinci meşru tahsilat tazelenen detayın anahtarıyla; çift tık tek istek;
 * `mukerrer`de otomatik tekrar yok; oturum düşünce AYNI istek (anahtar + gövde) tekrarlanır; dönem
 * kes+tahsil tekrarı sessiz ama `bilgi` gizlenmez; iptal dar izin bandı; 390 px + koyu tema axe 0.
 * Beklenen değerler sahte yanıtlardan ELLE kurulur (formül yok).
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];

const RENTAL_ID = '0b0e7c1a-1111-4aaa-8bbb-000000000001';
const CUSTOMER_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
const VEHICLE_ID = '0b0e7c1a-3333-4aaa-8bbb-000000000003';
const K1 = 'aaaaaaaa-0000-4000-8000-000000000001';
const K2 = 'aaaaaaaa-0000-4000-8000-000000000002';
const PAGE = `/app/kiralar/${RENTAL_ID}`;
const ME_REVERSE = { ...BEN, izinler: [...BEN.izinler, 'FinanceReverse'] };

/** Kira sözleşmesi (gösterim alanları; formül yok): 3600 genel toplam, 1000 tahsil, 2600 kalan. */
function kira(balance: number, collection: number, version = 'v1'): Record<string, unknown> {
  const fields = [
    'reservationId',
    'cikisSubeId',
    'donusKm',
    'donusYakit',
    'gercekDonusTar',
    'kmHediye',
    'bitisSebebi',
    'teslimAlanPersonelId',
    'teslimEdenPersonelId',
    'ikinciSurucuId',
    'ikinciSurucuSerbestAd',
    'ikinciSurucuSerbestSoyad',
    'ikinciSurucuSerbestTel',
    'ikinciSurucuSerbestEhliyetSinifi',
    'hediyeGun',
    'faturalananGun',
    'vadeTar',
    'iskontoTutar',
    'haftaSonuFark',
    'provizyon',
    'depozito',
    'komisyonOran',
    'komisyonTutar',
    'dropUcreti',
    'sonraOdeOran',
    'aciklama',
    'kaynak',
    'kampanyaKodu',
    'uyariAciklama',
    'ozelFaturaAciklama',
    'faturaListesindeGizle',
    'ucusNo',
    'provizyonNo',
    'provizyonTarih',
    'provizyonKapamaTarih',
    'provizyonKapamaTutar',
    'onayKodu',
    'firmaKodu',
    'projeAdi',
    'ozelKod',
    'talepTuru',
    'geldigiBirim',
    'kefilBilgisi',
    'assistFirma',
    'ozelSoforBilgisi',
    'ekKosullar',
    'belgeSablonId',
    'opsiyonNet',
    'opsiyonGun',
    'manuelFindexPuan',
    'kabisCikis',
    'kabisDonus',
    'otomatikUzat',
    'aksYedekAnahtarCikis',
    'aksYedekAnahtarDonus',
    'aksStepneCikis',
    'aksStepneDonus',
    'aksZincirCikis',
    'aksZincirDonus',
    'aksIlkYardimCikis',
    'aksIlkYardimDonus',
    'aksLastikCikis',
    'aksLastikDonus',
    'kiralamaTuru',
    'faturalamaTipi',
    'ozelKdvOran',
    'damgaVergisi',
    'updatedAtUtc',
  ];
  return {
    ...Object.fromEntries(fields.map((a) => [a, null])),
    id: RENTAL_ID,
    sozlesmeNo: '2026220901001',
    durum: 'Kirada',
    musteriId: CUSTOMER_ID,
    vehicleId: VEHICLE_ID,
    basTar: '2026-09-22T06:00:00+00:00',
    bitTar: '2026-09-25T06:00:00+00:00',
    cikisOfisi: 'Merkez',
    donusOfisi: 'Merkez',
    kmLimit: 300,
    fazlaKmUcret: 2.5,
    yakitBirimUcret: 45,
    cikisKm: 12000,
    cikisYakit: 8,
    fazlaKm: 0,
    fazlaKmBedeli: 0,
    eksikYakit: 0,
    yakitBedeli: 0,
    uzatmaGun: 0,
    uzatmaBedeli: 0,
    odemeSekli: 'Nakit',
    gun: 3,
    gunlukUcret: 1200,
    tutar: 3600,
    genelToplam: 3600,
    tahsilat: collection,
    bakiye: balance,
    provizyonDurum: 'Yok',
    riskOnay: false,
    fiyatTuru: 'KDV Dahil Günlük',
    doviz: 'TL',
    kurSnapshot: 1,
    donemselFaturalama: true,
    kdvOranSnapshot: 0.2,
    createdAtUtc: '2026-09-22T06:00:00+00:00',
    surum: version,
  };
}

function detay(key: string, balance: number, collection: number, version = 'v1'): object {
  return {
    kira: kira(balance, collection, version),
    musteri: { id: CUSTOMER_ID, ad: 'Ayşe Yılmaz' },
    ikinciSurucu: null,
    arac: {
      id: VEHICLE_ID,
      plaka: '34 ABC 123',
      marka: 'Fiat',
      tip: 'Egea',
      modelYili: 2024,
      vites: 'Manuel',
      yakit: 'Dizel',
      grup: 'C',
      segment: 'Orta',
      km: 12000,
      sube: 'Merkez',
      konum: 'Otopark',
    },
    islemSubeAdi: 'Merkez Şube',
    teslimAlanPersonelAd: null,
    teslimEdenPersonelAd: null,
    ekHizmetler: [],
    doviz: null,
    paylasim: null,
    yetkiler: { operasyon: true, silme: true, finans: true },
    toplamlar: { ekHizmetToplam: 0, cezaToplam: 0 },
    tahsilat: {
      anahtar: key,
      cariId: CUSTOMER_ID,
      rentalId: RENTAL_ID,
      doviz: 'TRY',
      varsayilanTutar: balance,
    },
  };
}

const PERIOD = {
  donemSira: 2,
  donemBas: '2026-10-01T00:00:00+00:00',
  donemBit: '2026-10-31T00:00:00+00:00',
  durum: 'Planlandi',
  tahakkuk: 3100,
  invoiceId: null,
  kesilenTutar: null,
};

const OUTSOURCED_SERVICE = {
  id: '0b0e7c1a-4444-4aaa-8bbb-000000000004',
  no: 'DH-000001',
  alinanHizmet: 'Çekici',
  hizmetAlinanFirma: 'Yol Yardım',
  hizmetBedeli: 1000,
  currency: 'TRY',
  tedarikciKomisyonOran: 10,
  durum: 'Kayitli',
  tarih: '2026-09-22T06:00:00+00:00',
};

interface Sahte {
  /** Finans yazma uçları (POST `/api/ui/v1/finans/...`) — test karar verir. */
  finans?: (route: Route, request: Request) => Promise<unknown> | unknown;
  /** Detayın o anki hali (her GET'te çağrılır). */
  detay?: () => object;
  /** Kira yazma uçları (PUT `/kiralar/{id}` …) — verilmezse 500. */
  kiraYazma?: (route: Route, request: Request) => Promise<unknown> | unknown;
}

interface Kayit {
  readonly yol: string;
  readonly govde: Record<string, unknown>;
  readonly anahtar: string | undefined;
}

/** Kira + finans + seçim uçları. Dönen `finansIstekleri` gönderilen her para isteğini kaydeder. */
async function fakeApi(
  page: Page,
  { finans: finance, detay: detailFn, kiraYazma: rentalWrite }: Sahte = {},
) {
  const financeRequests: Kayit[] = [];
  let detailRead = 0;
  await page.route(/\/api\/ui\/v1\/secim\//, (route) =>
    route.fulfill({
      json: route.request().url().includes('/secim/kur')
        ? [
            {
              id: 'USD',
              etiket: 'USD — ABD Doları',
              birim: 1,
              dovizAlis: 41.1234,
              dovizSatis: 41.2345,
              tarih: '2026-09-22T00:00:00+00:00',
            },
          ]
        : [],
    }),
  );
  await page.route(/\/api\/ui\/v1\/finans\//, async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'GET') {
      return route.fulfill({
        json: [
          {
            id: '0b0e7c1a-5555-4aaa-8bbb-000000000005',
            etiket: 'Kasa · Merkez Kasa (MRK · TRY)',
            kod: 'MRK',
            ad: 'Merkez Kasa',
            tur: request.url().includes('tur=Banka') ? 'Banka' : 'Kasa',
            doviz: 'TRY',
          },
        ],
      });
    }
    financeRequests.push({
      yol: path,
      govde: (request.postDataJSON() ?? {}) as Record<string, unknown>,
      anahtar: request.headers()['idempotency-key'],
    });
    return (finance ?? ((r) => r.fulfill({ status: 500 })))(route, request);
  });
  await page.route(/\/api\/ui\/v1\/kiralar(\/|\?|$)/, (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.replace('/api/ui/v1/kiralar', '');
    if (request.method() !== 'GET') {
      return (rentalWrite ?? ((r) => r.fulfill({ status: 500 })))(route, request);
    }
    if (path === `/${RENTAL_ID}`) {
      detailRead++;
      return route.fulfill({ json: detailFn?.() ?? detay(K1, 2600, 1000) });
    }
    // F4.3b sekme verileri (bu dosyanın testleri onlara dokunmaz; 404 konsol hatası olmasın).
    if (path === `/${RENTAL_ID}/musteri-ozet`) {
      return route.fulfill({
        json: {
          id: CUSTOMER_ID,
          ad: 'Ayşe Yılmaz',
          tip: 'Bireysel',
          cepTel: null,
          email: null,
          ehliyetNoMaskeli: null,
          pasaportNoMaskeli: null,
          ehliyetSinifi: null,
          ehliyetTarihi: null,
          ehliyetYeri: null,
          ehliyetUlke: null,
          pasaportYeri: null,
          adres: null,
          il: null,
          ilce: null,
          musteriTipi: null,
          riskLimiti: 0,
          karaListe: false,
          uyari: false,
          uyariNedeni: null,
        },
      });
    }
    if (path === '/ek-hizmet-katalogu') return route.fulfill({ json: { ogeler: [], toplam: 0 } });
    if (path === `/${RENTAL_ID}/faturalar`) return route.fulfill({ json: [] });
    if (path === `/${RENTAL_ID}/cezalar`)
      return route.fulfill({ json: { cezalar: [], hgsGecisleri: [] } });
    if (path === `/${RENTAL_ID}/donem-plani`) return route.fulfill({ json: [PERIOD] });
    if (path === `/${RENTAL_ID}/dis-hizmetler`)
      return route.fulfill({ json: [OUTSOURCED_SERVICE] });
    if (path === `/${RENTAL_ID}/karne-ozeti`) {
      return route.fulfill({ json: { vehicleId: VEHICLE_ID, dolulukYuzde: 61.5 } });
    }
    if (path === '/form-varsayilanlari') {
      return route.fulfill({
        json: {
          cikisYakit: 8,
          fiyatTuru: null,
          fiyatTurleri: ['KDV Dahil Günlük'],
          kiralamaTurleri: [],
          faturalamaTipleri: [],
          dovizler: ['TL'],
          odemeSekilleri: ['Nakit'],
          basTarEnGec: '2027-09-22T00:00:00Z',
        },
      });
    }
    if (path.startsWith(`/${RENTAL_ID}/donus-hesapla`)) {
      return route.fulfill({ json: { ok: false, hata: 'Önizleme yok.' } });
    }
    return route.fulfill({ status: 404, json: { status: 404, detail: 'yok' } });
  });
  return { finansIstekleri: financeRequests, detayOkuma: () => detailRead };
}

const panel = (page: Page): Locator => page.getByTestId('finans-paneli');
const toasts = (page: Page): Locator => page.locator('rc-toast-alani');

async function sekme(page: Page, name: string): Promise<void> {
  await panel(page).getByRole('tab', { name: name, exact: true }).click();
}

const collections = (k: readonly Kayit[]) => k.filter((x) => x.yol.endsWith('/finans/tahsilat'));

test('tahsilat: "1.500,50" → 1500.50, anahtar detaydan ve başlıksız; ikinci meşru tahsilat YENİ anahtarla', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await logIn(page);
  let written = false;
  const { finansIstekleri } = await fakeApi(page, {
    detay: () => (written ? detay(K2, 1099.5, 2500.5) : detay(K1, 2600, 1000)),
    finans: (route) => {
      written = true;
      return route.fulfill({ json: { id: 'c1' } });
    },
  });
  await page.goto(PAGE);
  const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00'); // sunucunun varsayılan tutarı (bakiye)
  // Klavyeyle (Tab → tümü seçili) gelip yazmak tutarı DEĞİŞTİRİR, sona EKLEMEZ ("2600,00500" → 2.600,01 değil).
  await panel(page).getByRole('tab', { name: 'Nakit', exact: true }).focus();
  await page.keyboard.press('Tab');
  await expect(amount).toBeFocused();
  await page.keyboard.type('1.500,50');
  await expect(amount).toHaveValue('1.500,50');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect(toasts(page)).toContainText('Tahsilat kaydedildi.');

  // Tazelenen detay: yeni kalan ön-dolu, yeni anahtar.
  await expect(amount).toHaveValue('1.099,50');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect.poll(() => collections(finansIstekleri).length).toBe(2);

  const [a, b] = collections(finansIstekleri);
  expect(a?.govde).toMatchObject({
    cariId: CUSTOMER_ID,
    kiraId: RENTAL_ID,
    tahsilatAnahtar: K1,
    tutar: '1500.50',
    hesap: 'Kasa',
    doviz: 'TRY',
  });
  expect(Number(a?.govde['tutar'])).toBe(1500.5);
  expect('kur' in (a?.govde ?? {})).toBe(false); // TRY'de kur gönderilmez
  expect(a?.anahtar).toBeUndefined(); // deterministik anahtar varken başlık yok
  expect(b?.govde).toMatchObject({ tahsilatAnahtar: K2, tutar: '1099.50' });
  expect(errors).toEqual([]);
});

test('çift tık tek istek (istek sürerken ikinci gönderim yok)', async ({ page }) => {
  await logIn(page);
  const { finansIstekleri } = await fakeApi(page, {
    finans: async (route) => {
      await new Promise((r) => setTimeout(r, 400));
      return route.fulfill({ json: { id: 'c1' } });
    },
  });
  await page.goto(PAGE);
  await expect(panel(page).getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue(
    '2.600,00',
  );
  await panel(page).getByTestId('tahsilat-Kasa').dblclick();
  await expect(toasts(page)).toContainText('Tahsilat kaydedildi.');
  expect(collections(finansIstekleri)).toHaveLength(1);
});

test('409 mukerrer (bayat anahtar): otomatik tekrar YOK, kayıt yeniden yüklenir, "Kayıt değişmiş" + sunucu mesajı', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await logIn(page);
  const message = 'Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti.';
  const { finansIstekleri, detayOkuma } = await fakeApi(page, {
    finans: (route) => problem(route, 409, 'mukerrer', message),
  });
  await page.goto(PAGE);
  const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00');
  await amount.click(); // odak → düzenleme yazımı; sonra tümünü seçip yaz
  await amount.fill('500');
  const once = detayOkuma();
  await panel(page).getByTestId('tahsilat-Kasa').click();

  // Bayat anahtarda HİÇBİR ŞEY yazılmadı: "Mükerrer işlem" değil, nötr uyarı + sunucu mesajı.
  const toast = toasts(page);
  await expect(toast).toContainText('Kira kaydı değişmiş');
  await expect(toast).toContainText(`${message} Kayıt yeniden yüklendi.`);
  await expect(toast).not.toContainText('Mükerrer işlem');
  await expect.poll(detayOkuma).toBeGreaterThan(once);
  // HIGH-1: tutar TEMİZLENİR ve yeniden ön-doldurulmaz — kullanıcı güncel bakiyeye bakıp bilinçli girer.
  await expect(amount).toHaveValue('');
  await page.waitForTimeout(300);
  expect(collections(finansIstekleri)).toHaveLength(1);
  expect(errors).toEqual([]);
});

test('HIGH-1 (R1b): ilk tahsilat yazıldı ama yanıt düştü → AYNI anahtarla tekrar → 409 mevcut → "zaten kaydedildi", form boş, ikinci tahsilat YOK', async ({
  page,
}) => {
  const errors = collectErrors(page, [...NETWORK_ERROR, /ERR_CONNECTION_RESET|net::/]);
  await logIn(page);
  let written = false;
  const detail = 'Bu tahsilat zaten kaydedildi (No T-000042, 500,00 TRY); yeni tahsilat yazılmadı.';
  const { finansIstekleri } = await fakeApi(page, {
    detay: () => (written ? detay(K2, 2100, 1500, 'v2') : detay(K1, 2600, 1000)),
    finans: (route, request) => {
      const g = request.postDataJSON() as Record<string, unknown>;
      if (!written) {
        written = true; // sunucu yazdı…
        return route.abort('connectionreset'); // …ama yanıt istemciye ulaşmadı
      }
      if (g['tahsilatAnahtar'] === K1) {
        return problem(route, 409, 'mukerrer', detail, {
          mevcut: { id: 'c1', belgeNo: 'T-000042', tutar: 500, doviz: 'TRY', ayniIcerik: true },
        });
      }
      return route.fulfill({ json: { id: 'c2' } });
    },
  });
  await page.goto(PAGE);
  const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00');
  await amount.fill('500');
  const button = panel(page).getByTestId('tahsilat-Kasa');
  await button.click();
  await expect(toasts(page)).toContainText('Sunucuya ulaşılamadı');
  await button.click(); // kullanıcı doğru olanı yapar: yeniden dener (AYNI anahtar)

  const toast = toasts(page);
  await expect(toast).toContainText('İşlem zaten kaydedildi');
  await expect(toast).toContainText(detail);
  await expect(toast).not.toContainText('Kira kaydı değişmiş');
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.100,00'); // kayıt yeniden yüklendi
  await expect(amount).toHaveValue(''); // form temiz; yeni tutar önerilmez

  // Kullanıcı tekrar basarsa: boş tutar → istemci doğrulaması, İSTEK YOK.
  await button.click();
  await page.waitForTimeout(300);
  const t = collections(finansIstekleri);
  expect(t).toHaveLength(2);
  expect(t.map((k) => k.govde['tahsilatAnahtar'])).toEqual([K1, K1]);
  expect(t[1]?.govde).toEqual(t[0]?.govde);
  expect(errors).toEqual([]);
});

test('oturum düşünce giden havale: yerinde giriş → AYNI Idempotency-Key + AYNI gövde', async ({
  page,
}) => {
  await logIn(page);
  let n = 0;
  const { finansIstekleri } = await fakeApi(page, {
    finans: (route) =>
      ++n === 1
        ? problem(route, 401, 'oturum_yok', 'Oturum açık değil.')
        : route.fulfill({ json: { id: 'o1' } }),
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await writeXsrf(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await writeXsrf(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });
  await page.goto(PAGE);
  await sekme(page, 'Kart/Havale');
  const payment = panel(page).locator('rc-kf-finans-odeme');
  await payment.getByRole('textbox', { name: 'Tutar', exact: true }).fill('1.250,75');
  await payment.getByTestId('odeme').click();

  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(toasts(page)).toContainText('Giden havale kaydedildi.');

  const [a, b] = finansIstekleri;
  expect(finansIstekleri).toHaveLength(2);
  expect(a?.anahtar).toMatch(/^[0-9a-f-]{36}$/);
  expect(b?.anahtar).toBe(a?.anahtar);
  expect(b?.govde).toEqual(a?.govde);
  expect(a?.govde).toMatchObject({ cariId: CUSTOMER_ID, tutar: '1250.75', hesap: 'Banka' });
  expect('kiraId' in (a?.govde ?? {})).toBe(false);
});

test('dönem kes + tahsil: çift gönderim SESSİZ (aynı fatura), tahsilat yazılmadı bilgisi gizlenmez', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await logIn(page);
  const info = 'Bu dönemin tahsilatı daha önce alınmış; yeni tahsilat yazılmadı.';
  let n = 0;
  const { finansIstekleri } = await fakeApi(page, {
    finans: (route) =>
      route.fulfill({
        json:
          ++n === 1
            ? { faturaId: 'f1', tahsilatYazildi: true, bilgi: null }
            : { faturaId: 'f1', tahsilatYazildi: false, bilgi: info },
      }),
  });
  await page.goto(PAGE);
  await sekme(page, 'Dönemler');
  const p = panel(page);
  await p.getByRole('checkbox', { name: 'Tahsilat 2' }).check();
  await p.getByRole('combobox', { name: 'Hesap türü 2' }).selectOption({ label: 'Banka' });
  await p.getByTestId('donem-kes-2').click();
  await expect(toasts(page)).toContainText('Dönem faturası kesildi, tahsilat yazıldı.');

  // İkinci sekme/tekrar gönderim: sunucu aynı faturayı döner, tahsilat yazılmaz — bilgi görünür.
  await p.getByTestId('donem-kes-2').click();
  await expect(p.getByTestId('donem-bilgi')).toHaveText(info);
  await expect(toasts(page)).toContainText(info);
  expect(finansIstekleri).toHaveLength(2);
  for (const k of finansIstekleri) {
    expect(k.govde).toEqual({ kiraId: RENTAL_ID, donemSira: 2, tahsilat: true, hesap: 'Banka' });
    expect(k.anahtar).toBeUndefined(); // yapısal: başlık yok
  }
  expect(errors).toEqual([]);
});

test('dış hizmet iptali: FinanceReverse yoksa düğme yok; sunucu 403 verirse izin bandı, liste değişmez', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await logIn(page);
  await fakeApi(page);
  await page.goto(PAGE);
  await sekme(page, 'Dış hizmet');
  await expect(panel(page).getByRole('cell', { name: 'DH-000001' })).toBeVisible();
  await expect(panel(page).getByRole('button', { name: /İptal \(ters kayıt\)/ })).toHaveCount(0);

  // Oturumda dar izin var görünür ama sunucu reddeder (ör. kullanıcı bazlı istisna yeni eklendi).
  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await logIn(page, ME_REVERSE);
  const { finansIstekleri } = await fakeApi(page, {
    finans: (route) =>
      problem(route, 403, 'yetki_yok', 'Bu işlem için Finans ters kayıt yetkisi gerekir.'),
  });
  await page.goto(PAGE);
  await sekme(page, 'Dış hizmet');
  await panel(page).getByRole('button', { name: 'İptal (ters kayıt) DH-000001' }).click();
  const approval = page.getByRole('alertdialog', { name: 'DH-000001 iptal edilsin mi?' });
  await approval.getByRole('button', { name: 'İptal et' }).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText(
    'Bu işlem için Finans ters kayıt yetkisi gerekir.',
  );
  expect(finansIstekleri.map((k) => k.yol)).toEqual([
    `/api/ui/v1/finans/dis-hizmet/${OUTSOURCED_SERVICE.id}/iptal`,
  ]);
  await expect(panel(page).getByRole('cell', { name: 'Kayitli' })).toBeVisible();
  expect(errors).toEqual([]);
});

test('panel işlemi sonrası kira sürümü tazelenir: kirli formla Kaydet 409 almaz, yeni surum + yazılan korunur', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await logIn(page);
  let collected = false;
  const puts: Record<string, unknown>[] = [];
  await fakeApi(page, {
    // Tahsilat kiranın Tahsilat/Bakiye'sini ve dolayısıyla sürümünü değiştirir (sunucu: v1 → v2).
    detay: () => (collected ? detay(K2, 1100, 2500, 'v2') : detay(K1, 2600, 1000, 'v1')),
    finans: (route) => {
      collected = true;
      return route.fulfill({ json: { id: 'c1' } });
    },
    kiraYazma: (route, request) => {
      const g = request.postDataJSON() as Record<string, unknown>;
      puts.push(g);
      if (g['surum'] !== (collected ? 'v2' : 'v1')) {
        return problem(
          route,
          409,
          'cakisma',
          'Kira başka bir oturumda değişti; güncel hâli yüklendi.',
        );
      }
      return route.fulfill({ json: kira(1100, 2500, 'v3') });
    },
  });
  await page.goto(`${PAGE}#sekme=ayrintilar`);
  const description = page
    .getByRole('tabpanel', { name: 'Ayrıntılar' })
    .getByRole('textbox', { name: 'Açıklama', exact: true });
  await description.fill('panel işleminden sonra kaydet');

  // Ana form KİRLİYKEN sabit panelde tahsilat → `degisti` → kira detayı (+ surum) tazelenir.
  const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect(toasts(page)).toContainText('Tahsilat kaydedildi.');
  await expect(amount).toHaveValue('1.100,00');
  await expect(page.getByTestId('yan-ozet')).toContainText('1.100,00');
  await expect(description).toHaveValue('panel işleminden sonra kaydet'); // yazılan ezilmedi

  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  expect(puts).toHaveLength(1); // 409 → ikinci deneme YOK: ilk PUT güncel sürümle gitti
  expect(puts[0]).toMatchObject({ surum: 'v2', aciklama: 'panel işleminden sonra kaydet' });
  await expect(page.locator('rc-uyari-bandi')).not.toContainText('başka bir oturumda');
  expect(errors).toEqual([]);
});

/** Metin kutusunda (sağa yaslı) `once` önekinin bittiği x konumu — "imleci buraya koyan" tık için. */
async function textX(box: Locator, all: string, once: string): Promise<number> {
  return box.evaluate(
    (el: HTMLInputElement, [t, o]) => {
      const st = getComputedStyle(el);
      const c = document.createElement('canvas').getContext('2d');
      if (!c) return 0;
      c.font = `${st.fontWeight} ${st.fontSize} ${st.fontFamily}`;
      const sag =
        el.getBoundingClientRect().right -
        parseFloat(st.paddingRight) -
        parseFloat(st.borderRightWidth);
      return sag - c.measureText(t ?? '').width + c.measureText(o ?? '').width;
    },
    [all, once] as const,
  );
}

async function click(page: Page, box: Locator, x: number): Promise<void> {
  const b = await box.boundingBox();
  await page.mouse.click(x, (b?.y ?? 0) + (b?.height ?? 0) / 2);
}

test('L1 + M-B: YAZILMIŞ tutarda fareyle ortaya tık imleci orada bırakır; Tab odağı tümünü seçer', async ({
  page,
}) => {
  await logIn(page);
  await fakeApi(page);
  await page.goto(PAGE);
  const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00');
  await amount.fill('2600'); // kullanıcı yazdı
  await amount.blur();
  await expect(amount).toHaveValue('2.600,00');
  // Düzenleme yazımında "26|00,00": iki rakamdan sonrasına tıkla.
  await click(page, amount, await textX(amount, '2600,00', '26'));
  const selection = await amount.evaluate(
    (el: HTMLInputElement) => el.selectionEnd! - el.selectionStart!,
  );
  expect(selection).toBe(0);
  await page.keyboard.type('9');
  await expect(amount).toHaveValue('26900,00');

  // Klavye odağı: tümü seçili, yazılan yerine geçer.
  await panel(page).getByRole('tab', { name: 'Nakit', exact: true }).focus();
  await page.keyboard.press('Tab');
  await expect(amount).toBeFocused();
  await page.keyboard.type('150');
  await expect(amount).toHaveValue('150');
});

for (const [name, whereAt] of [
  ['Q7a/P1e ortası', 'orta'],
  ['M-B metnin solu', 'sol'],
] as const) {
  test(`M-B (${name}): DOKUNULMAMIŞ ön-dolu tutara fareyle tıklayıp yazmak tutarı DEĞİŞTİRİR (başa/sona eklemez)`, async ({
    page,
  }) => {
    await logIn(page);
    const { finansIstekleri } = await fakeApi(page, {
      detay: () => {
        const d = detay(K1, 2600, 1000) as { kira: Record<string, unknown> };
        return { ...d, kira: { ...d.kira, depozito: 1500 } };
      },
      finans: (r) => r.fulfill({ json: { id: 'c' } }),
    });
    await page.goto(PAGE);
    const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
    await expect(amount).toHaveValue('2.600,00');
    // Koordinatla tıklanır: alan görünür alanda olmalı (sayfa bandıyla yan panel 720 px'in altına iner).
    await amount.scrollIntoViewIfNeeded();
    const b = await amount.boundingBox();
    const x =
      whereAt === 'sol'
        ? (b?.x ?? 0) + 4 // metnin (sağa yaslı) çok solu: imleç 0'a düşerdi
        : await textX(amount, '2.600,00', '2.6');
    await click(page, amount, x);
    await page.keyboard.type('500');
    await expect(amount).toHaveValue('500');
    await panel(page).getByTestId('tahsilat-Kasa').click();
    await expect.poll(() => collections(finansIstekleri).length).toBe(1);
    expect(collections(finansIstekleri)[0]?.govde['tutar']).toBe('500.00');
    // Tahsilat sonrası tazeleme bitsin: "kira yeniden yükleniyor" notu depozito alanını aşağı kaydırır; ölçülen
    // tık konumu yük altında alanı ıskalıyordu (tazeleme bitince düğme yeniden açılır).
    await expect(panel(page).getByTestId('tahsilat-Kasa')).toBeEnabled();

    // Q7b depozito: ön-dolu 1.500,00 → ortasına tık + "1000" → 1000.00 (10001500.00 değil).
    const dep = panel(page)
      .locator('rc-kf-finans-depozito')
      .getByRole('textbox', { name: 'Depozito tutarı' });
    await expect(dep).toHaveValue('1.500,00');
    await click(page, dep, await textX(dep, '1.500,00', '1.5'));
    await page.keyboard.type('1000');
    await expect(dep).toHaveValue('1000');
    await panel(page).getByTestId('depozito-al').click();
    await expect
      .poll(() => finansIstekleri.filter((k) => k.yol.endsWith('/depozito/al')).length)
      .toBe(1);
    expect(finansIstekleri.find((k) => k.yol.endsWith('/depozito/al'))?.govde['tutar']).toBe(
      '1000.00',
    );
  });
}

test('Q1: dokunmatik basış odak üretmeden iptal edilirse sonraki Tab odağı tümünü seçer', async ({
  page,
}) => {
  await logIn(page);
  const { finansIstekleri } = await fakeApi(page, {
    finans: (r) => r.fulfill({ json: { id: 'c' } }),
  });
  await page.goto(PAGE);
  const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00');
  await amount.fill('2600');
  await amount.blur();
  await amount.evaluate((el: HTMLInputElement) => {
    el.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch' }));
    el.dispatchEvent(new PointerEvent('pointercancel', { bubbles: true, pointerType: 'touch' }));
  });
  await panel(page).getByRole('tab', { name: 'Nakit', exact: true }).focus();
  await page.keyboard.press('Tab');
  await expect(amount).toBeFocused();
  await page.keyboard.type('500');
  await expect(amount).toHaveValue('500');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect.poll(() => collections(finansIstekleri).length).toBe(1);
  expect(collections(finansIstekleri)[0]?.govde['tutar']).toBe('500.00');
});

test('M-A (G2/Q5) + L-2: iki sekme aynı anahtar — öteki 100 yazdı → 409 mevcut FARKLI → "YAZILMADI" uyarısı; dokunulmamış ön-dolu tutar YENİ bakiyeyle yenilenir, yeni anahtarla bilinçli gönderim', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await logIn(page);
  let otherWrote = false;
  const detail =
    'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-000099, 100,00 TRY); girdiğiniz 2.600,00 TRY YAZILMADI. Güncel bakiyeyi kontrol edin.';
  const { finansIstekleri } = await fakeApi(page, {
    detay: () => (otherWrote ? detay(K2, 2500, 1100) : detay(K1, 2600, 1000)),
    finans: (route, request) => {
      const g = request.postDataJSON() as Record<string, unknown>;
      if (g['tahsilatAnahtar'] === K1) {
        otherWrote = true; // öteki sekme K1 ile 100 yazmıştı
        return problem(route, 409, 'mukerrer', detail, {
          mevcut: { id: 'a', belgeNo: 'T-000099', tutar: 100, doviz: 'TRY', ayniIcerik: false },
        });
      }
      return route.fulfill({ json: { id: 'c2' } });
    },
  });
  await page.goto(PAGE);
  const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00');
  await panel(page).getByTestId('tahsilat-Kasa').click();

  const toast = toasts(page);
  await expect(toast).toContainText('Başka bir tahsilat yazıldı');
  await expect(toast).toContainText('girdiğiniz 2.600,00 TRY YAZILMADI');
  await expect(toast).not.toContainText('İşlem zaten kaydedildi');
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.500,00'); // yeniden yüklendi
  // L-2: tutar elle yazılmadı (ön-dolu 2.600) → eski bakiye yerine yeni öneri; fazla tahsilat gitmez.
  await expect(amount).toHaveValue('2.500,00');

  await panel(page).getByTestId('tahsilat-Kasa').click(); // bilinçli yeniden gönderim
  await expect(toast).toContainText('Tahsilat kaydedildi.');
  const t = collections(finansIstekleri);
  expect(t.map((k) => k.govde['tahsilatAnahtar'])).toEqual([K1, K2]);
  expect(t[1]?.govde['tutar']).toBe('2500.00');
  expect(errors).toEqual([]);
});

test('M-A + L-2: kullanıcının ELLE yazdığı tutar "YAZILMADI" sonrası yeni bakiyeyle EZİLMEZ', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await logIn(page);
  let otherWrote = false;
  await fakeApi(page, {
    detay: () => (otherWrote ? detay(K2, 2500, 1100) : detay(K1, 2600, 1000)),
    finans: (route) => {
      otherWrote = true;
      return problem(
        route,
        409,
        'mukerrer',
        'başka bir tahsilat yazıldı; girdiğiniz 700,00 TRY YAZILMADI.',
        {
          mevcut: { id: 'a', belgeNo: 'T-000099', tutar: 100, doviz: 'TRY', ayniIcerik: false },
        },
      );
    },
  });
  await page.goto(PAGE);
  const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00');
  await amount.fill('700');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.500,00');
  await expect(amount).toHaveValue(/^700(,00)?$/);
  expect(errors).toEqual([]);
});

test('M-C (4. tur): 500 yazıldı ama yanıt düştü → tutar 600\'e düzeltilip AYNI anahtarla tekrar → 409 mevcut FARKLI → "Önceki denemeniz kaydedilmiş … 600 YAZILMADI", tutar TEMİZLENİR; tekrar basış çift yazım ÜRETMEZ', async ({
  page,
}) => {
  const errors = collectErrors(page, [...NETWORK_ERROR, /ERR_CONNECTION_RESET|net::/]);
  await logIn(page);
  let written = false;
  const { finansIstekleri } = await fakeApi(page, {
    // Sunucu (elle): 1. istek 500 yazdı (kalan 2.600 → 2.100, yeni anahtar K2).
    detay: () => (written ? detay(K2, 2100, 1500, 'v2') : detay(K1, 2600, 1000)),
    finans: (route, request) => {
      const g = request.postDataJSON() as Record<string, unknown>;
      if (!written) {
        written = true; // sunucu 500'ü yazdı…
        return route.abort('connectionreset'); // …ama yanıt istemciye ulaşmadı
      }
      if (g['tahsilatAnahtar'] === K1) {
        return problem(
          route,
          409,
          'mukerrer',
          'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-000042, 500,00 TRY); girdiğiniz 600,00 TRY YAZILMADI. Güncel bakiyeyi kontrol edin.',
          {
            mevcut: { id: 'c1', belgeNo: 'T-000042', tutar: 500, doviz: 'TRY', ayniIcerik: false },
          },
        );
      }
      return route.fulfill({ json: { id: 'c2' } });
    },
  });
  await page.goto(PAGE);
  const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00');
  await amount.fill('500');
  const button = panel(page).getByTestId('tahsilat-Kasa');
  await button.click();
  await expect(toasts(page)).toContainText('Sunucuya ulaşılamadı');
  await amount.fill('600'); // kullanıcı tutarı düzeltir
  await button.click();

  const toast = toasts(page);
  await expect(toast).toContainText('Önceki denemeniz kaydedilmiş — yeni tutar yazılmadı');
  await expect(toast).toContainText('Önceki denemeniz kaydedilmiş (No T-000042, 500,00 ₺)');
  await expect(toast).toContainText('girdiğiniz 600,00 ₺ YAZILMADI');
  await expect(toast).not.toContainText('başka bir tahsilat yazıldı');
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.100,00'); // yeniden yüklendi
  await expect(amount).toHaveValue(''); // TEMİZLENDİ; yeni öneri de basılmaz

  // Kullanıcı tekrar basar: boş tutar → istemci doğrulaması, İSTEK YOK → 1100 yazılamaz.
  await button.click();
  await page.waitForTimeout(300);
  const t = collections(finansIstekleri);
  expect(t).toHaveLength(2);
  expect(t.map((k) => k.govde['tahsilatAnahtar'])).toEqual([K1, K1]);
  expect(t.map((k) => k.govde['tutar'])).toEqual(['500.00', '600.00']);
  expect(errors).toEqual([]);
});

test('5. tur MEDIUM-1: Nakit 500 yazıldı ama yanıt düştü → Kart/Havale\'de AYNI anahtarla 600 → "Önceki denemeniz kaydedilmiş", Kart tutarı TEMİZLENİR; tekrar basış çift yazım ÜRETMEZ', async ({
  page,
}) => {
  const errors = collectErrors(page, [...NETWORK_ERROR, /ERR_CONNECTION_RESET|net::/]);
  await logIn(page);
  let written = false;
  const { finansIstekleri } = await fakeApi(page, {
    // Sunucu (elle): Nakit'in 500'ü yazıldı; anahtar K1 artık bu kayda ait.
    detay: () => (written ? detay(K2, 2100, 1500, 'v2') : detay(K1, 2600, 1000)),
    finans: (route, request) => {
      const g = request.postDataJSON() as Record<string, unknown>;
      if (!written) {
        written = true;
        return route.abort('connectionreset');
      }
      if (g['tahsilatAnahtar'] === K1) {
        return problem(
          route,
          409,
          'mukerrer',
          'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-000042, 500,00 TRY); girdiğiniz 600,00 TRY YAZILMADI. Güncel bakiyeyi kontrol edin.',
          {
            mevcut: { id: 'c1', belgeNo: 'T-000042', tutar: 500, doviz: 'TRY', ayniIcerik: false },
          },
        );
      }
      return route.fulfill({ json: { id: 'c2' } });
    },
  });
  await page.goto(PAGE);
  const cashAmount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(cashAmount).toHaveValue('2.600,00');
  await cashAmount.fill('500');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect(toasts(page)).toContainText('Sunucuya ulaşılamadı');

  await sekme(page, 'Kart/Havale');
  // Sekme geçişi bir sonraki çizimde görünür; `rc-kf-finans-tahsilat` o ana dek hâlâ NAKİT formudur ve `fill`
  // 600'ü Nakit'e yazıyordu (Kart ön-dolu 2.600 gidiyordu). Hesaba özgü test kimliği Kart formunu bekler.
  const card = panel(page).getByTestId('tahsilat-formu-Banka');
  const cardAmount = card.getByRole('textbox', { name: 'Tutar', exact: true });
  await cardAmount.fill('600');
  const cardButton = panel(page).getByTestId('tahsilat-Banka');
  await cardButton.click();

  const toast = toasts(page);
  await expect(toast).toContainText('Önceki denemeniz kaydedilmiş (No T-000042, 500,00 ₺)');
  await expect(toast).toContainText('girdiğiniz 600,00 ₺ YAZILMADI');
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.100,00');
  await expect(cardAmount).toHaveValue('');

  await cardButton.click(); // boş tutar → istemci doğrulaması, İSTEK YOK
  await page.waitForTimeout(300);
  const t = collections(finansIstekleri);
  expect(t.map((k) => [k.govde['tahsilatAnahtar'], k.govde['hesap'], k.govde['tutar']])).toEqual([
    [K1, 'Kasa', '500.00'],
    [K1, 'Banka', '600.00'],
  ]);
  expect(errors).toEqual([]);
});

test("H1 (#316): Nakit yanıtı koptu → hemen Kart/Havale'de 600 → gönderilen tutar EKRANDAKİ 600; Nakit formu dokunulmaz", async ({
  page,
}) => {
  const errors = collectErrors(page, [...NETWORK_ERROR, /ERR_CONNECTION_RESET|net::/]);
  await logIn(page);
  let request = 0;
  let writtenValue = false;
  const { finansIstekleri } = await fakeApi(page, {
    // Sunucu (elle): Nakit'in 500'ü YAZILMADI (bağlantı koptu); Kart'ın 600'ü K1 ile yazılır → kalan 2.000.
    detay: () => (writtenValue ? detay(K2, 2000, 1600, 'v2') : detay(K1, 2600, 1000)),
    finans: (route) => {
      if (++request === 1) return route.abort('connectionreset');
      writtenValue = true;
      return route.fulfill({ json: { id: 'c1' } });
    },
  });
  await page.goto(PAGE);
  const cash = panel(page).getByTestId('tahsilat-formu-Kasa');
  const cashAmount = cash.getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(cashAmount).toHaveValue('2.600,00');
  await cashAmount.fill('500');
  await cash.getByTestId('tahsilat-Kasa').click();
  await expect(toasts(page)).toContainText('Sunucuya ulaşılamadı');

  await sekme(page, 'Kart/Havale');
  const card = panel(page).getByTestId('tahsilat-formu-Banka');
  const cardAmount = card.getByRole('textbox', { name: 'Tutar', exact: true });
  await cardAmount.fill('600');
  await page.waitForTimeout(1000); // arada hiçbir tazeleme/yeniden kurulum yazılanı ezmemeli
  await expect(cardAmount).toHaveValue('600');
  await card.getByTestId('tahsilat-Banka').click();
  await expect(toasts(page)).toContainText('Tahsilat kaydedildi.');
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.000,00');

  // Nakit formu Kart'a yazılandan etkilenmedi: sonucu bilinmeyen 500 donmuş hâliyle duruyor.
  await sekme(page, 'Nakit');
  await expect(
    panel(page)
      .getByTestId('tahsilat-formu-Kasa')
      .getByRole('textbox', { name: 'Tutar', exact: true }),
  ).toHaveValue(/^500(,00)?$/);
  expect(
    collections(finansIstekleri).map((k) => [
      k.govde['tahsilatAnahtar'],
      k.govde['hesap'],
      k.govde['tutar'],
    ]),
  ).toEqual([
    [K1, 'Kasa', '500.00'],
    [K1, 'Banka', '600.00'],
  ]);
  expect(errors).toEqual([]);
});

test('H1 (#316): tahsilat sonrası kira tazelenirken İKİ formda da Tahsil Et pasif; yeni detayla Kart yeni anahtarı kullanır', async ({
  page,
}) => {
  await logIn(page);
  let writtenValue = false;
  const { finansIstekleri } = await fakeApi(page, {
    // Sunucu (elle): Nakit 700 yazılır → kalan 2.600 − 700 = 1.900, yeni anahtar K2.
    detay: () => (writtenValue ? detay(K2, 1900, 1700, 'v2') : detay(K1, 2600, 1000)),
    finans: (route) => {
      writtenValue = true;
      return route.fulfill({ json: { id: 'c1' } });
    },
  });
  // Tahsilattan sonraki detay okuması test bırakana dek bekletilir (sahteApi'den SONRA kaydedilen önce eşleşir).
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route(
    (url) => url.pathname === `/api/ui/v1/kiralar/${RENTAL_ID}`,
    async (route) => {
      if (writtenValue) await gate;
      return route.fallback();
    },
  );
  await page.goto(PAGE);
  const cash = panel(page).getByTestId('tahsilat-formu-Kasa');
  await expect(cash.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('2.600,00');
  await cash.getByRole('textbox', { name: 'Tutar', exact: true }).fill('700');
  await cash.getByTestId('tahsilat-Kasa').click();
  await expect(toasts(page)).toContainText('Tahsilat kaydedildi.');
  await expect(cash.getByTestId('tahsilat-Kasa')).toBeDisabled();

  await sekme(page, 'Kart/Havale');
  const card = panel(page).getByTestId('tahsilat-formu-Banka');
  const cardButton = card.getByTestId('tahsilat-Banka');
  await expect(card).toContainText('kira yeniden yükleniyor');
  await expect(cardButton).toBeDisabled();
  await cardButton.click({ force: true }); // pasif düğme: istek YOK
  await page.waitForTimeout(300);
  expect(collections(finansIstekleri)).toHaveLength(1);

  release();
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('1.900,00');
  await expect(cardButton).toBeEnabled();
  await expect(card.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('1.900,00');
  await cardButton.click();
  await expect(toasts(page)).toContainText('Tahsilat kaydedildi.');
  await expect
    .poll(() =>
      collections(finansIstekleri).map((k) => [
        k.govde['tahsilatAnahtar'],
        k.govde['hesap'],
        k.govde['tutar'],
      ]),
    )
    .toEqual([
      [K1, 'Kasa', '700.00'],
      [K2, 'Banka', '1900.00'],
    ]);
});

const cardForm = (page: Page): Locator => panel(page).getByTestId('tahsilat-formu-Banka');
const cashForm = (page: Page): Locator => panel(page).getByTestId('tahsilat-formu-Kasa');
const amountBox = (l: Locator): Locator => l.getByRole('textbox', { name: 'Tutar', exact: true });

test("#318 T2: Kart'a yazılan 600 + giden havale 2xx (kira tazelenir) → 600 korunur ve aynen gider", async ({
  page,
}) => {
  await logIn(page);
  let payment = false;
  const { finansIstekleri, detayOkuma } = await fakeApi(page, {
    // Giden havale kiraya bağlanmaz: anahtar aynı (K1), yalnız sürüm değişir.
    detay: () => (payment ? detay(K1, 2600, 1000, 'v2') : detay(K1, 2600, 1000)),
    finans: (route) => {
      payment = true;
      return route.fulfill({ json: { id: 'o1' } });
    },
  });
  await page.goto(PAGE);
  await sekme(page, 'Kart/Havale');
  const cardAmount = amountBox(cardForm(page));
  await expect(cardAmount).toHaveValue('2.600,00');
  await cardAmount.fill('600');
  await amountBox(panel(page).locator('rc-kf-finans-odeme')).fill('100');
  const once = detayOkuma();
  await panel(page).getByTestId('odeme').click();
  await expect.poll(() => detayOkuma()).toBeGreaterThan(once);
  await expect(cardAmount).toHaveValue(/^600(,00)?$/);
  await cardForm(page).getByTestId('tahsilat-Banka').click();
  await expect
    .poll(() =>
      collections(finansIstekleri).map((k) => [k.govde['tahsilatAnahtar'], k.govde['tutar']]),
    )
    .toEqual([[K1, '600.00']]);
});

test("#318 T3: Kart'a yazılan 600 + başka rotaya gidip dönüş (sekmeye dönüş tazelemesi) → 600 korunur ve aynen gider", async ({
  page,
}) => {
  await logIn(page);
  const { finansIstekleri, detayOkuma } = await fakeApi(page, {
    detay: () => detay(K1, 2650, 1000, 'v2'),
    finans: (route) => route.fulfill({ json: { id: 'c9' } }),
  });
  await page.goto(PAGE);
  await sekme(page, 'Kart/Havale');
  await amountBox(cardForm(page)).fill('600');
  const once = detayOkuma();
  await page.evaluate(() => {
    history.pushState({}, '', '/app/');
    dispatchEvent(new PopStateEvent('popstate', { state: {} }));
  });
  await expect(page).not.toHaveURL(new RegExp(RENTAL_ID));
  await expect(panel(page)).toBeHidden(); // rota gerçekten değişti (kira sekmesi arka planda yaşar)
  await page.goBack();
  await expect(page).toHaveURL(new RegExp(RENTAL_ID));
  await expect.poll(() => detayOkuma()).toBeGreaterThan(once);
  const card = cardForm(page);
  if (!(await card.count())) await sekme(page, 'Kart/Havale');
  await expect(amountBox(card)).toHaveValue(/^600(,00)?$/);
  await card.getByTestId('tahsilat-Banka').click();
  await expect
    .poll(() => collections(finansIstekleri).map((k) => [k.govde['hesap'], k.govde['tutar']]))
    .toEqual([['Banka', '600.00']]);
});

test('#318 T4/L1: Nakit 2xx sonrası kira tazelemesi 503 → iki formda "Yeniden yükle"; başarılı okumada düğmeler açılır', async ({
  page,
}) => {
  await logIn(page);
  let written = false;
  let corrupt = true;
  const { finansIstekleri } = await fakeApi(page, {
    // Sunucu (elle): Nakit 700 yazılır → kalan 2.600 − 700 = 1.900, yeni anahtar K2.
    detay: () => (written ? detay(K2, 1900, 1700, 'v2') : detay(K1, 2600, 1000)),
    finans: (route) => {
      written = true;
      return route.fulfill({ json: { id: 'c1' } });
    },
  });
  await page.route(
    (url) => url.pathname === `/api/ui/v1/kiralar/${RENTAL_ID}`,
    (route) =>
      written && corrupt
        ? route.fulfill({ status: 503, json: { status: 503, kod: 'sunucu', detail: 'x' } })
        : route.fallback(),
  );
  const errors = collectErrors(page, [/status of 503/]);
  await page.goto(PAGE);
  await amountBox(cashForm(page)).fill('700');
  await cashForm(page).getByTestId('tahsilat-Kasa').click();
  await expect(toasts(page)).toContainText('Tahsilat kaydedildi.');
  await expect(cashForm(page).getByTestId('tahsilat-yeniden-yukle-Kasa')).toBeVisible();
  await expect(cashForm(page)).toContainText('Kira yüklenemedi');
  await expect(cashForm(page).getByTestId('tahsilat-Kasa')).toBeDisabled();

  await sekme(page, 'Kart/Havale');
  const reload = cardForm(page).getByTestId('tahsilat-yeniden-yukle-Banka');
  await expect(reload).toBeVisible();
  await expect(cardForm(page).getByTestId('tahsilat-Banka')).toBeDisabled();

  corrupt = false;
  await reload.click();
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('1.900,00');
  await expect(reload).toHaveCount(0);
  await expect(cardForm(page).getByTestId('tahsilat-Banka')).toBeEnabled();
  await expect(amountBox(cardForm(page))).toHaveValue('1.900,00');
  await cardForm(page).getByTestId('tahsilat-Banka').click();
  await expect
    .poll(() =>
      collections(finansIstekleri).map((k) => [
        k.govde['tahsilatAnahtar'],
        k.govde['hesap'],
        k.govde['tutar'],
      ]),
    )
    .toEqual([
      [K1, 'Kasa', '700.00'],
      [K2, 'Banka', '1900.00'],
    ]);
  expect(errors).toEqual([]);
});

test("#318 T5/L2: Kart'a yazılan 600 + Nakit 700 2xx → Kart YENİ anahtarı alır, 600 korunur; tek basışta 409'suz yazılır", async ({
  page,
}) => {
  await logIn(page);
  let cash = false;
  const { finansIstekleri } = await fakeApi(page, {
    detay: () => (cash ? detay(K2, 1900, 1700, 'v2') : detay(K1, 2600, 1000)),
    finans: (route, request) => {
      const g = request.postDataJSON() as Record<string, unknown>;
      if (g['hesap'] === 'Kasa') cash = true;
      else if (g['tahsilatAnahtar'] === K1)
        return problem(route, 409, 'mukerrer', 'Bayat anahtar.', {
          mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 700, doviz: 'TRY', ayniIcerik: false },
        });
      return route.fulfill({ json: { id: g['hesap'] === 'Kasa' ? 'c1' : 'c2' } });
    },
  });
  await page.goto(PAGE);
  await sekme(page, 'Kart/Havale');
  await amountBox(cardForm(page)).fill('600');
  await sekme(page, 'Nakit');
  await amountBox(cashForm(page)).fill('700');
  await cashForm(page).getByTestId('tahsilat-Kasa').click();
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('1.900,00');
  await sekme(page, 'Kart/Havale');
  await expect(amountBox(cardForm(page))).toHaveValue(/^600(,00)?$/);
  await cardForm(page).getByTestId('tahsilat-Banka').click();
  await expect
    .poll(() =>
      collections(finansIstekleri).map((k) => [
        k.govde['tahsilatAnahtar'],
        k.govde['hesap'],
        k.govde['tutar'],
      ]),
    )
    .toEqual([
      [K1, 'Kasa', '700.00'],
      [K2, 'Banka', '600.00'],
    ]);
  await expect(toasts(page)).not.toContainText('Bayat anahtar');
});

/** Detay GET'i: `durum()` null → normal (sahteApi'ye düşer), sayı → o durumla hata; `gecikme()` ms bekletir. */
async function detailCheck(
  page: Page,
  status: () => number | null,
  delay: () => number = () => 0,
): Promise<void> {
  await page.route(
    (url) => url.pathname === `/api/ui/v1/kiralar/${RENTAL_ID}`,
    async (route) => {
      if (route.request().method() !== 'GET') return route.fallback();
      const s = status(); // istek anındaki mod (gecikme sırasında mod değişse de bu isteğin sonucu sabit)
      const ms = delay();
      if (ms) await new Promise((ok) => setTimeout(ok, ms));
      if (s === null) return route.fallback();
      const code =
        s >= 500
          ? 'sunucu'
          : s === 403
            ? 'yetki_yok'
            : s === 429
              ? 'cok_istek'
              : s === 400
                ? 'dogrulama'
                : undefined;
      return route.fulfill({
        status: s,
        json: { status: s, detail: `hata ${s}`, ...(code ? { kod: code } : {}) },
      });
    },
  );
}

/** Tahsilat istekleri "hesap:anahtar:tutar" (K1/K2 adlarıyla). */
const anahtarlar = (k: readonly Kayit[]) =>
  collections(k).map(
    (x) =>
      `${String(x.govde['hesap'])}:${x.govde['tahsilatAnahtar'] === K1 ? 'K1' : x.govde['tahsilatAnahtar'] === K2 ? 'K2' : '?'}:${String(x.govde['tutar'])}`,
  );

/** Uygulama içi başka rotaya gidip geri döner (kira sekmesi yaşar; dönüşte detay yeniden okunur). */
async function goAndReturn(page: Page): Promise<void> {
  await page.evaluate(() => {
    history.pushState({}, '', '/app/');
    dispatchEvent(new PopStateEvent('popstate', { state: {} }));
  });
  await expect(panel(page)).toBeHidden();
  await page.goBack();
  await expect(page).toHaveURL(new RegExp(RENTAL_ID));
}

for (const cardTyped of [true, false]) {
  test(`#318 B1: Kart sonucu belirsiz (K1) → Nakit sonuçlanır (${cardTyped ? '409' : '2xx'}) → tazeleme → Kart tekrarı YİNE K1 (donmuş anahtar değişmez)`, async ({
    page,
  }) => {
    const errors = collectErrors(page, [...NETWORK_ERROR, /ERR_CONNECTION_RESET|net::/]);
    await logIn(page);
    let n = 0;
    let result = false;
    const { finansIstekleri } = await fakeApi(page, {
      detay: () => (result ? detay(K2, 1900, 1700, 'v2') : detay(K1, 2600, 1000)),
      finans: (route, request) => {
        const g = request.postDataJSON() as Record<string, unknown>;
        if (++n === 1) return route.abort('connectionreset'); // Kart 600 K1: sonuç belirsiz
        if (g['hesap'] === 'Kasa') {
          result = true;
          if (cardTyped)
            return problem(route, 409, 'mukerrer', 'Başka tahsilat yazıldı.', {
              mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 600, doviz: 'TRY', ayniIcerik: false },
            });
          return route.fulfill({ json: { id: 'c2' } });
        }
        return problem(route, 409, 'mukerrer', 'K1 ile kayıt var.', {
          mevcut: {
            id: cardTyped ? 'c1' : 'c2',
            belgeNo: 'T-1',
            tutar: cardTyped ? 600 : 700,
            doviz: 'TRY',
            ayniIcerik: cardTyped,
          },
        });
      },
    });
    await page.goto(PAGE);
    await sekme(page, 'Kart/Havale');
    await amountBox(cardForm(page)).fill('600');
    await cardForm(page).getByTestId('tahsilat-Banka').click();
    await expect(toasts(page)).toContainText('Sunucuya ulaşılamadı');
    await sekme(page, 'Nakit');
    await amountBox(cashForm(page)).fill('700');
    await cashForm(page).getByTestId('tahsilat-Kasa').click();
    await expect(panel(page).getByTestId('finans-kalan')).toContainText('1.900,00');
    await sekme(page, 'Kart/Havale');
    await expect(cardForm(page).getByTestId('tahsilat-Banka')).toBeEnabled();
    await cardForm(page).getByTestId('tahsilat-Banka').click();
    await expect
      .poll(() => anahtarlar(finansIstekleri))
      .toEqual(['Banka:K1:600.00', 'Kasa:K1:700.00', 'Banka:K1:600.00']);
    expect(errors).toEqual([]);
  });
}

test('#318 B3/L1: Nakit donmuş (K1) + Kart 2xx → tazeleme 503 → iki form pasif + Yeniden yükle → Nakit tekrarı YİNE K1', async ({
  page,
}) => {
  await logIn(page);
  let n = 0;
  let cardOk = false;
  let corrupt = true;
  const { finansIstekleri } = await fakeApi(page, {
    detay: () => (cardOk ? detay(K2, 2000, 1600, 'v2') : detay(K1, 2600, 1000)),
    finans: (route, request) => {
      const g = request.postDataJSON() as Record<string, unknown>;
      if (++n === 1) return route.abort('connectionreset');
      if (g['hesap'] === 'Banka') {
        cardOk = true;
        return route.fulfill({ json: { id: 'k1' } });
      }
      return problem(route, 409, 'mukerrer', 'Başka tahsilat yazıldı.', {
        mevcut: { id: 'k1', belgeNo: 'T-9', tutar: 600, doviz: 'TRY', ayniIcerik: false },
      });
    },
  });
  await detailCheck(page, () => (cardOk && corrupt ? 503 : null));
  await page.goto(PAGE);
  await amountBox(cashForm(page)).fill('500');
  await cashForm(page).getByTestId('tahsilat-Kasa').click();
  await expect(toasts(page)).toContainText('Sunucuya ulaşılamadı');
  await sekme(page, 'Kart/Havale');
  await amountBox(cardForm(page)).fill('600');
  await cardForm(page).getByTestId('tahsilat-Banka').click();
  await expect(toasts(page)).toContainText('Tahsilat kaydedildi');
  await expect(cardForm(page).getByTestId('tahsilat-yeniden-yukle-Banka')).toBeVisible();
  await expect(cardForm(page).getByTestId('tahsilat-Banka')).toBeDisabled();
  await sekme(page, 'Nakit');
  await expect(cashForm(page).getByTestId('tahsilat-Kasa')).toBeDisabled();
  await expect(page.locator('.kf-tazeleme-hatasi')).toBeVisible();

  corrupt = false;
  await cashForm(page).getByTestId('tahsilat-yeniden-yukle-Kasa').click();
  await expect(cashForm(page).getByTestId('tahsilat-yeniden-yukle-Kasa')).toHaveCount(0);
  await expect(cashForm(page).getByTestId('tahsilat-Kasa')).toBeEnabled();
  await expect(amountBox(cashForm(page))).toHaveValue('500,00'); // donmuş deneme korundu
  await cashForm(page).getByTestId('tahsilat-Kasa').click();
  await expect
    .poll(() => anahtarlar(finansIstekleri))
    .toEqual(['Kasa:K1:500.00', 'Banka:K1:600.00', 'Kasa:K1:500.00']);
});

for (const [status, temporary] of [
  [404, false],
  [403, false],
  [400, false],
  [429, true],
] as const) {
  test(`#318 B4: 503 → Yeniden dene sürerken panel ve donmuş Nakit korunur; ardından ${status} → ${temporary ? 'GEÇİCİ, son iyi veri kalır' : 'kesin, eski veri YOK'}`, async ({
    page,
  }) => {
    await logIn(page);
    let mod: 'normal' | '503' | 'yavas' | 'son' = 'normal';
    const { finansIstekleri } = await fakeApi(page, {
      finans: (route) => route.abort('connectionreset'),
    });
    await detailCheck(
      page,
      () => (mod === '503' ? 503 : mod === 'son' ? status : null),
      () => (mod === 'yavas' ? 1500 : 0),
    );
    await page.goto(PAGE);
    await amountBox(cashForm(page)).fill('500');
    await cashForm(page).getByTestId('tahsilat-Kasa').click();
    await expect(toasts(page)).toContainText('Sunucuya ulaşılamadı');

    mod = '503';
    await goAndReturn(page);
    const banner = page.locator('.kf-tazeleme-hatasi');
    await expect(banner).toBeVisible();
    mod = 'yavas';
    const slowRead = page.waitForResponse(
      (r) => new URL(r.url()).pathname === `/api/ui/v1/kiralar/${RENTAL_ID}`,
    );
    await banner.getByRole('button').click();
    // Yeniden okuma sürerken: sayfa iskelete düşmez, panel yeniden kurulmaz, donmuş 500 durur.
    await expect(banner).toHaveCount(0);
    await expect(panel(page)).toHaveCount(1);
    await expect(amountBox(cashForm(page))).toHaveValue('500,00');
    await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.600,00');
    await slowRead;

    mod = 'son';
    await goAndReturn(page);
    if (temporary) {
      await expect(banner).toBeVisible();
      await expect(panel(page)).toHaveCount(1);
      await expect(amountBox(cashForm(page))).toHaveValue('500,00');
    } else {
      await expect(panel(page)).toHaveCount(0);
    }
    expect(anahtarlar(finansIstekleri)).toEqual(['Kasa:K1:500.00']);
  });
}

test('L3 metni: sonucu bilinmeyen tahsilat varken sekmeyi kapatmak özel uyarıyla sorulur', async ({
  page,
}) => {
  await logIn(page);
  await fakeApi(page, { finans: (r) => r.abort('connectionreset') });
  await page.goto(PAGE);
  await expect(panel(page).getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue(
    '2.600,00',
  );
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect(toasts(page)).toContainText('Sunucuya ulaşılamadı');
  await page.getByRole('button', { name: /Kira 2026220901001 sekmesini kapat/ }).click();
  const approval = page.getByRole('alertdialog');
  await expect(approval).toContainText('Sonucu bilinmeyen bir tahsilat/ödeme var');
  await approval.getByRole('button', { name: 'Sayfada kal' }).click();
});

test('L2/L6: Kalan rozeti + fazla tahsilat uyarısı; döviz değişince ön-dolu tutar temizlenir; depozito ikinci kez önerilmez', async ({
  page,
}) => {
  await logIn(page);
  let depositTaken = false;
  const { finansIstekleri } = await fakeApi(page, {
    detay: () => {
      const d = detay(K1, 2600, 1000) as { kira: Record<string, unknown> };
      return { ...d, kira: { ...d.kira, depozito: 1500 } };
    },
    finans: (route) => {
      depositTaken = true;
      return route.fulfill({ json: { id: 'd1' } });
    },
  });
  await page.goto(PAGE);
  const p = panel(page);
  await expect(p.getByTestId('finans-kalan')).toHaveText('Kalan: 2.600,00 ₺');
  const amount = p.getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00');
  await expect(p.getByTestId('tahsilat-bakiye-Kasa')).toHaveCount(0);
  await amount.fill('3000');
  await expect(p.getByTestId('tahsilat-bakiye-Kasa')).toContainText(
    'kalan bakiyeyi (2.600,00 ₺) aşıyor',
  );

  // Döviz değişince DOKUNULMAMIŞ ön-dolu tutar temizlenir (Kart formu: tutar hâlâ öneri).
  await p.getByRole('tab', { name: 'Kart/Havale', exact: true }).click();
  const card = p.getByTestId('tahsilat-formu-Banka');
  await expect(card.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('2.600,00');
  await card.getByRole('combobox', { name: 'Döviz' }).selectOption('USD');
  await expect(card.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('');

  // Depozito: alındıktan sonra kiranın depozitosu yeniden önerilmez → ikinci tık istek üretmez.
  await p.getByRole('tab', { name: 'Nakit', exact: true }).click();
  const dep = p.locator('rc-kf-finans-depozito').getByRole('textbox', { name: 'Depozito tutarı' });
  await expect(dep).toHaveValue('1.500,00');
  await p.getByTestId('depozito-al').click();
  await expect(toasts(page)).toContainText('Depozito alındı.');
  expect(depositTaken).toBe(true);
  await expect(dep).toHaveValue('');
  await p.getByTestId('depozito-al').click();
  await page.waitForTimeout(300);
  expect(finansIstekleri.filter((k) => k.yol.endsWith('/depozito/al'))).toHaveLength(1);
});

test('L6: kalan bakiye yoksa tahsilat formunda uyarı', async ({ page }) => {
  await logIn(page);
  await fakeApi(page, { detay: () => detay(K1, 0, 3600) });
  await page.goto(PAGE);
  await expect(panel(page).getByTestId('finans-kalan')).toHaveText('Kalan: 0,00 ₺');
  await expect(panel(page).getByTestId('tahsilat-bakiye-Kasa')).toContainText('Kalan bakiye yok');
  await expect(panel(page).getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('');
});

test('L3: panelde yazılmış tutar varken kira sekmesini KAPATMAK sorulur (sekme değişimi değil — bileşen yaşar)', async ({
  page,
}) => {
  await logIn(page);
  await fakeApi(page);
  await page.goto(PAGE);
  const amount = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('2.600,00');
  await amount.fill('777');
  await page.getByRole('button', { name: /Kira 2026220901001 sekmesini kapat/ }).click();
  const approval = page.getByRole('alertdialog');
  await expect(approval).toBeVisible();
  await approval.getByRole('button', { name: 'Sayfada kal' }).click();
  await expect(amount).toHaveValue('777,00'); // yazılan yerinde
});

test('L4: kapalı vergi kutusunda geçersiz alan → Fatura kes kutuyu açıp alana odaklanır, istek yok', async ({
  page,
}) => {
  await logIn(page);
  const { finansIstekleri } = await fakeApi(page, {
    finans: (r) => r.fulfill({ json: { id: 'f' } }),
  });
  await page.goto(PAGE);
  await sekme(page, 'Faturalar');
  const fat = panel(page).locator('rc-kf-finans-faturalar');
  await fat.locator('summary').click();
  const rate = fat.getByRole('textbox', { name: 'Tevkifat oranı %' });
  await rate.fill('150');
  await fat.locator('summary').click(); // kapat
  await expect(rate).toBeHidden();
  await fat.getByTestId('fatura-kes').click();
  await expect(rate).toBeVisible();
  await expect(rate).toBeFocused();
  await expect(fat).toContainText('En çok 100');
  expect(finansIstekleri).toHaveLength(0);
});

test('L7: dönem kesiminde 400 → plan yeniden okunur', async ({ page }) => {
  await logIn(page);
  let planRead = 0;
  page.on('request', (r) => {
    if (r.url().endsWith('/donem-plani')) planRead++;
  });
  await fakeApi(page, {
    finans: (route) => problem(route, 400, 'dogrulama', 'Kira faturaları bu sırada değişti.'),
  });
  await page.goto(PAGE);
  await sekme(page, 'Dönemler');
  await expect(panel(page).getByTestId('donem-kes-2')).toBeVisible();
  const once = planRead;
  await panel(page).getByTestId('donem-kes-2').click();
  await expect(toasts(page)).toContainText('Kira faturaları bu sırada değişti.');
  await expect.poll(() => planRead).toBeGreaterThan(once);
});

test('Muhasebe (FinanceWrite, OperationsWrite yok): kur listesi ve tedarikçi cari araması çalışır', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await logIn(page, { ...BEN, rol: 'Muhasebe', izinler: ['FinanceWrite', 'FinanceReverse'] });
  const selections: string[] = [];
  page.on('request', (r) => {
    if (r.url().includes('/api/ui/v1/secim/')) selections.push(new URL(r.url()).pathname);
  });
  await fakeApi(page, {
    detay: () => ({
      ...detay(K1, 2600, 1000),
      yetkiler: { operasyon: false, silme: false, finans: true },
    }),
  });
  await page.route(/\/api\/ui\/v1\/secim\/musteri/, (route) =>
    route.fulfill({ json: [{ id: CUSTOMER_ID, etiket: 'Yol Yardım Ltd.', tip: 'Kurumsal' }] }),
  );
  await page.goto(PAGE);
  await sekme(page, 'Kurlar');
  await expect(panel(page).getByRole('cell', { name: 'USD — ABD Doları' })).toBeVisible();
  await sekme(page, 'Dış hizmet');
  const account = panel(page).getByRole('combobox', { name: 'Tedarikçi cari' });
  await account.click();
  await account.fill('yol');
  await expect(page.getByRole('option', { name: /Yol Yardım Ltd\./ })).toBeVisible();
  expect(selections).toEqual(
    expect.arrayContaining(['/api/ui/v1/secim/kur', '/api/ui/v1/secim/musteri']),
  );
  expect(errors).toEqual([]);
});

test.describe('390 px ve koyu tema', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  test('finans paneli: her sekmede gövde taşması yok, axe ciddi/kritik 0', async ({ page }) => {
    await logIn(page, ME_REVERSE);
    await fakeApi(page);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto(PAGE);
    await expect(panel(page).getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue(
      '2.600,00',
    );
    for (const name of [
      'Nakit',
      'Kart/Havale',
      'Faturalar',
      'Dönemler',
      'Dış hizmet',
      'Kurlar',
      'Ceza/HGS',
    ]) {
      await sekme(page, name);
      await expect(panel(page).getByRole('tab', { name: name, exact: true })).toHaveAttribute(
        'aria-selected',
        'true',
      );
      expect(await measureOverflow(page), name).toEqual({ tasma: 0, suclular: [] });
      expect(await seriousViolations(page, '[data-testid="finans-paneli"]'), name).toEqual([]);
    }
  });
});
