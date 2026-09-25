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
    toplamlar: { ekHizmetToplam: 0, cezaToplam: 0 },
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
    // F4.3b sekme verileri (bu dosyanın testleri onlara dokunmaz; 404 konsol hatası olmasın).
    if (yol === `/${KIRA_ID}/musteri-ozet`) {
      return route.fulfill({
        json: {
          id: MUSTERI_ID,
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
    if (yol === '/ek-hizmet-katalogu') return route.fulfill({ json: { ogeler: [], toplam: 0 } });
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
          mevcut: { id: 'c1', belgeNo: 'T-000042', tutar: 500, doviz: 'TRY', ayniIcerik: true },
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

/** Metin kutusunda (sağa yaslı) `once` önekinin bittiği x konumu — "imleci buraya koyan" tık için. */
async function metinX(kutu: Locator, tum: string, once: string): Promise<number> {
  return kutu.evaluate(
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
    [tum, once] as const,
  );
}

async function tikla(page: Page, kutu: Locator, x: number): Promise<void> {
  const b = await kutu.boundingBox();
  await page.mouse.click(x, (b?.y ?? 0) + (b?.height ?? 0) / 2);
}

test('L1 + M-B: YAZILMIŞ tutarda fareyle ortaya tık imleci orada bırakır; Tab odağı tümünü seçer', async ({
  page,
}) => {
  await oturumAc(page);
  await sahteApi(page);
  await page.goto(SAYFA);
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  await tutar.fill('2600'); // kullanıcı yazdı
  await tutar.blur();
  await expect(tutar).toHaveValue('2.600,00');
  // Düzenleme yazımında "26|00,00": iki rakamdan sonrasına tıkla.
  await tikla(page, tutar, await metinX(tutar, '2600,00', '26'));
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

for (const [ad, nerede] of [
  ['Q7a/P1e ortası', 'orta'],
  ['M-B metnin solu', 'sol'],
] as const) {
  test(`M-B (${ad}): DOKUNULMAMIŞ ön-dolu tutara fareyle tıklayıp yazmak tutarı DEĞİŞTİRİR (başa/sona eklemez)`, async ({
    page,
  }) => {
    await oturumAc(page);
    const { finansIstekleri } = await sahteApi(page, {
      detay: () => {
        const d = detay(K1, 2600, 1000) as { kira: Record<string, unknown> };
        return { ...d, kira: { ...d.kira, depozito: 1500 } };
      },
      finans: (r) => r.fulfill({ json: { id: 'c' } }),
    });
    await page.goto(SAYFA);
    const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
    await expect(tutar).toHaveValue('2.600,00');
    // Koordinatla tıklanır: alan görünür alanda olmalı (sayfa bandıyla yan panel 720 px'in altına iner).
    await tutar.scrollIntoViewIfNeeded();
    const b = await tutar.boundingBox();
    const x =
      nerede === 'sol'
        ? (b?.x ?? 0) + 4 // metnin (sağa yaslı) çok solu: imleç 0'a düşerdi
        : await metinX(tutar, '2.600,00', '2.6');
    await tikla(page, tutar, x);
    await page.keyboard.type('500');
    await expect(tutar).toHaveValue('500');
    await panel(page).getByTestId('tahsilat-Kasa').click();
    await expect.poll(() => tahsilatlar(finansIstekleri).length).toBe(1);
    expect(tahsilatlar(finansIstekleri)[0]?.govde['tutar']).toBe('500.00');
    // Tahsilat sonrası tazeleme bitsin: "kira yeniden yükleniyor" notu depozito alanını aşağı kaydırır; ölçülen
    // tık konumu yük altında alanı ıskalıyordu (tazeleme bitince düğme yeniden açılır).
    await expect(panel(page).getByTestId('tahsilat-Kasa')).toBeEnabled();

    // Q7b depozito: ön-dolu 1.500,00 → ortasına tık + "1000" → 1000.00 (10001500.00 değil).
    const dep = panel(page)
      .locator('rc-kf-finans-depozito')
      .getByRole('textbox', { name: 'Depozito tutarı' });
    await expect(dep).toHaveValue('1.500,00');
    await tikla(page, dep, await metinX(dep, '1.500,00', '1.5'));
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
  await oturumAc(page);
  const { finansIstekleri } = await sahteApi(page, {
    finans: (r) => r.fulfill({ json: { id: 'c' } }),
  });
  await page.goto(SAYFA);
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  await tutar.fill('2600');
  await tutar.blur();
  await tutar.evaluate((el: HTMLInputElement) => {
    el.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch' }));
    el.dispatchEvent(new PointerEvent('pointercancel', { bubbles: true, pointerType: 'touch' }));
  });
  await panel(page).getByRole('tab', { name: 'Nakit', exact: true }).focus();
  await page.keyboard.press('Tab');
  await expect(tutar).toBeFocused();
  await page.keyboard.type('500');
  await expect(tutar).toHaveValue('500');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect.poll(() => tahsilatlar(finansIstekleri).length).toBe(1);
  expect(tahsilatlar(finansIstekleri)[0]?.govde['tutar']).toBe('500.00');
});

test('M-A (G2/Q5) + L-2: iki sekme aynı anahtar — öteki 100 yazdı → 409 mevcut FARKLI → "YAZILMADI" uyarısı; dokunulmamış ön-dolu tutar YENİ bakiyeyle yenilenir, yeni anahtarla bilinçli gönderim', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await oturumAc(page);
  let digerYazdi = false;
  const detail =
    'Bu ekran açıldıktan sonra başka bir tahsilat yazıldı (No T-000099, 100,00 TRY); girdiğiniz 2.600,00 TRY YAZILMADI. Güncel bakiyeyi kontrol edin.';
  const { finansIstekleri } = await sahteApi(page, {
    detay: () => (digerYazdi ? detay(K2, 2500, 1100) : detay(K1, 2600, 1000)),
    finans: (route, istek) => {
      const g = istek.postDataJSON() as Record<string, unknown>;
      if (g['tahsilatAnahtar'] === K1) {
        digerYazdi = true; // öteki sekme K1 ile 100 yazmıştı
        return problem(route, 409, 'mukerrer', detail, {
          mevcut: { id: 'a', belgeNo: 'T-000099', tutar: 100, doviz: 'TRY', ayniIcerik: false },
        });
      }
      return route.fulfill({ json: { id: 'c2' } });
    },
  });
  await page.goto(SAYFA);
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  await panel(page).getByTestId('tahsilat-Kasa').click();

  const toast = toastlar(page);
  await expect(toast).toContainText('Başka bir tahsilat yazıldı');
  await expect(toast).toContainText('girdiğiniz 2.600,00 TRY YAZILMADI');
  await expect(toast).not.toContainText('İşlem zaten kaydedildi');
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.500,00'); // yeniden yüklendi
  // L-2: tutar elle yazılmadı (ön-dolu 2.600) → eski bakiye yerine yeni öneri; fazla tahsilat gitmez.
  await expect(tutar).toHaveValue('2.500,00');

  await panel(page).getByTestId('tahsilat-Kasa').click(); // bilinçli yeniden gönderim
  await expect(toast).toContainText('Tahsilat kaydedildi.');
  const t = tahsilatlar(finansIstekleri);
  expect(t.map((k) => k.govde['tahsilatAnahtar'])).toEqual([K1, K2]);
  expect(t[1]?.govde['tutar']).toBe('2500.00');
  expect(hatalar).toEqual([]);
});

test('M-A + L-2: kullanıcının ELLE yazdığı tutar "YAZILMADI" sonrası yeni bakiyeyle EZİLMEZ', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await oturumAc(page);
  let digerYazdi = false;
  await sahteApi(page, {
    detay: () => (digerYazdi ? detay(K2, 2500, 1100) : detay(K1, 2600, 1000)),
    finans: (route) => {
      digerYazdi = true;
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
  await page.goto(SAYFA);
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  await tutar.fill('700');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.500,00');
  await expect(tutar).toHaveValue(/^700(,00)?$/);
  expect(hatalar).toEqual([]);
});

test('M-C (4. tur): 500 yazıldı ama yanıt düştü → tutar 600\'e düzeltilip AYNI anahtarla tekrar → 409 mevcut FARKLI → "Önceki denemeniz kaydedilmiş … 600 YAZILMADI", tutar TEMİZLENİR; tekrar basış çift yazım ÜRETMEZ', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, [...AG_HATASI, /ERR_CONNECTION_RESET|net::/]);
  await oturumAc(page);
  let yazildi = false;
  const { finansIstekleri } = await sahteApi(page, {
    // Sunucu (elle): 1. istek 500 yazdı (kalan 2.600 → 2.100, yeni anahtar K2).
    detay: () => (yazildi ? detay(K2, 2100, 1500, 'v2') : detay(K1, 2600, 1000)),
    finans: (route, istek) => {
      const g = istek.postDataJSON() as Record<string, unknown>;
      if (!yazildi) {
        yazildi = true; // sunucu 500'ü yazdı…
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
  await page.goto(SAYFA);
  const tutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(tutar).toHaveValue('2.600,00');
  await tutar.fill('500');
  const dugme = panel(page).getByTestId('tahsilat-Kasa');
  await dugme.click();
  await expect(toastlar(page)).toContainText('Sunucuya ulaşılamadı');
  await tutar.fill('600'); // kullanıcı tutarı düzeltir
  await dugme.click();

  const toast = toastlar(page);
  await expect(toast).toContainText('Önceki denemeniz kaydedilmiş — yeni tutar yazılmadı');
  await expect(toast).toContainText('Önceki denemeniz kaydedilmiş (No T-000042, 500,00 ₺)');
  await expect(toast).toContainText('girdiğiniz 600,00 ₺ YAZILMADI');
  await expect(toast).not.toContainText('başka bir tahsilat yazıldı');
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.100,00'); // yeniden yüklendi
  await expect(tutar).toHaveValue(''); // TEMİZLENDİ; yeni öneri de basılmaz

  // Kullanıcı tekrar basar: boş tutar → istemci doğrulaması, İSTEK YOK → 1100 yazılamaz.
  await dugme.click();
  await page.waitForTimeout(300);
  const t = tahsilatlar(finansIstekleri);
  expect(t).toHaveLength(2);
  expect(t.map((k) => k.govde['tahsilatAnahtar'])).toEqual([K1, K1]);
  expect(t.map((k) => k.govde['tutar'])).toEqual(['500.00', '600.00']);
  expect(hatalar).toEqual([]);
});

test('5. tur MEDIUM-1: Nakit 500 yazıldı ama yanıt düştü → Kart/Havale\'de AYNI anahtarla 600 → "Önceki denemeniz kaydedilmiş", Kart tutarı TEMİZLENİR; tekrar basış çift yazım ÜRETMEZ', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, [...AG_HATASI, /ERR_CONNECTION_RESET|net::/]);
  await oturumAc(page);
  let yazildi = false;
  const { finansIstekleri } = await sahteApi(page, {
    // Sunucu (elle): Nakit'in 500'ü yazıldı; anahtar K1 artık bu kayda ait.
    detay: () => (yazildi ? detay(K2, 2100, 1500, 'v2') : detay(K1, 2600, 1000)),
    finans: (route, istek) => {
      const g = istek.postDataJSON() as Record<string, unknown>;
      if (!yazildi) {
        yazildi = true;
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
  await page.goto(SAYFA);
  const nakitTutar = panel(page).getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(nakitTutar).toHaveValue('2.600,00');
  await nakitTutar.fill('500');
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect(toastlar(page)).toContainText('Sunucuya ulaşılamadı');

  await sekme(page, 'Kart/Havale');
  // Sekme geçişi bir sonraki çizimde görünür; `rc-kf-finans-tahsilat` o ana dek hâlâ NAKİT formudur ve `fill`
  // 600'ü Nakit'e yazıyordu (Kart ön-dolu 2.600 gidiyordu). Hesaba özgü test kimliği Kart formunu bekler.
  const kart = panel(page).getByTestId('tahsilat-formu-Banka');
  const kartTutar = kart.getByRole('textbox', { name: 'Tutar', exact: true });
  await kartTutar.fill('600');
  const kartDugme = panel(page).getByTestId('tahsilat-Banka');
  await kartDugme.click();

  const toast = toastlar(page);
  await expect(toast).toContainText('Önceki denemeniz kaydedilmiş (No T-000042, 500,00 ₺)');
  await expect(toast).toContainText('girdiğiniz 600,00 ₺ YAZILMADI');
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.100,00');
  await expect(kartTutar).toHaveValue('');

  await kartDugme.click(); // boş tutar → istemci doğrulaması, İSTEK YOK
  await page.waitForTimeout(300);
  const t = tahsilatlar(finansIstekleri);
  expect(t.map((k) => [k.govde['tahsilatAnahtar'], k.govde['hesap'], k.govde['tutar']])).toEqual([
    [K1, 'Kasa', '500.00'],
    [K1, 'Banka', '600.00'],
  ]);
  expect(hatalar).toEqual([]);
});

test("H1 (#316): Nakit yanıtı koptu → hemen Kart/Havale'de 600 → gönderilen tutar EKRANDAKİ 600; Nakit formu dokunulmaz", async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, [...AG_HATASI, /ERR_CONNECTION_RESET|net::/]);
  await oturumAc(page);
  let istek = 0;
  let yazilan = false;
  const { finansIstekleri } = await sahteApi(page, {
    // Sunucu (elle): Nakit'in 500'ü YAZILMADI (bağlantı koptu); Kart'ın 600'ü K1 ile yazılır → kalan 2.000.
    detay: () => (yazilan ? detay(K2, 2000, 1600, 'v2') : detay(K1, 2600, 1000)),
    finans: (route) => {
      if (++istek === 1) return route.abort('connectionreset');
      yazilan = true;
      return route.fulfill({ json: { id: 'c1' } });
    },
  });
  await page.goto(SAYFA);
  const nakit = panel(page).getByTestId('tahsilat-formu-Kasa');
  const nakitTutar = nakit.getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(nakitTutar).toHaveValue('2.600,00');
  await nakitTutar.fill('500');
  await nakit.getByTestId('tahsilat-Kasa').click();
  await expect(toastlar(page)).toContainText('Sunucuya ulaşılamadı');

  await sekme(page, 'Kart/Havale');
  const kart = panel(page).getByTestId('tahsilat-formu-Banka');
  const kartTutar = kart.getByRole('textbox', { name: 'Tutar', exact: true });
  await kartTutar.fill('600');
  await page.waitForTimeout(1000); // arada hiçbir tazeleme/yeniden kurulum yazılanı ezmemeli
  await expect(kartTutar).toHaveValue('600');
  await kart.getByTestId('tahsilat-Banka').click();
  await expect(toastlar(page)).toContainText('Tahsilat kaydedildi.');
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.000,00');

  // Nakit formu Kart'a yazılandan etkilenmedi: sonucu bilinmeyen 500 donmuş hâliyle duruyor.
  await sekme(page, 'Nakit');
  await expect(
    panel(page)
      .getByTestId('tahsilat-formu-Kasa')
      .getByRole('textbox', { name: 'Tutar', exact: true }),
  ).toHaveValue(/^500(,00)?$/);
  expect(
    tahsilatlar(finansIstekleri).map((k) => [
      k.govde['tahsilatAnahtar'],
      k.govde['hesap'],
      k.govde['tutar'],
    ]),
  ).toEqual([
    [K1, 'Kasa', '500.00'],
    [K1, 'Banka', '600.00'],
  ]);
  expect(hatalar).toEqual([]);
});

test('H1 (#316): tahsilat sonrası kira tazelenirken İKİ formda da Tahsil Et pasif; yeni detayla Kart yeni anahtarı kullanır', async ({
  page,
}) => {
  await oturumAc(page);
  let yazilan = false;
  const { finansIstekleri } = await sahteApi(page, {
    // Sunucu (elle): Nakit 700 yazılır → kalan 2.600 − 700 = 1.900, yeni anahtar K2.
    detay: () => (yazilan ? detay(K2, 1900, 1700, 'v2') : detay(K1, 2600, 1000)),
    finans: (route) => {
      yazilan = true;
      return route.fulfill({ json: { id: 'c1' } });
    },
  });
  // Tahsilattan sonraki detay okuması test bırakana dek bekletilir (sahteApi'den SONRA kaydedilen önce eşleşir).
  let birak: () => void = () => undefined;
  const kapi = new Promise<void>((coz) => (birak = coz));
  await page.route(
    (url) => url.pathname === `/api/ui/v1/kiralar/${KIRA_ID}`,
    async (route) => {
      if (yazilan) await kapi;
      return route.fallback();
    },
  );
  await page.goto(SAYFA);
  const nakit = panel(page).getByTestId('tahsilat-formu-Kasa');
  await expect(nakit.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('2.600,00');
  await nakit.getByRole('textbox', { name: 'Tutar', exact: true }).fill('700');
  await nakit.getByTestId('tahsilat-Kasa').click();
  await expect(toastlar(page)).toContainText('Tahsilat kaydedildi.');
  await expect(nakit.getByTestId('tahsilat-Kasa')).toBeDisabled();

  await sekme(page, 'Kart/Havale');
  const kart = panel(page).getByTestId('tahsilat-formu-Banka');
  const kartDugme = kart.getByTestId('tahsilat-Banka');
  await expect(kart).toContainText('kira yeniden yükleniyor');
  await expect(kartDugme).toBeDisabled();
  await kartDugme.click({ force: true }); // pasif düğme: istek YOK
  await page.waitForTimeout(300);
  expect(tahsilatlar(finansIstekleri)).toHaveLength(1);

  birak();
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('1.900,00');
  await expect(kartDugme).toBeEnabled();
  await expect(kart.getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue('1.900,00');
  await kartDugme.click();
  await expect(toastlar(page)).toContainText('Tahsilat kaydedildi.');
  await expect
    .poll(() =>
      tahsilatlar(finansIstekleri).map((k) => [
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

const kartFormu = (page: Page): Locator => panel(page).getByTestId('tahsilat-formu-Banka');
const nakitFormu = (page: Page): Locator => panel(page).getByTestId('tahsilat-formu-Kasa');
const tutarKutusu = (l: Locator): Locator => l.getByRole('textbox', { name: 'Tutar', exact: true });

test("#318 T2: Kart'a yazılan 600 + giden havale 2xx (kira tazelenir) → 600 korunur ve aynen gider", async ({
  page,
}) => {
  await oturumAc(page);
  let odeme = false;
  const { finansIstekleri, detayOkuma } = await sahteApi(page, {
    // Giden havale kiraya bağlanmaz: anahtar aynı (K1), yalnız sürüm değişir.
    detay: () => (odeme ? detay(K1, 2600, 1000, 'v2') : detay(K1, 2600, 1000)),
    finans: (route) => {
      odeme = true;
      return route.fulfill({ json: { id: 'o1' } });
    },
  });
  await page.goto(SAYFA);
  await sekme(page, 'Kart/Havale');
  const kartTutar = tutarKutusu(kartFormu(page));
  await expect(kartTutar).toHaveValue('2.600,00');
  await kartTutar.fill('600');
  await tutarKutusu(panel(page).locator('rc-kf-finans-odeme')).fill('100');
  const once = detayOkuma();
  await panel(page).getByTestId('odeme').click();
  await expect.poll(() => detayOkuma()).toBeGreaterThan(once);
  await expect(kartTutar).toHaveValue(/^600(,00)?$/);
  await kartFormu(page).getByTestId('tahsilat-Banka').click();
  await expect
    .poll(() =>
      tahsilatlar(finansIstekleri).map((k) => [k.govde['tahsilatAnahtar'], k.govde['tutar']]),
    )
    .toEqual([[K1, '600.00']]);
});

test("#318 T3: Kart'a yazılan 600 + başka rotaya gidip dönüş (sekmeye dönüş tazelemesi) → 600 korunur ve aynen gider", async ({
  page,
}) => {
  await oturumAc(page);
  const { finansIstekleri, detayOkuma } = await sahteApi(page, {
    detay: () => detay(K1, 2650, 1000, 'v2'),
    finans: (route) => route.fulfill({ json: { id: 'c9' } }),
  });
  await page.goto(SAYFA);
  await sekme(page, 'Kart/Havale');
  await tutarKutusu(kartFormu(page)).fill('600');
  const once = detayOkuma();
  await page.evaluate(() => {
    history.pushState({}, '', '/app/');
    dispatchEvent(new PopStateEvent('popstate', { state: {} }));
  });
  await expect(page).not.toHaveURL(new RegExp(KIRA_ID));
  await expect(panel(page)).toBeHidden(); // rota gerçekten değişti (kira sekmesi arka planda yaşar)
  await page.goBack();
  await expect(page).toHaveURL(new RegExp(KIRA_ID));
  await expect.poll(() => detayOkuma()).toBeGreaterThan(once);
  const kart = kartFormu(page);
  if (!(await kart.count())) await sekme(page, 'Kart/Havale');
  await expect(tutarKutusu(kart)).toHaveValue(/^600(,00)?$/);
  await kart.getByTestId('tahsilat-Banka').click();
  await expect
    .poll(() => tahsilatlar(finansIstekleri).map((k) => [k.govde['hesap'], k.govde['tutar']]))
    .toEqual([['Banka', '600.00']]);
});

test('#318 T4/L1: Nakit 2xx sonrası kira tazelemesi 503 → iki formda "Yeniden yükle"; başarılı okumada düğmeler açılır', async ({
  page,
}) => {
  await oturumAc(page);
  let yazildi = false;
  let bozuk = true;
  const { finansIstekleri } = await sahteApi(page, {
    // Sunucu (elle): Nakit 700 yazılır → kalan 2.600 − 700 = 1.900, yeni anahtar K2.
    detay: () => (yazildi ? detay(K2, 1900, 1700, 'v2') : detay(K1, 2600, 1000)),
    finans: (route) => {
      yazildi = true;
      return route.fulfill({ json: { id: 'c1' } });
    },
  });
  await page.route(
    (url) => url.pathname === `/api/ui/v1/kiralar/${KIRA_ID}`,
    (route) =>
      yazildi && bozuk
        ? route.fulfill({ status: 503, json: { status: 503, kod: 'sunucu', detail: 'x' } })
        : route.fallback(),
  );
  const hatalar = hatalariTopla(page, [/status of 503/]);
  await page.goto(SAYFA);
  await tutarKutusu(nakitFormu(page)).fill('700');
  await nakitFormu(page).getByTestId('tahsilat-Kasa').click();
  await expect(toastlar(page)).toContainText('Tahsilat kaydedildi.');
  await expect(nakitFormu(page).getByTestId('tahsilat-yeniden-yukle-Kasa')).toBeVisible();
  await expect(nakitFormu(page)).toContainText('Kira yüklenemedi');
  await expect(nakitFormu(page).getByTestId('tahsilat-Kasa')).toBeDisabled();

  await sekme(page, 'Kart/Havale');
  const yenidenYukle = kartFormu(page).getByTestId('tahsilat-yeniden-yukle-Banka');
  await expect(yenidenYukle).toBeVisible();
  await expect(kartFormu(page).getByTestId('tahsilat-Banka')).toBeDisabled();

  bozuk = false;
  await yenidenYukle.click();
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('1.900,00');
  await expect(yenidenYukle).toHaveCount(0);
  await expect(kartFormu(page).getByTestId('tahsilat-Banka')).toBeEnabled();
  await expect(tutarKutusu(kartFormu(page))).toHaveValue('1.900,00');
  await kartFormu(page).getByTestId('tahsilat-Banka').click();
  await expect
    .poll(() =>
      tahsilatlar(finansIstekleri).map((k) => [
        k.govde['tahsilatAnahtar'],
        k.govde['hesap'],
        k.govde['tutar'],
      ]),
    )
    .toEqual([
      [K1, 'Kasa', '700.00'],
      [K2, 'Banka', '1900.00'],
    ]);
  expect(hatalar).toEqual([]);
});

test("#318 T5/L2: Kart'a yazılan 600 + Nakit 700 2xx → Kart YENİ anahtarı alır, 600 korunur; tek basışta 409'suz yazılır", async ({
  page,
}) => {
  await oturumAc(page);
  let nakit = false;
  const { finansIstekleri } = await sahteApi(page, {
    detay: () => (nakit ? detay(K2, 1900, 1700, 'v2') : detay(K1, 2600, 1000)),
    finans: (route, istek) => {
      const g = istek.postDataJSON() as Record<string, unknown>;
      if (g['hesap'] === 'Kasa') nakit = true;
      else if (g['tahsilatAnahtar'] === K1)
        return problem(route, 409, 'mukerrer', 'Bayat anahtar.', {
          mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 700, doviz: 'TRY', ayniIcerik: false },
        });
      return route.fulfill({ json: { id: g['hesap'] === 'Kasa' ? 'c1' : 'c2' } });
    },
  });
  await page.goto(SAYFA);
  await sekme(page, 'Kart/Havale');
  await tutarKutusu(kartFormu(page)).fill('600');
  await sekme(page, 'Nakit');
  await tutarKutusu(nakitFormu(page)).fill('700');
  await nakitFormu(page).getByTestId('tahsilat-Kasa').click();
  await expect(panel(page).getByTestId('finans-kalan')).toContainText('1.900,00');
  await sekme(page, 'Kart/Havale');
  await expect(tutarKutusu(kartFormu(page))).toHaveValue(/^600(,00)?$/);
  await kartFormu(page).getByTestId('tahsilat-Banka').click();
  await expect
    .poll(() =>
      tahsilatlar(finansIstekleri).map((k) => [
        k.govde['tahsilatAnahtar'],
        k.govde['hesap'],
        k.govde['tutar'],
      ]),
    )
    .toEqual([
      [K1, 'Kasa', '700.00'],
      [K2, 'Banka', '600.00'],
    ]);
  await expect(toastlar(page)).not.toContainText('Bayat anahtar');
});

/** Detay GET'i: `durum()` null → normal (sahteApi'ye düşer), sayı → o durumla hata; `gecikme()` ms bekletir. */
async function detayKontrol(
  page: Page,
  durum: () => number | null,
  gecikme: () => number = () => 0,
): Promise<void> {
  await page.route(
    (url) => url.pathname === `/api/ui/v1/kiralar/${KIRA_ID}`,
    async (route) => {
      if (route.request().method() !== 'GET') return route.fallback();
      const s = durum(); // istek anındaki mod (gecikme sırasında mod değişse de bu isteğin sonucu sabit)
      const ms = gecikme();
      if (ms) await new Promise((ok) => setTimeout(ok, ms));
      if (s === null) return route.fallback();
      const kod =
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
        json: { status: s, detail: `hata ${s}`, ...(kod ? { kod } : {}) },
      });
    },
  );
}

/** Tahsilat istekleri "hesap:anahtar:tutar" (K1/K2 adlarıyla). */
const anahtarlar = (k: readonly Kayit[]) =>
  tahsilatlar(k).map(
    (x) =>
      `${String(x.govde['hesap'])}:${x.govde['tahsilatAnahtar'] === K1 ? 'K1' : x.govde['tahsilatAnahtar'] === K2 ? 'K2' : '?'}:${String(x.govde['tutar'])}`,
  );

/** Uygulama içi başka rotaya gidip geri döner (kira sekmesi yaşar; dönüşte detay yeniden okunur). */
async function gitVeDon(page: Page): Promise<void> {
  await page.evaluate(() => {
    history.pushState({}, '', '/app/');
    dispatchEvent(new PopStateEvent('popstate', { state: {} }));
  });
  await expect(panel(page)).toBeHidden();
  await page.goBack();
  await expect(page).toHaveURL(new RegExp(KIRA_ID));
}

for (const kartYazildi of [true, false]) {
  test(`#318 B1: Kart sonucu belirsiz (K1) → Nakit sonuçlanır (${kartYazildi ? '409' : '2xx'}) → tazeleme → Kart tekrarı YİNE K1 (donmuş anahtar değişmez)`, async ({
    page,
  }) => {
    const hatalar = hatalariTopla(page, [...AG_HATASI, /ERR_CONNECTION_RESET|net::/]);
    await oturumAc(page);
    let n = 0;
    let sonuc = false;
    const { finansIstekleri } = await sahteApi(page, {
      detay: () => (sonuc ? detay(K2, 1900, 1700, 'v2') : detay(K1, 2600, 1000)),
      finans: (route, istek) => {
        const g = istek.postDataJSON() as Record<string, unknown>;
        if (++n === 1) return route.abort('connectionreset'); // Kart 600 K1: sonuç belirsiz
        if (g['hesap'] === 'Kasa') {
          sonuc = true;
          if (kartYazildi)
            return problem(route, 409, 'mukerrer', 'Başka tahsilat yazıldı.', {
              mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 600, doviz: 'TRY', ayniIcerik: false },
            });
          return route.fulfill({ json: { id: 'c2' } });
        }
        return problem(route, 409, 'mukerrer', 'K1 ile kayıt var.', {
          mevcut: {
            id: kartYazildi ? 'c1' : 'c2',
            belgeNo: 'T-1',
            tutar: kartYazildi ? 600 : 700,
            doviz: 'TRY',
            ayniIcerik: kartYazildi,
          },
        });
      },
    });
    await page.goto(SAYFA);
    await sekme(page, 'Kart/Havale');
    await tutarKutusu(kartFormu(page)).fill('600');
    await kartFormu(page).getByTestId('tahsilat-Banka').click();
    await expect(toastlar(page)).toContainText('Sunucuya ulaşılamadı');
    await sekme(page, 'Nakit');
    await tutarKutusu(nakitFormu(page)).fill('700');
    await nakitFormu(page).getByTestId('tahsilat-Kasa').click();
    await expect(panel(page).getByTestId('finans-kalan')).toContainText('1.900,00');
    await sekme(page, 'Kart/Havale');
    await expect(kartFormu(page).getByTestId('tahsilat-Banka')).toBeEnabled();
    await kartFormu(page).getByTestId('tahsilat-Banka').click();
    await expect
      .poll(() => anahtarlar(finansIstekleri))
      .toEqual(['Banka:K1:600.00', 'Kasa:K1:700.00', 'Banka:K1:600.00']);
    expect(hatalar).toEqual([]);
  });
}

test('#318 B3/L1: Nakit donmuş (K1) + Kart 2xx → tazeleme 503 → iki form pasif + Yeniden yükle → Nakit tekrarı YİNE K1', async ({
  page,
}) => {
  await oturumAc(page);
  let n = 0;
  let kartOk = false;
  let bozuk = true;
  const { finansIstekleri } = await sahteApi(page, {
    detay: () => (kartOk ? detay(K2, 2000, 1600, 'v2') : detay(K1, 2600, 1000)),
    finans: (route, istek) => {
      const g = istek.postDataJSON() as Record<string, unknown>;
      if (++n === 1) return route.abort('connectionreset');
      if (g['hesap'] === 'Banka') {
        kartOk = true;
        return route.fulfill({ json: { id: 'k1' } });
      }
      return problem(route, 409, 'mukerrer', 'Başka tahsilat yazıldı.', {
        mevcut: { id: 'k1', belgeNo: 'T-9', tutar: 600, doviz: 'TRY', ayniIcerik: false },
      });
    },
  });
  await detayKontrol(page, () => (kartOk && bozuk ? 503 : null));
  await page.goto(SAYFA);
  await tutarKutusu(nakitFormu(page)).fill('500');
  await nakitFormu(page).getByTestId('tahsilat-Kasa').click();
  await expect(toastlar(page)).toContainText('Sunucuya ulaşılamadı');
  await sekme(page, 'Kart/Havale');
  await tutarKutusu(kartFormu(page)).fill('600');
  await kartFormu(page).getByTestId('tahsilat-Banka').click();
  await expect(toastlar(page)).toContainText('Tahsilat kaydedildi');
  await expect(kartFormu(page).getByTestId('tahsilat-yeniden-yukle-Banka')).toBeVisible();
  await expect(kartFormu(page).getByTestId('tahsilat-Banka')).toBeDisabled();
  await sekme(page, 'Nakit');
  await expect(nakitFormu(page).getByTestId('tahsilat-Kasa')).toBeDisabled();
  await expect(page.locator('.kf-tazeleme-hatasi')).toBeVisible();

  bozuk = false;
  await nakitFormu(page).getByTestId('tahsilat-yeniden-yukle-Kasa').click();
  await expect(nakitFormu(page).getByTestId('tahsilat-yeniden-yukle-Kasa')).toHaveCount(0);
  await expect(nakitFormu(page).getByTestId('tahsilat-Kasa')).toBeEnabled();
  await expect(tutarKutusu(nakitFormu(page))).toHaveValue('500,00'); // donmuş deneme korundu
  await nakitFormu(page).getByTestId('tahsilat-Kasa').click();
  await expect
    .poll(() => anahtarlar(finansIstekleri))
    .toEqual(['Kasa:K1:500.00', 'Banka:K1:600.00', 'Kasa:K1:500.00']);
});

for (const [durum, gecici] of [
  [404, false],
  [403, false],
  [400, false],
  [429, true],
] as const) {
  test(`#318 B4: 503 → Yeniden dene sürerken panel ve donmuş Nakit korunur; ardından ${durum} → ${gecici ? 'GEÇİCİ, son iyi veri kalır' : 'kesin, eski veri YOK'}`, async ({
    page,
  }) => {
    await oturumAc(page);
    let mod: 'normal' | '503' | 'yavas' | 'son' = 'normal';
    const { finansIstekleri } = await sahteApi(page, {
      finans: (route) => route.abort('connectionreset'),
    });
    await detayKontrol(
      page,
      () => (mod === '503' ? 503 : mod === 'son' ? durum : null),
      () => (mod === 'yavas' ? 1500 : 0),
    );
    await page.goto(SAYFA);
    await tutarKutusu(nakitFormu(page)).fill('500');
    await nakitFormu(page).getByTestId('tahsilat-Kasa').click();
    await expect(toastlar(page)).toContainText('Sunucuya ulaşılamadı');

    mod = '503';
    await gitVeDon(page);
    const bant = page.locator('.kf-tazeleme-hatasi');
    await expect(bant).toBeVisible();
    mod = 'yavas';
    const yavasOkuma = page.waitForResponse(
      (r) => new URL(r.url()).pathname === `/api/ui/v1/kiralar/${KIRA_ID}`,
    );
    await bant.getByRole('button').click();
    // Yeniden okuma sürerken: sayfa iskelete düşmez, panel yeniden kurulmaz, donmuş 500 durur.
    await expect(bant).toHaveCount(0);
    await expect(panel(page)).toHaveCount(1);
    await expect(tutarKutusu(nakitFormu(page))).toHaveValue('500,00');
    await expect(panel(page).getByTestId('finans-kalan')).toContainText('2.600,00');
    await yavasOkuma;

    mod = 'son';
    await gitVeDon(page);
    if (gecici) {
      await expect(bant).toBeVisible();
      await expect(panel(page)).toHaveCount(1);
      await expect(tutarKutusu(nakitFormu(page))).toHaveValue('500,00');
    } else {
      await expect(panel(page)).toHaveCount(0);
    }
    expect(anahtarlar(finansIstekleri)).toEqual(['Kasa:K1:500.00']);
  });
}

test('L3 metni: sonucu bilinmeyen tahsilat varken sekmeyi kapatmak özel uyarıyla sorulur', async ({
  page,
}) => {
  await oturumAc(page);
  await sahteApi(page, { finans: (r) => r.abort('connectionreset') });
  await page.goto(SAYFA);
  await expect(panel(page).getByRole('textbox', { name: 'Tutar', exact: true })).toHaveValue(
    '2.600,00',
  );
  await panel(page).getByTestId('tahsilat-Kasa').click();
  await expect(toastlar(page)).toContainText('Sunucuya ulaşılamadı');
  await page.getByRole('button', { name: /Kira 2026220901001 sekmesini kapat/ }).click();
  const onay = page.getByRole('alertdialog');
  await expect(onay).toContainText('Sonucu bilinmeyen bir tahsilat/ödeme var');
  await onay.getByRole('button', { name: 'Sayfada kal' }).click();
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
  const kart = p.getByTestId('tahsilat-formu-Banka');
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
