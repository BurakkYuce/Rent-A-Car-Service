import { expect, test, type Locator, type Page, type Request, type Route } from '@playwright/test';

import { BEN, ciddiIhlaller, hatalariTopla, oturumAc, problem, xsrfYaz } from './ortak';
import { tasmaOlc } from './vitrin-sayfalari';

/**
 * F4.4 kira formu II — sabit panel finans işlemleri (sahte `/api/ui/v1`, üretim derlemesi + CSP).
 * Para kuralları (docs/api/idempotency-envanteri.md "SPA sözleşmesi"): deterministik tahsilat anahtarı
 * DETAYDAN ve başlıksız; ikinci meşru tahsilat tazelenen detayın anahtarıyla; çift tık tek istek;
 * `mukerrer`de otomatik tekrar yok; oturum düşünce AYNI istek (anahtar + gövde) tekrarlanır; dönem
 * kes+tahsil tekrarı sessiz ama `bilgi` gizlenmez; iptal dar izin bandı; 390 px + koyu tema axe 0.
 * Beklenen değerler sahte yanıtlardan ELLE kurulur (formül yok).
 */
const AG_HATASI = [/Failed to load resource: the server responded with a status of 4\d\d/];

const KIRA_ID = '0b0e7c1a-1111-4aaa-8bbb-000000000001';
const MUSTERI_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
const ARAC_ID = '0b0e7c1a-3333-4aaa-8bbb-000000000003';
const K1 = 'aaaaaaaa-0000-4000-8000-000000000001';
const K2 = 'aaaaaaaa-0000-4000-8000-000000000002';
const SAYFA = `/app/kiralar/${KIRA_ID}`;
const BEN_TERS = { ...BEN, izinler: [...BEN.izinler, 'FinanceReverse'] };

/** Kira sözleşmesi (gösterim alanları; formül yok): 3600 genel toplam, 1000 tahsil, 2600 kalan. */
function kira(bakiye: number, tahsilat: number, surum = 'v1'): Record<string, unknown> {
  const alanlar = [
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
    ...Object.fromEntries(alanlar.map((a) => [a, null])),
    id: KIRA_ID,
    sozlesmeNo: '2026220901001',
    durum: 'Kirada',
    musteriId: MUSTERI_ID,
    vehicleId: ARAC_ID,
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
    tahsilat,
    bakiye,
    provizyonDurum: 'Yok',
    riskOnay: false,
    fiyatTuru: 'KDV Dahil Günlük',
    doviz: 'TL',
    kurSnapshot: 1,
    donemselFaturalama: true,
    kdvOranSnapshot: 0.2,
    createdAtUtc: '2026-09-22T06:00:00+00:00',
    surum,
  };
}

function detay(anahtar: string, bakiye: number, tahsilat: number, surum = 'v1'): object {
  return {
    kira: kira(bakiye, tahsilat, surum),
    musteri: { id: MUSTERI_ID, ad: 'Ayşe Yılmaz' },
    ikinciSurucu: null,
    arac: {
      id: ARAC_ID,
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
    tahsilat: {
      anahtar,
      cariId: MUSTERI_ID,
      rentalId: KIRA_ID,
      doviz: 'TRY',
      varsayilanTutar: bakiye,
    },
  };
}

const DONEM = {
  donemSira: 2,
  donemBas: '2026-10-01T00:00:00+00:00',
  donemBit: '2026-10-31T00:00:00+00:00',
  durum: 'Planlandi',
  tahakkuk: 3100,
  invoiceId: null,
  kesilenTutar: null,
};

const DIS_HIZMET = {
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
  finans?: (route: Route, istek: Request) => Promise<unknown> | unknown;
  /** Detayın o anki hali (her GET'te çağrılır). */
  detay?: () => object;
  /** Kira yazma uçları (PUT `/kiralar/{id}` …) — verilmezse 500. */
  kiraYazma?: (route: Route, istek: Request) => Promise<unknown> | unknown;
}

interface Kayit {
  readonly yol: string;
  readonly govde: Record<string, unknown>;
  readonly anahtar: string | undefined;
}

/** Kira + finans + seçim uçları. Dönen `finansIstekleri` gönderilen her para isteğini kaydeder. */
async function sahteApi(page: Page, { finans, detay: detayFn, kiraYazma }: Sahte = {}) {
  const finansIstekleri: Kayit[] = [];
  let detayOkuma = 0;
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
    const istek = route.request();
    const yol = new URL(istek.url()).pathname;
    if (istek.method() === 'GET') {
      return route.fulfill({
        json: [
          {
            id: '0b0e7c1a-5555-4aaa-8bbb-000000000005',
            etiket: 'Kasa · Merkez Kasa (MRK · TRY)',
            kod: 'MRK',
            ad: 'Merkez Kasa',
            tur: istek.url().includes('tur=Banka') ? 'Banka' : 'Kasa',
            doviz: 'TRY',
          },
        ],
      });
    }
    finansIstekleri.push({
      yol,
      govde: (istek.postDataJSON() ?? {}) as Record<string, unknown>,
      anahtar: istek.headers()['idempotency-key'],
    });
    return (finans ?? ((r) => r.fulfill({ status: 500 })))(route, istek);
  });
  await page.route(/\/api\/ui\/v1\/kiralar(\/|\?|$)/, (route) => {
    const istek = route.request();
    const yol = new URL(istek.url()).pathname.replace('/api/ui/v1/kiralar', '');
    if (istek.method() !== 'GET') {
      return (kiraYazma ?? ((r) => r.fulfill({ status: 500 })))(route, istek);
    }
    if (yol === `/${KIRA_ID}`) {
      detayOkuma++;
      return route.fulfill({ json: detayFn?.() ?? detay(K1, 2600, 1000) });
    }
    if (yol === `/${KIRA_ID}/faturalar`) return route.fulfill({ json: [] });
    if (yol === `/${KIRA_ID}/cezalar`)
      return route.fulfill({ json: { cezalar: [], hgsGecisleri: [] } });
    if (yol === `/${KIRA_ID}/donem-plani`) return route.fulfill({ json: [DONEM] });
    if (yol === `/${KIRA_ID}/dis-hizmetler`) return route.fulfill({ json: [DIS_HIZMET] });
    if (yol === `/${KIRA_ID}/karne-ozeti`) {
      return route.fulfill({ json: { vehicleId: ARAC_ID, dolulukYuzde: 61.5 } });
    }
    if (yol === '/form-varsayilanlari') {
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
    if (yol.startsWith(`/${KIRA_ID}/donus-hesapla`)) {
      return route.fulfill({ json: { ok: false, hata: 'Önizleme yok.' } });
    }
    return route.fulfill({ status: 404, json: { status: 404, detail: 'yok' } });
  });
  return { finansIstekleri, detayOkuma: () => detayOkuma };
}

const panel = (page: Page): Locator => page.getByTestId('finans-paneli');
const toastlar = (page: Page): Locator => page.locator('rc-toast-alani');

async function sekme(page: Page, ad: string): Promise<void> {
  await panel(page).getByRole('tab', { name: ad, exact: true }).click();
}

const tahsilatlar = (k: readonly Kayit[]) => k.filter((x) => x.yol.endsWith('/finans/tahsilat'));

test('tahsilat: "1.500,50" → 1500.50, anahtar detaydan ve başlıksız; ikinci meşru tahsilat YENİ anahtarla', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await oturumAc(page);
  let yazildi = false;
  const { finansIstekleri } = await sahteApi(page, {
    detay: () => (yazildi ? detay(K2, 1099.5, 2500.5) : detay(K1, 2600, 1000)),
    finans: (route) => {
      yazildi = true;
      return route.fulfill({ json: { id: 'c1' } });
    },
  });
  await page.goto(SAYFA);
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00'); // sunucunun varsayılan tutarı (bakiye)
  // Klavyeyle (Tab → tümü seçili) gelip yazmak tutarı DEĞİŞTİRİR, sona EKLEMEZ ("2600,00500" → 2.600,01 değil).
  await panel(page).getByRole('tab', { name: 'Nakit', exact: true }).focus();
  await page.keyboard.press('Tab');
  await expect(tutar).toBeFocused();
  await page.keyboard.type('1.500,50');
  await expect(tutar).toHaveValue('1.500,50');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect(toastlar(page)).toContainText('Tahsilat kaydedildi.');

  // Tazelenen detay: yeni kalan ön-dolu, yeni anahtar.
  await expect(tutar).toHaveValue('1.099,50');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect.poll(() => tahsilatlar(finansIstekleri).length).toBe(2);

  const [a, b] = tahsilatlar(finansIstekleri);
  expect(a?.govde).toMatchObject({
    cariId: MUSTERI_ID,
    kiraId: KIRA_ID,
    tahsilatAnahtar: K1,
    tutar: '1500.50',
    hesap: 'Kasa',
    doviz: 'TRY',
  });
  expect(Number(a?.govde['tutar'])).toBe(1500.5);
  expect('kur' in (a?.govde ?? {})).toBe(false); // TRY'de kur gönderilmez
  expect(a?.anahtar).toBeUndefined(); // deterministik anahtar varken başlık yok
  expect(b?.govde).toMatchObject({ tahsilatAnahtar: K2, tutar: '1099.50' });
  expect(hatalar).toEqual([]);
});

test('çift tık tek istek (istek sürerken ikinci gönderim yok)', async ({ page }) => {
  await oturumAc(page);
  const { finansIstekleri } = await sahteApi(page, {
    finans: async (route) => {
      await new Promise((r) => setTimeout(r, 400));
      return route.fulfill({ json: { id: 'c1' } });
    },
  });
  await page.goto(SAYFA);
  await expect(panel(page).getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue(
    '2.600,00',
  );
  await panel(page).getByTestId('tahsilat-Kasa').dblclick();
  await expect(toastlar(page)).toContainText('Tahsilat kaydedildi.');
  expect(tahsilatlar(finansIstekleri)).toHaveLength(1);
});

test('409 mukerrer (bayat anahtar): otomatik tekrar YOK, kayıt yeniden yüklenir, "Kayıt değişmiş" + sunucu mesajı', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await oturumAc(page);
  const mesaj = 'Kiranın bakiyesi ya da kasa işlemleri bu ekran açıldıktan sonra değişti.';
  const { finansIstekleri, detayOkuma } = await sahteApi(page, {
    finans: (route) => problem(route, 409, 'mukerrer', mesaj),
  });
  await page.goto(SAYFA);
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  await tutar.click(); // odak → düzenleme yazımı; sonra tümünü seçip yaz
  await tutar.fill('500');
  const once = detayOkuma();
  await panel(page).getByTestId('tahsilat-Kasa').click();

  // Bayat anahtarda HİÇBİR ŞEY yazılmadı: "Mükerrer işlem" değil, nötr uyarı + sunucu mesajı.
  const toast = toastlar(page);
  await expect(toast).toContainText('Kira kaydı değişmiş');
  await expect(toast).toContainText(`${mesaj} Kayıt yeniden yüklendi.`);
  await expect(toast).not.toContainText('Mükerrer işlem');
  await expect.poll(detayOkuma).toBeGreaterThan(once);
  // HIGH-1: tutar TEMİZLENİR ve yeniden ön-doldurulmaz — kullanıcı güncel bakiyeye bakıp bilinçli girer.
  await expect(tutar).toHaveValue('');
  await page.waitForTimeout(300);
  expect(tahsilatlar(finansIstekleri)).toHaveLength(1);
  expect(hatalar).toEqual([]);
});

test('HIGH-1 (R1b): ilk tahsilat yazıldı ama yanıt düştü → AYNI anahtarla tekrar → 409 mevcut → "zaten kaydedildi", form boş, ikinci tahsilat YOK', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, [...AG_HATASI, /ERR_CONNECTION_RESET|net::/]);
  await oturumAc(page);
  let yazildi = false;
  const detail = 'Bu tahsilat zaten kaydedildi (No T-000042, 500,00 TRY); yeni tahsilat yazılmadı.';
  const { finansIstekleri } = await sahteApi(page, {
    detay: () => (yazildi ? detay(K2, 2100, 1500, 'v2') : detay(K1, 2600, 1000)),
    finans: (route, istek) => {
      const g = istek.postDataJSON() as Record<string, unknown>;
      if (!yazildi) {
        yazildi = true; // sunucu yazdı…
        return route.abort('connectionreset'); // …ama yanıt istemciye ulaşmadı
      }
      if (g['tahsilatAnahtar'] === K1) {
        return problem(route, 409, 'mukerrer', detail, {
          mevcut: { id: 'c1', belgeNo: 'T-000042', tutar: 500, doviz: 'TRY' },
        });
      }
      return route.fulfill({ json: { id: 'c2' } });
    },
  });
  await page.goto(SAYFA);
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  await tutar.fill('500');
  const dugme = panel(page).getByTestId('tahsilat-Kasa');
  await dugme.click();
  await expect(toastlar(page)).toContainText('Sunucuya ulaşılamadı');
  await dugme.click(); // kullanıcı doğru olanı yapar: yeniden dener (AYNI anahtar)

  const toast = toastlar(page);
  await expect(toast).toContainText('İşlem zaten kaydedildi');
  await expect(toast).toContainText(detail);
  await expect(toast).not.toContainText('Kira kaydı değişmiş');
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.100,00'); // kayıt yeniden yüklendi
  await expect(tutar).toHaveValue(''); // form temiz; yeni tutar önerilmez

  // Kullanıcı tekrar basarsa: boş tutar → istemci doğrulaması, İSTEK YOK.
  await dugme.click();
  await page.waitForTimeout(300);
  const t = tahsilatlar(finansIstekleri);
  expect(t).toHaveLength(2);
  expect(t.map((k) => k.govde['tahsilatAnahtar'])).toEqual([K1, K1]);
  expect(t[1]?.govde).toEqual(t[0]?.govde);
  expect(hatalar).toEqual([]);
});

test('oturum düşünce giden havale: yerinde giriş → AYNI Idempotency-Key + AYNI gövde', async ({
  page,
}) => {
  await oturumAc(page);
  let n = 0;
  const { finansIstekleri } = await sahteApi(page, {
    finans: (route) =>
      ++n === 1
        ? problem(route, 401, 'oturum_yok', 'Oturum açık değil.')
        : route.fulfill({ json: { id: 'o1' } }),
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await xsrfYaz(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await xsrfYaz(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });
  await page.goto(SAYFA);
  await sekme(page, 'Kart/Havale');
  const odeme = panel(page).locator('rc-kf-finans-odeme');
  await odeme.getByRole('textbox', { name: 'Tutar', exact: true }).fill('1.250,75');
  await odeme.getByTestId('odeme').click();

  const diyalog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(diyalog).toBeVisible();
  await diyalog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await diyalog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(toastlar(page)).toContainText('Giden havale kaydedildi.');

  const [a, b] = finansIstekleri;
  expect(finansIstekleri).toHaveLength(2);
  expect(a?.anahtar).toMatch(/^[0-9a-f-]{36}$/);
  expect(b?.anahtar).toBe(a?.anahtar);
  expect(b?.govde).toEqual(a?.govde);
  expect(a?.govde).toMatchObject({ cariId: MUSTERI_ID, tutar: '1250.75', hesap: 'Banka' });
  expect('kiraId' in (a?.govde ?? {})).toBe(false);
});

test('dönem kes + tahsil: çift gönderim SESSİZ (aynı fatura), tahsilat yazılmadı bilgisi gizlenmez', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await oturumAc(page);
  const bilgi = 'Bu dönemin tahsilatı daha önce alınmış; yeni tahsilat yazılmadı.';
  let n = 0;
  const { finansIstekleri } = await sahteApi(page, {
    finans: (route) =>
      route.fulfill({
        json:
          ++n === 1
            ? { faturaId: 'f1', tahsilatYazildi: true, bilgi: null }
            : { faturaId: 'f1', tahsilatYazildi: false, bilgi },
      }),
  });
  await page.goto(SAYFA);
  await sekme(page, 'Dönemler');
  const p = panel(page);
  await p.getByRole('checkbox', { name: 'Tahsilat 2' }).check();
  await p.getByRole('combobox', { name: 'Hesap türü 2' }).selectOption({ label: 'Banka' });
  await p.getByTestId('donem-kes-2').click();
  await expect(toastlar(page)).toContainText('Dönem faturası kesildi, tahsilat yazıldı.');

  // İkinci sekme/tekrar gönderim: sunucu aynı faturayı döner, tahsilat yazılmaz — bilgi görünür.
  await p.getByTestId('donem-kes-2').click();
  await expect(p.getByTestId('donem-bilgi')).toHaveText(bilgi);
  await expect(toastlar(page)).toContainText(bilgi);
  expect(finansIstekleri).toHaveLength(2);
  for (const k of finansIstekleri) {
    expect(k.govde).toEqual({ kiraId: KIRA_ID, donemSira: 2, tahsilat: true, hesap: 'Banka' });
    expect(k.anahtar).toBeUndefined(); // yapısal: başlık yok
  }
  expect(hatalar).toEqual([]);
});

test('dış hizmet iptali: FinanceReverse yoksa düğme yok; sunucu 403 verirse izin bandı, liste değişmez', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await oturumAc(page);
  await sahteApi(page);
  await page.goto(SAYFA);
  await sekme(page, 'Dış hizmet');
  await expect(panel(page).getByRole('cell', { name: 'DH-000001' })).toBeVisible();
  await expect(panel(page).getByRole('button', { name: /İptal \(ters kayıt\)/ })).toHaveCount(0);

  // Oturumda dar izin var görünür ama sunucu reddeder (ör. kullanıcı bazlı istisna yeni eklendi).
  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await oturumAc(page, BEN_TERS);
  const { finansIstekleri } = await sahteApi(page, {
    finans: (route) =>
      problem(route, 403, 'yetki_yok', 'Bu işlem için Finans ters kayıt yetkisi gerekir.'),
  });
  await page.goto(SAYFA);
  await sekme(page, 'Dış hizmet');
  await panel(page).getByRole('button', { name: 'İptal (ters kayıt) DH-000001' }).click();
  const onay = page.getByRole('alertdialog', { name: 'DH-000001 iptal edilsin mi?' });
  await onay.getByRole('button', { name: 'İptal et' }).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText(
    'Bu işlem için Finans ters kayıt yetkisi gerekir.',
  );
  expect(finansIstekleri.map((k) => k.yol)).toEqual([
    `/api/ui/v1/finans/dis-hizmet/${DIS_HIZMET.id}/iptal`,
  ]);
  await expect(panel(page).getByRole('cell', { name: 'Kayitli' })).toBeVisible();
  expect(hatalar).toEqual([]);
});

test('panel işlemi sonrası kira sürümü tazelenir: kirli formla Kaydet 409 almaz, yeni surum + yazılan korunur', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await oturumAc(page);
  let tahsilEdildi = false;
  const putlar: Record<string, unknown>[] = [];
  await sahteApi(page, {
    // Tahsilat kiranın Tahsilat/Bakiye'sini ve dolayısıyla sürümünü değiştirir (sunucu: v1 → v2).
    detay: () => (tahsilEdildi ? detay(K2, 1100, 2500, 'v2') : detay(K1, 2600, 1000, 'v1')),
    finans: (route) => {
      tahsilEdildi = true;
      return route.fulfill({ json: { id: 'c1' } });
    },
    kiraYazma: (route, istek) => {
      const g = istek.postDataJSON() as Record<string, unknown>;
      putlar.push(g);
      if (g['surum'] !== (tahsilEdildi ? 'v2' : 'v1')) {
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
  await page.goto(`${SAYFA}#sekme=ayrintilar`);
  const aciklama = page
    .getByRole('tabpanel', { name: 'Ayrıntılar' })
    .getByRole('textbox', { name: 'Açıklama', exact: true });
  await aciklama.fill('panel işleminden sonra kaydet');

  // Ana form KİRLİYKEN sabit panelde tahsilat → `degisti` → kira detayı (+ surum) tazelenir.
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect(toastlar(page)).toContainText('Tahsilat kaydedildi.');
  await expect(tutar).toHaveValue('1.100,00');
  await expect(page.getByTestId('yan-ozet')).toContainText('1.100,00');
  await expect(aciklama).toHaveValue('panel işleminden sonra kaydet'); // yazılan ezilmedi

  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  expect(putlar).toHaveLength(1); // 409 → ikinci deneme YOK: ilk PUT güncel sürümle gitti
  expect(putlar[0]).toMatchObject({ surum: 'v2', aciklama: 'panel işleminden sonra kaydet' });
  await expect(page.locator('rc-uyari-bandi')).not.toContainText('başka bir oturumda');
  expect(hatalar).toEqual([]);
});

test('L1: fareyle imleci tutarın ortasına koyan kullanıcı oraya yazar (tümü seçilmez); Tab ile gelince tümü seçilir', async ({
  page,
}) => {
  await oturumAc(page);
  await sahteApi(page);
  await page.goto(SAYFA);
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  // Düzenleme yazımında "26|00,00": iki rakamdan sonrasına tıkla (sağa yaslı metin, genişlik ölçülür).
  const kutu = await tutar.boundingBox();
  const x = await tutar.evaluate((el: HTMLInputElement) => {
    const st = getComputedStyle(el);
    const c = document.createElement('canvas').getContext('2d');
    if (!c) return 0;
    c.font = `${st.fontWeight} ${st.fontSize} ${st.fontFamily}`;
    const sag =
      el.getBoundingClientRect().right -
      parseFloat(st.paddingRight) -
      parseFloat(st.borderRightWidth);
    return sag - c.measureText('2600,00').width + c.measureText('26').width;
  });
  await page.mouse.click(x, (kutu?.y ?? 0) + (kutu?.height ?? 0) / 2);
  const secim = await tutar.evaluate(
    (el: HTMLInputElement) => el.selectionEnd! - el.selectionStart!,
  );
  expect(secim).toBe(0);
  await page.keyboard.type('9');
  await expect(tutar).toHaveValue('26900,00');

  // Klavye odağı: tümü seçili, yazılan yerine geçer.
  await panel(page).getByRole('tab', { name: 'Nakit', exact: true }).focus();
  await page.keyboard.press('Tab');
  await expect(tutar).toBeFocused();
  await page.keyboard.type('150');
  await expect(tutar).toHaveValue('150');
});

test('L2/L6: Kalan rozeti + fazla tahsilat uyarısı; döviz değişince ön-dolu tutar temizlenir; depozito ikinci kez önerilmez', async ({
  page,
}) => {
  await oturumAc(page);
  let depozitoAlindi = false;
  const { finansIstekleri } = await sahteApi(page, {
    detay: () => {
      const d = detay(K1, 2600, 1000) as { kira: Record<string, unknown> };
      return { ...d, kira: { ...d.kira, depozito: 1500 } };
    },
    finans: (route) => {
      depozitoAlindi = true;
      return route.fulfill({ json: { id: 'd1' } });
    },
  });
  await page.goto(SAYFA);
  const p = panel(page);
  await expect(p.getByTestId('finans-kalan')).toHaveText('Kalan: 2.600,00 ₺');
  const tutar = p.getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  await expect(p.getByTestId('tahsilat-bakiye-Kasa')).toHaveCount(0);
  await tutar.fill('3000');
  await expect(p.getByTestId('tahsilat-bakiye-Kasa')).toContainText(
    'kalan bakiyeyi (2.600,00 ₺) aşıyor',
  );

  // Döviz değişince DOKUNULMAMIŞ ön-dolu tutar temizlenir (Kart formu: tutar hâlâ öneri).
  await p.getByRole('tab', { name: 'Kart/Havale', exact: true }).click();
  const kart = p.locator('rc-kf-finans-tahsilat');
  await expect(kart.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('2.600,00');
  await kart.getByRole('combobox', { name: 'Döviz' }).selectOption('USD');
  await expect(kart.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('');

  // Depozito: alındıktan sonra kiranın depozitosu yeniden önerilmez → ikinci tık istek üretmez.
  await p.getByRole('tab', { name: 'Nakit', exact: true }).click();
  const dep = p.locator('rc-kf-finans-depozito').getByRole('textbox', { name: 'Depozito tutarı' });
  await expect(dep).toHaveValue('1.500,00');
  await p.getByTestId('depozito-al').click();
  await expect(toastlar(page)).toContainText('Depozito alındı.');
  expect(depozitoAlindi).toBe(true);
  await expect(dep).toHaveValue('');
  await p.getByTestId('depozito-al').click();
  await page.waitForTimeout(300);
  expect(finansIstekleri.filter((k) => k.yol.endsWith('/depozito/al'))).toHaveLength(1);
});

test('L6: kalan bakiye yoksa tahsilat formunda uyarı', async ({ page }) => {
  await oturumAc(page);
  await sahteApi(page, { detay: () => detay(K1, 0, 3600) });
  await page.goto(SAYFA);
  await expect(panel(page).getByTestId('finans-kalan')).toHaveText('Kalan: 0,00 ₺');
  await expect(panel(page).getByTestId('tahsilat-bakiye-Kasa')).toContainText('Kalan bakiye yok');
  await expect(panel(page).getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('');
});

test('L3: panelde yazılmış tutar varken kira sekmesini KAPATMAK sorulur (sekme değişimi değil — bileşen yaşar)', async ({
  page,
}) => {
  await oturumAc(page);
  await sahteApi(page);
  await page.goto(SAYFA);
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  await tutar.fill('777');
  await page.getByRole('button', { name: /Kira 2026220901001 sekmesini kapat/ }).click();
  const onay = page.getByRole('alertdialog');
  await expect(onay).toBeVisible();
  await onay.getByRole('button', { name: 'Sayfada kal' }).click();
  await expect(tutar).toHaveValue('777,00'); // yazılan yerinde
});

test('L4: kapalı vergi kutusunda geçersiz alan → Fatura kes kutuyu açıp alana odaklanır, istek yok', async ({
  page,
}) => {
  await oturumAc(page);
  const { finansIstekleri } = await sahteApi(page, {
    finans: (r) => r.fulfill({ json: { id: 'f' } }),
  });
  await page.goto(SAYFA);
  await sekme(page, 'Faturalar');
  const fat = panel(page).locator('rc-kf-finans-faturalar');
  await fat.locator('summary').click();
  const oran = fat.getByRole('textbox', { name: 'Tevkifat oranı %' });
  await oran.fill('150');
  await fat.locator('summary').click(); // kapat
  await expect(oran).toBeHidden();
  await fat.getByTestId('fatura-kes').click();
  await expect(oran).toBeVisible();
  await expect(oran).toBeFocused();
  await expect(fat).toContainText('En çok 100');
  expect(finansIstekleri).toHaveLength(0);
});

test('L7: dönem kesiminde 400 → plan yeniden okunur', async ({ page }) => {
  await oturumAc(page);
  let planOkuma = 0;
  page.on('request', (r) => {
    if (r.url().endsWith('/donem-plani')) planOkuma++;
  });
  await sahteApi(page, {
    finans: (route) => problem(route, 400, 'dogrulama', 'Kira faturaları bu sırada değişti.'),
  });
  await page.goto(SAYFA);
  await sekme(page, 'Dönemler');
  await expect(panel(page).getByTestId('donem-kes-2')).toBeVisible();
  const once = planOkuma;
  await panel(page).getByTestId('donem-kes-2').click();
  await expect(toastlar(page)).toContainText('Kira faturaları bu sırada değişti.');
  await expect.poll(() => planOkuma).toBeGreaterThan(once);
});

test('Muhasebe (FinanceWrite, OperationsWrite yok): kur listesi ve tedarikçi cari araması çalışır', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await oturumAc(page, { ...BEN, rol: 'Muhasebe', izinler: ['FinanceWrite', 'FinanceReverse'] });
  const secimler: string[] = [];
  page.on('request', (r) => {
    if (r.url().includes('/api/ui/v1/secim/')) secimler.push(new URL(r.url()).pathname);
  });
  await sahteApi(page, {
    detay: () => ({
      ...detay(K1, 2600, 1000),
      yetkiler: { operasyon: false, silme: false, finans: true },
    }),
  });
  await page.route(/\/api\/ui\/v1\/secim\/musteri/, (route) =>
    route.fulfill({ json: [{ id: MUSTERI_ID, etiket: 'Yol Yardım Ltd.', tip: 'Kurumsal' }] }),
  );
  await page.goto(SAYFA);
  await sekme(page, 'Kurlar');
  await expect(panel(page).getByRole('cell', { name: 'USD — ABD Doları' })).toBeVisible();
  await sekme(page, 'Dış hizmet');
  const cari = panel(page).getByRole('combobox', { name: 'Tedarikçi cari' });
  await cari.click();
  await cari.fill('yol');
  await expect(page.getByRole('option', { name: /Yol Yardım Ltd\./ })).toBeVisible();
  expect(secimler).toEqual(
    expect.arrayContaining(['/api/ui/v1/secim/kur', '/api/ui/v1/secim/musteri']),
  );
  expect(hatalar).toEqual([]);
});

test.describe('390 px ve koyu tema', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  test('finans paneli: her sekmede gövde taşması yok, axe ciddi/kritik 0', async ({ page }) => {
    await oturumAc(page, BEN_TERS);
    await sahteApi(page);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto(SAYFA);
    await expect(panel(page).getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue(
      '2.600,00',
    );
    for (const ad of [
      'Nakit',
      'Kart/Havale',
      'Faturalar',
      'Dönemler',
      'Dış hizmet',
      'Kurlar',
      'Ceza/HGS',
    ]) {
      await sekme(page, ad);
      await expect(panel(page).getByRole('tab', { name: ad, exact: true })).toHaveAttribute(
        'aria-selected',
        'true',
      );
      expect(await tasmaOlc(page), ad).toEqual({ tasma: 0, suclular: [] });
      expect(await ciddiIhlaller(page, '[data-testid="finans-paneli"]'), ad).toEqual([]);
    }
  });
});
