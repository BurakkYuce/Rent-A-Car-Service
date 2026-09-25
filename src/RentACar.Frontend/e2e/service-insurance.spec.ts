import { expect, test, type Page } from '@playwright/test';

import { BEN, ciddiIhlaller, hatalariTopla, oturumAc, problem, xsrfYaz } from './ortak';
import {
  INSPECTION_1,
  MTV_1,
  POLICY_1,
  SERVICE_1,
  mtvDetail,
  page1,
  rateCard,
  serviceDetail,
  serviceInsuranceEndpoints,
} from './service-insurance-fakes';
import { hazirBekle, tasmaOlc, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F9.2 servis / sigorta / vade / fiyat-tarife ekranları. Üç zorunlu senaryo PARA formunda (MTV ödemesi): doğrulama
 * hatasında form korunur, oturum düşünce AYNI istek aynı anahtarla, `cakisma` formu silmez; + kaybolan yanıttan sonra
 * aynı anahtar/gövdeyle tek ödeme, tanım ekranında `cakisma` birleştirmesi, çift tıklamada tek kalem; axe iki tema +
 * 320/390/768/1440 taşma.
 */
const AG_HATASI = [
  /Failed to load resource: the server responded with a status of 4\d\d/,
  /Failed to load resource: net::ERR_FAILED/,
];
const ADMIN = { ...BEN, izinler: [...BEN.izinler, 'ManageUsers', 'OperationsDelete'] };

const P = (
  ad: string,
  yol: string,
  baslik: string,
  hazir?: VitrinSayfasi['hazir'],
): VitrinSayfasi => ({
  ad,
  yol,
  baslik,
  ...(hazir ? { hazir } : {}),
});
const grid = (text: string) => async (page: Page) => {
  await expect(page.getByRole('gridcell', { name: text }).first()).toBeVisible();
};

const PAGES: readonly VitrinSayfasi[] = [
  P('servisler', '/app/servisler', 'Servis / Bakım', grid('SR-000001')),
  P('servis-kayit', `/app/servisler/${SERVICE_1}`, 'Servis SR-000001'),
  P('regulasyon', '/app/regulasyon', 'Sigorta', grid('34 ABC 123')),
  P('police', `/app/regulasyon/sigortalar/${POLICY_1}`, 'Poliçe 34ABC123 Kasko'),
  P('mtv', '/app/regulasyon/mtv', 'MTV', grid('2026-1')),
  P('mtv-kayit', `/app/regulasyon/mtv/${MTV_1}`, 'MTV 34ABC123 2026-1'),
  P('muayene', '/app/regulasyon/muayene', 'Muayene', grid('34 ABC 123')),
  P('muayene-kayit', `/app/regulasyon/muayeneler/${INSPECTION_1}`, 'Muayene 34ABC123'),
  P('vade', '/app/vade', 'Vade Uyarıları', grid('Kasko')),
  P('tarifeler', '/app/tarifeler', 'Tarife Yönetimi', grid('B-STD')),
  P('tarife-gruplari', '/app/tarife-gruplari', 'Tarife (Fiyat) Grupları'),
  P('sigorta-urunleri', '/app/sigorta-urunleri', 'Sigorta & Ek Hizmet Ürün Kataloğu'),
  P('ek-hizmetler', '/app/ek-hizmetler', 'Ek Hizmet Tanımları'),
  P('tarife-matris', '/app/tarife-matris', 'Tarife Matrisi'),
  P('kira-kurallari', '/app/kira-kurallari', 'Kiralama Kuralları (Promosyon / Şart)'),
  P('broker-yasaklari', '/app/broker-yasaklari', 'Broker / Kaynak Satış Yasakları'),
  P('servis-tanimlari', '/app/servis-tanimlari', 'Periyodik Bakım Tanım Tablosu', async (page) => {
    await expect(page.getByText('Renault · Clio · Dizel · Manuel')).toBeVisible();
  }),
  P('fiyat-hesapla', '/app/fiyat-hesapla', 'Fiyat Motoru — Kira Teklifi Hesapla'),
  P('maliyet-hesapla', '/app/maliyet-hesapla', 'Filo / Uzun Dönem Maliyet Hesaplayıcı'),
  P('maliyet-teklifleri', '/app/maliyet-teklifleri', 'Kayıtlı Maliyet Teklifleri', grid('X Filo')),
  P('tarife-aktar', '/app/tarife-aktar', 'Tarife İçe Aktar (Toplu Fiyat)'),
];

test.beforeEach(async ({ page }) => {
  await oturumAc(page, ADMIN);
});

// Sayfa başına ayrı test (#305 deseni): tek testte 21 sayfa × 2 tema axe taraması CI'da 30 sn sınırına dayanıyordu.
for (const s of PAGES) {
  test(`${s.ad}: içerik + axe iki tema, konsol hatası yok`, async ({ page }) => {
    const hatalar = hatalariTopla(page, AG_HATASI);
    await serviceInsuranceEndpoints(page);
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto(s.yol);
    await hazirBekle(page, s);
    expect(await ciddiIhlaller(page), `${s.ad} açık`).toEqual([]);
    await page.emulateMedia({ colorScheme: 'dark' });
    expect(await ciddiIhlaller(page), `${s.ad} koyu`).toEqual([]);
    expect(hatalar).toEqual([]);
  });
}

async function payForm(page: Page) {
  await page.goto(`/app/regulasyon/mtv/${MTV_1}`);
  await hazirBekle(page, PAGES[5]!);
  return page.getByRole('region', { name: 'Öde' });
}

test('MTV ödemesi: doğrulama hatasında form korunur, düzeltilen gövde AYNI anahtarla gider', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let n = 0;
  const written = await serviceInsuranceEndpoints(page, {
    write: async (r, path) => {
      if (!path.endsWith('/odeme')) return false;
      if (++n === 1)
        await problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
          errors: { odemeTarihi: ['MTV ödeme tarihi gelecekte olamaz.'] },
        });
      else
        await r.fulfill({
          json: { odemeId: 'o2', sira: 2, tutar: 400, kalan: 600, odendi: false },
        });
      return true;
    },
  });
  const form = await payForm(page);
  const amount = form.getByRole('textbox', { name: 'Ödeme tutarı' });
  await amount.fill('400,555');
  await form.getByRole('button', { name: 'Öde' }).click();
  await expect(form.getByText('En fazla 2 ondalık hane girilebilir.')).toBeVisible();
  expect(written).toHaveLength(0);

  await amount.fill('400');
  await form.getByRole('textbox', { name: 'Evrak No' }).fill('EV-2');
  await form.getByRole('button', { name: 'Öde' }).click();
  await expect(form.getByText('MTV ödeme tarihi gelecekte olamaz.')).toBeVisible();
  await expect(amount).toHaveValue('400,00');
  await expect(form.getByRole('textbox', { name: 'Evrak No' })).toHaveValue('EV-2');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    hesap: 'Kasa',
    tutar: '400.00',
    beklenenKalan: 1000,
    evrakNo: 'EV-2',
  });
  expect(written[0]?.anahtar).toBeTruthy();

  await form.getByRole('button', { name: 'Öde' }).click();
  await expect(page.getByText('2. ödeme kaydedildi (400,00 ₺); kalan 600,00 ₺.')).toBeVisible();
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(await ciddiIhlaller(page)).toEqual([]);
  expect(hatalar).toEqual([]);
});

test('MTV ödemesi: oturum düşünce form kaybolmaz — yerinde giriş, AYNI istek (aynı anahtar + gövde)', async ({
  page,
}) => {
  let n = 0;
  const written = await serviceInsuranceEndpoints(page, {
    write: async (r, path) => {
      if (!path.endsWith('/odeme')) return false;
      if (++n === 1) await problem(r, 401, 'oturum_yok', 'Oturum açık değil.');
      else
        await r.fulfill({
          json: { odemeId: 'o2', sira: 2, tutar: 250.5, kalan: 749.5, odendi: false },
        });
      return true;
    },
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await xsrfYaz(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await xsrfYaz(page, 'yeni-belirtec');
    return route.fulfill({ json: ADMIN });
  });
  const form = await payForm(page);
  await form.getByRole('combobox', { name: 'Hesap', exact: true }).selectOption('Banka');
  await form.getByRole('textbox', { name: 'Ödeme tutarı' }).fill('250,50');
  await form.getByRole('button', { name: 'Öde' }).click();

  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(
    page.getByRole('textbox', { name: 'Ödeme tutarı', includeHidden: true }),
  ).toHaveValue('250,50');
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByText('2. ödeme kaydedildi (250,50 ₺); kalan 749,50 ₺.')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ hesap: 'Banka', tutar: '250.50' });
});

test('MTV ödemesi: cakisma formu silmez — kalan yenilenir, sonraki gönderim yeni kalanla', async ({
  page,
}) => {
  let kalan = 1000;
  let n = 0;
  const written = await serviceInsuranceEndpoints(page, {
    mtv: () => mtvDetail(kalan),
    write: async (r, path) => {
      if (!path.endsWith('/odeme')) return false;
      if (++n === 1) {
        kalan = 700; // başka sekme 300 ödedi
        await problem(
          r,
          409,
          'cakisma',
          'Kalan bu ekran açıldıktan sonra değişti; hiçbir şey yazılmadı.',
        );
      } else
        await r.fulfill({
          json: { odemeId: 'o3', sira: 3, tutar: 200, kalan: 500, odendi: false },
        });
      return true;
    },
  });
  const form = await payForm(page);
  const amount = form.getByRole('textbox', { name: 'Ödeme tutarı' });
  await amount.fill('200');
  await form.getByRole('textbox', { name: 'Evrak No' }).fill('EV-9');
  await form.getByRole('button', { name: 'Öde' }).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText(
    'Kalan bu ekran açıldıktan sonra değişti',
  );
  await expect(page.getByText('Kalan: 700,00 ₺')).toBeVisible();
  await expect(amount).toHaveValue('200,00'); // form SİLİNMEDİ
  await expect(form.getByRole('textbox', { name: 'Evrak No' })).toHaveValue('EV-9');
  await form.getByRole('button', { name: 'Öde' }).click();
  await expect(page.getByText('3. ödeme kaydedildi (200,00 ₺); kalan 500,00 ₺.')).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    beklenenKalan: 1000,
    tutar: '200.00',
  });
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({
    beklenenKalan: 700,
    tutar: '200.00',
  });
  expect(written[1]?.anahtar).not.toBe(written[0]?.anahtar); // kesin 409: yeni işlem
});

test('MTV ödemesi: kaybolan yanıt → donmuş kopya AYNI anahtar + gövdeyle → sunucu "zaten kaydedildi", tek ödeme', async ({
  page,
}) => {
  let payments = 0;
  const written = await serviceInsuranceEndpoints(page, {
    mtv: () => mtvDetail(payments === 0 ? 1000 : 600),
    write: async (r, path) => {
      if (!path.endsWith('/odeme')) return false;
      if (payments === 0) {
        payments = 1; // sunucu YAZDI, yanıt kayboldu
        await r.abort('failed');
        return true;
      }
      await problem(
        r,
        409,
        'mukerrer',
        'Bu ödeme zaten kaydedildi (MTV 2026-1 #2, 400,00 TRY); yeni ödeme yazılmadı.',
        {
          mevcut: {
            id: 'o2',
            belgeNo: 'MTV 2026-1 #2',
            tutar: 400,
            doviz: 'TRY',
            ayniIcerik: true,
          },
        },
      );
      return true;
    },
  });
  const form = await payForm(page);
  await form.getByRole('textbox', { name: 'Ödeme tutarı' }).fill('400');
  await form.getByRole('button', { name: 'Öde' }).click();
  await expect(form.getByRole('alert')).toContainText('sonucu bilinmiyor');
  await expect(form.getByRole('textbox', { name: 'Ödeme tutarı' })).toBeDisabled();
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect(page.getByText('Bu ödeme zaten kaydedildi (MTV 2026-1 #2')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(payments).toBe(1);
  await expect(page.getByText('Kalan: 600,00 ₺')).toBeVisible();
});

test('tarife düzenleme: cakisma formu silmez — güncel kayıt birleşir, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let surum = 'rc-1';
  let extra: Record<string, unknown> = {};
  let put = 0;
  const written = await serviceInsuranceEndpoints(page, {
    rate: () => rateCard(surum, extra),
    write: async (r, _path, method) => {
      if (method !== 'PUT') return false;
      if (++put === 1) {
        surum = 'rc-2';
        extra = { maxGun: 30 }; // başka oturum max günü değiştirdi
        await problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti; güncel hâli yükleyin.');
      } else await r.fulfill({ json: rateCard('rc-3', { ad: 'Yeni ad', maxGun: 30 }) });
      return true;
    },
  });
  await page.goto('/app/tarifeler');
  await hazirBekle(page, PAGES[9]!);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const editor = page.getByRole('region', { name: 'Tarife Düzenle' });
  const name = editor.getByRole('textbox', { name: 'Ad', exact: true });
  await expect(name).toHaveValue('B Grubu Standart');
  await expect(editor.getByRole('button', { name: 'Kaydet' })).toBeEnabled();
  await name.fill('Yeni ad');
  await editor.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt siz düzenlerken değişti');
  await expect(name).toHaveValue('Yeni ad'); // form SİLİNMEDİ
  await expect(editor.getByRole('textbox', { name: 'Max gün' })).toHaveValue('30');
  await editor.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('B-STD kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    surum: 'rc-1',
    ad: 'Yeni ad',
    gunlukUcret: '1250.5000',
    gecerliBas: '2026-09-30T21:00:00Z',
  });
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({
    surum: 'rc-2',
    ad: 'Yeni ad',
    maxGun: 30,
  });
  expect(hatalar).toEqual([]);
});

test('servis kalemi: çift tıklamada tek istek, Idempotency-Key gönderilir; toplamlar sunucudan', async ({
  page,
}) => {
  let lines = 1;
  const written = await serviceInsuranceEndpoints(page, {
    service: () => serviceDetail(`sv-${lines}`, lines),
    write: async (r, path) => {
      if (!path.endsWith('/kalemler')) return false;
      await new Promise((ok) => setTimeout(ok, 400));
      lines = 2;
      await r.fulfill({ json: serviceDetail('sv-2', 2) });
      return true;
    },
  });
  await page.goto(`/app/servisler/${SERVICE_1}`);
  await hazirBekle(page, PAGES[1]!);
  const section = page.getByRole('region', { name: 'Kalemler' });
  await section.getByRole('textbox', { name: 'Kalem', exact: true }).fill('Yağ');
  await section.getByRole('textbox', { name: 'Birim fiyat' }).fill('100');
  await section.getByRole('textbox', { name: 'Miktar' }).fill('2');
  await section.getByRole('button', { name: 'Kalem Ekle' }).dblclick();
  await expect(page.getByText('Kalem eklendi.')).toBeVisible();
  expect(written).toHaveLength(1);
  expect(written[0]?.anahtar).toBeTruthy();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toEqual({
    aciklama: 'Yağ',
    birimFiyat: '100.00',
    miktar: 2,
    indirim: null,
    kdvOran: null,
    tutar: null,
  });
  await expect(section.getByRole('cell', { name: '480,00 ₺' })).toBeVisible(); // sunucu genel toplamı (2 × 240)
});

// ---- #301 bağımsız inceleme bulguları (M1–M4, L1, P5) — kalıcı kilit
const wait = (ms: number) => new Promise((ok) => setTimeout(ok, ms));

test('M1 MTV: istek uçarken form KİLİTLİ; donmuş kopya ekranda gönderilenle aynı, tekrar aynı gövdeyle', async ({
  page,
}) => {
  let n = 0;
  const written = await serviceInsuranceEndpoints(page, {
    write: async (r, path) => {
      if (!path.endsWith('/odeme')) return false;
      if (++n === 1) {
        await wait(1200);
        await r.abort('failed');
      } else
        await r.fulfill({
          json: { odemeId: 'o2', sira: 2, tutar: 500, kalan: 500, odendi: false },
        });
      return true;
    },
  });
  const form = await payForm(page);
  const amount = form.getByRole('textbox', { name: 'Ödeme tutarı' });
  await amount.fill('500');
  await form.getByRole('button', { name: 'Öde' }).click();
  await expect(form.getByRole('button', { name: 'Gönderiliyor…' })).toBeVisible();
  await expect(amount).toBeDisabled(); // uçuşta düzenlenemez
  await expect(form.getByRole('alert')).toContainText('sonucu bilinmiyor');
  await expect(amount).toHaveValue('500,00');
  await expect(amount).toBeDisabled();
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect(page.getByText('2. ödeme kaydedildi (500,00 ₺); kalan 500,00 ₺.')).toBeVisible();
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
});

test('M2 maliyet: hesap uçarken girdi değişirse sonuç BAYAT kalır, Kaydet kapalı; yeniden hesapla açılır', async ({
  page,
}) => {
  await serviceInsuranceEndpoints(page, {
    write: async (_r, path) => {
      if (path === '/api/ui/v1/maliyet-hesapla') await wait(1200);
      return false; // varsayılan sabit sonuç
    },
  });
  await page.goto('/app/maliyet-hesapla');
  const alis = page.getByRole('textbox', { name: 'Alış bedeli (net)' });
  await alis.fill('1000000');
  await page.getByRole('button', { name: 'Hesapla', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Gönderiliyor…' })).toBeVisible();
  await alis.fill('2000000');
  await expect(page.getByRole('heading', { name: 'Sonuç (araç başına)' })).toBeVisible();
  await expect(page.getByText('Girdi hesaplamadan sonra değişti')).toBeVisible();
  const save = page.getByRole('button', { name: 'Teklifi Kaydet' });
  await expect(save).toBeDisabled();
  await page.getByRole('button', { name: 'Hesapla', exact: true }).click();
  await expect(page.getByText('Girdi hesaplamadan sonra değişti')).toHaveCount(0);
  await expect(save).toBeEnabled();
});

test('M3 servis kalemi: kendi yazılmış denemesi NET tutarla tanınır, form tamamen temizlenir, ikinci kalem gitmez', async ({
  page,
}) => {
  let n = 0;
  const written = await serviceInsuranceEndpoints(page, {
    write: async (r, path) => {
      if (!path.endsWith('/kalemler')) return false;
      n++;
      if (n === 1)
        await r.abort('failed'); // sunucu 100 × 2 = 200 net YAZDI, yanıt kayboldu
      else if (n === 2) await problem(r, 429, 'cok_istek', 'Çok fazla istek.');
      else
        await problem(
          r,
          409,
          'mukerrer',
          'Bu işlem anahtarıyla başka içerikte bir kalem eklenmiş; girdiğiniz kalem YAZILMADI. Kaydı kontrol edin.',
          { mevcut: { id: 'l2', belgeNo: 'Yağ', tutar: 200, doviz: 'TRY', ayniIcerik: false } },
        );
      return true;
    },
  });
  await page.goto(`/app/servisler/${SERVICE_1}`);
  await hazirBekle(page, PAGES[1]!);
  const section = page.getByRole('region', { name: 'Kalemler' });
  await section.getByRole('textbox', { name: 'Kalem', exact: true }).fill('Yağ');
  await section.getByRole('textbox', { name: 'Birim fiyat' }).fill('100');
  await section.getByRole('textbox', { name: 'Miktar' }).fill('2');
  await section.getByRole('button', { name: 'Kalem Ekle' }).click();
  await section.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click(); // 429 → kesin red
  await expect(section.getByRole('textbox', { name: 'Miktar' })).toBeEnabled();
  await section.getByRole('textbox', { name: 'Miktar' }).fill('3');
  await section.getByRole('button', { name: 'Kalem Ekle' }).click(); // aynı anahtar, farklı gövde → 409
  await expect(page.getByText('Önceki denemeniz kaydedilmiş — yeni tutar yazılmadı')).toBeVisible();
  await expect(page.getByText(/girdiğiniz 300,00 ₺ YAZILMADI/)).toBeVisible(); // girilen = satır toplamı
  expect(written[2]?.anahtar).toBe(written[0]?.anahtar);
  await expect(section.getByRole('textbox', { name: 'Birim fiyat' })).toHaveValue('');
  await expect(section.getByRole('textbox', { name: 'Miktar' })).toHaveValue('');
  await section.getByRole('button', { name: 'Kalem Ekle' }).click(); // boş form: istek gitmez
  await wait(300);
  expect(written).toHaveLength(3);
});

test('M4 muayene: "zaten kaydedildi" sonrası form (ceza dahil) sıfırlanır; açık tutar girilmeden ödeme gitmez', async ({
  page,
}) => {
  let n = 0;
  const written = await serviceInsuranceEndpoints(page, {
    write: async (r, path) => {
      if (!path.endsWith('/odeme')) return false;
      n++;
      if (n === 1)
        await r.abort('failed'); // 400 + ceza 100 YAZILDI, yanıt kayboldu
      else if (n === 2)
        await problem(
          r,
          409,
          'mukerrer',
          'Bu ödeme zaten kaydedildi (Muayene 10.09.2026 #1, 400,00 TRY); yeni ödeme yazılmadı.',
          {
            mevcut: {
              id: 'o1',
              belgeNo: 'Muayene 10.09.2026 #1',
              tutar: 400,
              doviz: 'TRY',
              ayniIcerik: true,
            },
          },
        );
      else
        await r.fulfill({
          json: { odemeId: 'o2', sira: 2, tutar: 200, kalan: 300, odendi: false },
        });
      return true;
    },
  });
  await page.goto(`/app/regulasyon/muayeneler/${INSPECTION_1}`);
  await hazirBekle(page, PAGES[7]!);
  const form = page.getByRole('region', { name: 'Öde' });
  await form.getByRole('combobox', { name: 'Hesap', exact: true }).selectOption('Banka');
  await form.getByRole('textbox', { name: 'Ödeme tutarı' }).fill('400');
  await form.getByRole('textbox', { name: 'Ceza' }).fill('100');
  await form.getByRole('button', { name: 'Öde' }).click();
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect(page.getByText('Bu ödeme zaten kaydedildi').first()).toBeVisible();
  await expect(form.getByRole('textbox', { name: 'Ödeme tutarı' })).toHaveValue('');
  await expect(form.getByRole('textbox', { name: 'Ceza' })).toHaveValue('');
  await expect(
    form.getByRole('combobox', { name: 'Hesap', exact: true }).locator('option:checked'),
  ).toHaveText('Banka'); // hesap korunur
  await expect(form.getByText('Boş tutar kalanın tamamını öder')).toBeVisible();
  await form.getByRole('button', { name: 'Öde' }).click(); // boş tutar: gönderim kilitli
  await wait(300);
  expect(written).toHaveLength(2);
  // Yazıp silmek kilidi AÇMAZ (yeniden inceleme L-new): boş tutar hâlâ "kalanın tamamı" demek.
  await form.getByRole('textbox', { name: 'Ödeme tutarı' }).fill('5');
  await form.getByRole('textbox', { name: 'Ödeme tutarı' }).fill('');
  await form.getByRole('button', { name: 'Öde' }).click();
  await wait(300);
  expect(written).toHaveLength(2);
  await expect(form.getByText('Boş tutar kalanın tamamını öder')).toBeVisible();
  await form.getByRole('textbox', { name: 'Ödeme tutarı' }).fill('200');
  await form.getByRole('button', { name: 'Öde' }).click();
  await expect(page.getByText('2. ödeme kaydedildi (200,00 ₺); kalan 300,00 ₺.')).toBeVisible();
  expect(JSON.parse(written[2]?.govde ?? '{}')).toMatchObject({
    tutar: '200.00',
    ceza: null,
    hesap: 'Banka',
  });
  expect(written[2]?.anahtar).not.toBe(written[0]?.anahtar);
});

test('L1 tarife aktar: süzgeç değişip liste yüklenirken kanal sil KAPALI; onay yeni kanalın sayısını gösterir', async ({
  page,
}) => {
  await serviceInsuranceEndpoints(page);
  await page.route(
    (u) => u.pathname === '/api/ui/v1/tarife-aktar',
    async (r) => {
      const kanal = new URL(r.request().url()).searchParams.get('kanal');
      if (kanal === 'B') await wait(1500);
      await r.fulfill({
        json: { satirlar: page1([]), bekleyen: 0, onayli: 0, silinecek: kanal === 'B' ? 7 : 2 },
      });
    },
  );
  await page.goto('/app/tarife-aktar?kanal=A');
  const button = page.getByRole('button', { name: /bekleyen satır/ });
  await expect(button).toContainText('(2 bekleyen');
  await page.getByRole('textbox', { name: 'Kanal' }).fill('B');
  await page.getByRole('button', { name: 'Filtrele', exact: true }).click();
  await expect(button).toBeDisabled();
  await expect(button).toContainText('(7 bekleyen');
  await expect(button).toBeEnabled();
  await button.click();
  await expect(page.getByRole('alertdialog')).toContainText(
    "'B' kaynağının TÜM ŞUBELERİNDEKİ 7 BEKLEYEN",
  );
});

test('P5 tarife: 4 haneli fiyat dokunmadan (odak alıp bırakınca) kaydedilince kaymaz', async ({
  page,
}) => {
  const written = await serviceInsuranceEndpoints(page, {
    rate: () => rateCard('rc-1', { gunlukUcret: 1250.5678 }),
    write: async (r, _p, method) => {
      if (method !== 'PUT') return false;
      await r.fulfill({ json: rateCard('rc-2', { gunlukUcret: 1250.5678 }) });
      return true;
    },
  });
  await page.goto('/app/tarifeler');
  await hazirBekle(page, PAGES[9]!);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const editor = page.getByRole('region', { name: 'Tarife Düzenle' });
  await expect(editor.getByRole('button', { name: 'Kaydet' })).toBeEnabled();
  await editor.getByRole('textbox', { name: 'Günlük ücret' }).focus();
  await editor.getByRole('textbox', { name: 'Ad', exact: true }).focus();
  await editor.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('B-STD kaydedildi.')).toBeVisible();
  expect(
    Number((JSON.parse(written[0]?.govde ?? '{}') as { gunlukUcret: string }).gunlukUcret),
  ).toBe(1250.5678);
});

for (const s of PAGES) {
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await serviceInsuranceEndpoints(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await hazirBekle(page, s);
        expect(await tasmaOlc(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await serviceInsuranceEndpoints(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await hazirBekle(page, s);
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
