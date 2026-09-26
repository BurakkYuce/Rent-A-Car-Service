import { expect, test, type Page } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, logIn, problem, writeXsrf } from './ortak';
import {
  INSTALLMENT_1,
  LOAN_1,
  ORDER_1,
  financeEndpoints,
  fleetPlan,
  installment,
  loanDetail,
} from './vehicle-finance-fakes';
import { statusRow, vehicleEndpoints } from './vehicle-fakes';
import { waitReady, measureOverflow, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F6.2b araç finans ekranları: kredi (liste + kayıt + TAKSİT ÖDEME), müşteri taksit, sipariş, BAF, hasar, filo plan.
 * Üç zorunlu senaryo (doğrulama hatasında form korunur, oturum düşünce form kaybolmaz, `cakisma` formu silmez) + para
 * (kaybolan yanıttan sonra aynı anahtarla tek ödeme, çift tıklamada tek istek) + axe iki tema + 320/390/768/1440 taşma.
 */
const NETWORK_ERROR = [
  /Failed to load resource: the server responded with a status of 4\d\d/,
  /Failed to load resource: net::ERR_FAILED/,
];

const LOANS: VitrinSayfasi = {
  ad: 'arac-kredi',
  yol: '/app/arac-kredi',
  baslik: 'Araç Kredisi Takip',
  hazir: async (page) => {
    await expect(page.getByRole('link', { name: 'KR-000001' })).toBeVisible();
  },
};
const LOAN: VitrinSayfasi = {
  ad: 'arac-kredi-kayit',
  yol: `/app/arac-kredi/${LOAN_1}`,
  baslik: 'Kredi KR-000001',
  hazir: async (page) => {
    await expect(page.getByText('Sonraki taksit: 4/12')).toBeVisible();
  },
};
const INSTALLMENTS: VitrinSayfasi = {
  ad: 'musteri-taksit',
  yol: '/app/musteri-taksit',
  baslik: 'Müşteri Taksit Takibi',
  hazir: async (page) => {
    await expect(page.getByRole('gridcell', { name: 'Ayşe Yılmaz' })).toBeVisible();
  },
};
const ORDERS: VitrinSayfasi = {
  ad: 'arac-siparis',
  yol: '/app/arac-siparis',
  baslik: 'Araç Sipariş / Tedarik',
  hazir: async (page) => {
    await expect(page.getByRole('link', { name: 'SP-000001' })).toBeVisible();
  },
};
const ORDER: VitrinSayfasi = {
  ad: 'arac-siparis-kayit',
  yol: `/app/arac-siparis/${ORDER_1}`,
  baslik: 'Sipariş SP-000001',
  hazir: async (page) => {
    await expect(page.getByRole('combobox', { name: 'Tedarikçi', exact: true })).toHaveValue(
      'Bayi A',
    );
  },
};
const ORDER_NEW: VitrinSayfasi = {
  ad: 'arac-siparis-yeni',
  yol: '/app/arac-siparis/yeni',
  baslik: 'Yeni Sipariş',
};
const ALLOCATIONS: VitrinSayfasi = {
  ad: 'baf',
  yol: '/app/baf',
  baslik: 'BAF — Personel Araç Tahsis',
  hazir: async (page) => {
    await expect(page.getByRole('gridcell', { name: 'Ali Veli' })).toBeVisible();
  },
};
const DAMAGE: VitrinSayfasi = {
  ad: 'hasar',
  yol: '/app/hasar',
  baslik: 'Hasar Dosyaları',
  hazir: async (page) => {
    await expect(page.getByRole('gridcell', { name: 'HD-000001' })).toBeVisible();
  },
};
const PLAN: VitrinSayfasi = {
  ad: 'filo-plan',
  yol: '/app/filo-plan',
  baslik: 'Filo Plan Yönetimi',
  hazir: async (page) => {
    await expect(page.getByRole('gridcell', { name: 'EKO' })).toBeVisible();
  },
};
const PAGES = [LOANS, LOAN, INSTALLMENTS, ORDERS, ORDER, ORDER_NEW, ALLOCATIONS, DAMAGE, PLAN];

test.beforeEach(async ({ page }) => {
  await logIn(page, { ...BEN, izinler: [...BEN.izinler, 'OperationsDelete'] });
});

// Sayfa başına ayrı test: tek testte tüm sayfalar × 2 tema axe taraması CI'da 30 sn sınırına dayanıyordu.
for (const s of PAGES) {
  test(`${s.ad}: içerik + axe iki tema, konsol hatası yok`, async ({ page }) => {
    const errors = collectErrors(page, NETWORK_ERROR);
    await financeEndpoints(page);
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto(s.yol);
    await waitReady(page, s);
    expect(await seriousViolations(page), `${s.ad} açık`).toEqual([]);
    await page.emulateMedia({ colorScheme: 'dark' });
    expect(await seriousViolations(page), `${s.ad} koyu`).toEqual([]);
    expect(errors).toEqual([]);
  });
}

test('kredi listesi: özet kartlar, sağa yaslı para, dışa aktarma süzgeçle', async ({ page }) => {
  await financeEndpoints(page);
  await page.goto(`${LOANS.yol}?durum=Aktif`);
  await waitReady(page, LOANS);
  await expect(page.getByRole('gridcell', { name: '22.500,00 ₺' })).toBeVisible();
  await expect(page.getByText('Toplam Kredi Borcu').locator('..')).toContainText('22.500,00 ₺');
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/arac-kredileri?format=excel&durumF=Aktif',
  );
});

async function payPanel(page: Page) {
  await page.goto(LOAN.yol);
  await waitReady(page, LOAN);
  return page.getByRole('region', { name: 'Taksit Öde' });
}

test('taksit ödeme: kaybolan yanıt → "tekrar" AYNI anahtar + AYNI gövde → sunucu tek ödeme, zaten kaydedildi', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let payments = 0;
  const written = await financeEndpoints(page, {
    loan: () => loanDetail(3 + payments),
    write: async (r, path) => {
      if (!path.endsWith('/taksit-ode')) return false;
      if (payments === 0) {
        payments = 1; // sunucu YAZDI, yanıt kayboldu
        await r.abort('failed');
        return true;
      }
      await problem(
        r,
        409,
        'mukerrer',
        'Bu taksit ödemesi zaten kaydedildi (Gider No GD-000010, 2.500,00 TRY); yeni ödeme yazılmadı.',
        {
          mevcut: { id: 'g1', belgeNo: 'GD-000010', tutar: 2500, doviz: 'TRY', ayniIcerik: true },
        },
      );
      return true;
    },
  });
  const panel = await payPanel(page);
  await panel.getByRole('combobox', { name: 'Hesap', exact: true }).selectOption('Banka');
  await panel.getByRole('button', { name: 'Taksit Öde' }).click();

  await expect(panel.getByRole('alert')).toContainText('sonucu bilinmiyor');
  // Donmuş kopya: form kilitli, düğme aynı işlemi tekrar gönderir.
  await expect(panel.getByRole('combobox', { name: 'Hesap', exact: true })).toBeDisabled();
  // İnceleme L1: donmuş ödeme varken sekmeyi kapatmak sorulur (kopya kaybolmasın).
  await page
    .getByRole('navigation', { name: 'Açık sekmeler' })
    .getByRole('button', { name: 'Kredi KR-000001 sekmesini kapat' })
    .click();
  const leave = page.getByRole('alertdialog', { name: 'Sayfadan ayrılınsın mı?' });
  await leave.getByRole('button', { name: 'Sayfada kal' }).click();
  await expect(leave).toHaveCount(0);
  await panel.getByRole('button', { name: 'Aynı ödemeyi tekrar gönder' }).click();

  await expect(page.getByText('Bu taksit ödemesi zaten kaydedildi')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toEqual({ sira: 4, hesap: 'Banka', hesapId: null });
  expect(payments).toBe(1);
  // Kayıt yenilendi: 4. taksit ödendi, sonraki 5. — kullanıcı ikinci ödemeye yönlendirilmez.
  await expect(page.getByText('Sonraki taksit: 5/12')).toBeVisible();
  expect(errors.filter((h) => !/Failed to load resource/.test(h))).toEqual([]);
});

test('taksit ödeme: çift tıklamada tek istek; başarıda yeni anahtar', async ({ page }) => {
  let payments = 0;
  const written = await financeEndpoints(page, {
    loan: () => loanDetail(3 + payments),
    write: async (r, path) => {
      if (!path.endsWith('/taksit-ode')) return false;
      await new Promise((ok) => setTimeout(ok, 400));
      payments++;
      await r.fulfill({
        json: {
          giderId: 'g1',
          giderNo: `GD-00001${payments}`,
          sira: 3 + payments,
          tutar: 2500,
          doviz: 'TRY',
          kredi: loanDetail(3 + payments),
        },
      });
      return true;
    },
  });
  const panel = await payPanel(page);
  await panel.getByRole('button', { name: 'Taksit Öde' }).dblclick();
  await expect(page.getByText('4. taksit ödendi (Gider No GD-000011, 2.500,00 ₺).')).toBeVisible();
  await expect(page.getByText('Sonraki taksit: 5/12')).toBeVisible();
  expect(written).toHaveLength(1);

  await panel.getByRole('button', { name: 'Taksit Öde' }).click();
  await expect(page.getByText('Sonraki taksit: 6/12')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).not.toBe(written[0]?.anahtar);
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({ sira: 5 });
});

test('müşteri taksit: doğrulama hatasında form korunur; tr tutar "1.500,50" → 1500.50; 3 ondalık reddedilir', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let n = 0;
  const written = await financeEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/musteri-taksitleri') return false;
      if (++n === 1)
        await problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
          errors: { vade: ['Vade tarihi çok eski.'] },
        });
      else await r.fulfill({ status: 201, json: { id: INSTALLMENT_1 } });
      return true;
    },
  });
  await page.goto(INSTALLMENTS.yol);
  await waitReady(page, INSTALLMENTS);
  const form = page.getByRole('region', { name: 'Tek Taksit Ekle' });
  await form.getByRole('combobox', { name: 'Müşteri' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  await form.getByRole('textbox', { name: 'Vade' }).fill('01.10.2026');
  const amount = form.getByRole('textbox', { name: 'Tutar' });
  await amount.fill('10,555');
  await form.getByRole('button', { name: 'Ekle' }).click();
  await expect(form.getByText('En fazla 2 ondalık hane girilebilir.')).toBeVisible();
  expect(written).toHaveLength(0);

  await amount.fill('1.500,50');
  await form.getByRole('button', { name: 'Ekle' }).click();
  await expect(form.getByText('Vade tarihi çok eski.')).toBeVisible();
  await expect(amount).toHaveValue('1.500,50');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    cariId: 'c0c0c0c0-0000-4000-8000-000000000001',
    taksitTutari: '1500.50',
    durum: 'Bekliyor',
  });
  expect(await seriousViolations(page)).toEqual([]);

  await form.getByRole('button', { name: 'Ekle' }).click();
  await expect(page.getByText('Taksit eklendi.')).toBeVisible();
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar); // ilk istek yazılmadı: aynı işlem
  expect(errors).toEqual([]);
});

test('müşteri taksit planı: oturum düşünce form kaybolmaz — yerinde giriş, AYNI istek (aynı anahtar)', async ({
  page,
}) => {
  let n = 0;
  const written = await financeEndpoints(page, {
    write: async (r, path) => {
      if (!path.endsWith('/plan')) return false;
      if (++n === 1) await problem(r, 401, 'oturum_yok', 'Oturum açık değil.');
      else await r.fulfill({ status: 201, json: { adet: 12, ids: [] } });
      return true;
    },
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await writeXsrf(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await writeXsrf(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });
  await page.goto(INSTALLMENTS.yol);
  await waitReady(page, INSTALLMENTS);
  const form = page.getByRole('region', { name: 'Taksit Planı Üret' });
  await form.getByRole('combobox', { name: 'Müşteri' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  await form.getByRole('textbox', { name: 'Toplam Tutar' }).fill('12.000,01');
  await form.getByRole('textbox', { name: 'İlk Vade' }).fill('01.11.2026');
  await form.getByRole('button', { name: 'Plan Üret' }).click();

  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(
    page.getByRole('textbox', { name: 'Toplam Tutar', includeHidden: true }),
  ).toHaveValue('12.000,01');
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByText('12 taksitlik plan üretildi.')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    toplamTutar: '12000.01',
    taksitSayisi: 12,
    ilkVade: '2026-10-31T21:00:00.000Z',
  });
});

test('müşteri taksit düzenleme: cakisma formu silmez — güncel kayıt birleşir, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  let version = 'ts-1';
  let status = 'Bekliyor';
  let put = 0;
  const written = await financeEndpoints(page, {
    installment: () => installment({ surum: version, durum: status }),
    write: async (r, path) => {
      if (r.request().method() !== 'PUT') return false;
      if (++put === 1) {
        version = 'ts-2';
        status = 'Odendi'; // başka oturum ödendi işaretledi
        await problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti; güncel hâli yükleyin.');
      } else await r.fulfill({ json: installment({ surum: 'ts-3', aciklama: 'Yeni not' }) });
      void path;
      return true;
    },
  });
  await page.goto(INSTALLMENTS.yol);
  await waitReady(page, INSTALLMENTS);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const form = page.getByRole('region', { name: 'Taksiti Düzenle' });
  const note = form.getByRole('textbox', { name: 'Açıklama' });
  await expect(note).toHaveValue('Peşinat sonrası');
  await note.fill('Yeni not');
  await form.getByRole('button', { name: 'Kaydet' }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt siz düzenlerken değişti');
  await expect(note).toHaveValue('Yeni not'); // form SİLİNMEDİ
  await expect(form.getByRole('combobox', { name: 'Durum' }).locator('option:checked')).toHaveText(
    'Ödendi',
  );
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    surum: 'ts-1',
    aciklama: 'Yeni not',
    vade: '2026-10-14T21:00:00Z',
    taksitTutari: '1250.50',
  });
  await form.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Taksit kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({
    surum: 'ts-2',
    aciklama: 'Yeni not',
    durum: 'Odendi',
  });
  expect(errors).toEqual([]);
});

test('sipariş: satır düğmeleri sunucu yetkilerinden; iptal onaylı', async ({ page }) => {
  const written = await financeEndpoints(page);
  await page.goto(ORDERS.yol);
  await waitReady(page, ORDERS);
  await expect(page.getByRole('gridcell', { name: '1.500.000,00 ₺' })).toBeVisible();
  await expect(page.getByRole('gridcell', { name: 'KR-000001 — Ziraat' })).toBeVisible();
  await page.getByRole('button', { name: 'İptal' }).click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('SP-000001');
  await dialog.getByRole('button', { name: 'İptal' }).click();
  await expect.poll(() => written.length).toBe(1);
  expect(page.url()).toContain('/app/arac-siparis');
});

test('müşteri taksit: ilk okuma dönmeden yazan kullanıcı — dokunmadığı alanlar TAZE kayıttan gider (inceleme M1)', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  const written = await financeEndpoints(page, {
    // Liste bayat: başka oturum tutarı 1.250,50 → 2.000 yaptı; tekil kayıt 1,5 sn gecikmeli döner.
    installmentRow: () => installment({ taksitTutari: 1250.5 }),
    installment: () => installment({ taksitTutari: 2000, tutarBaz: 2000, surum: 'ts-2' }),
    recordDelayMs: 1500,
    write: async (r) => {
      if (r.request().method() !== 'PUT') return false;
      await r.fulfill({ json: installment({ taksitTutari: 2000, surum: 'ts-3' }) });
      return true;
    },
  });
  await page.goto(INSTALLMENTS.yol);
  await waitReady(page, INSTALLMENTS);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const form = page.getByRole('region', { name: 'Taksiti Düzenle' });
  await form.getByRole('textbox', { name: 'Açıklama' }).fill('Yeni not'); // ilk okuma henüz dönmedi
  const save = form.getByRole('button', { name: 'Kaydet' });
  await expect(save).toBeEnabled({ timeout: 5000 });
  await expect(form.getByRole('textbox', { name: 'Tutar' })).toHaveValue('2.000,00');
  await save.click();
  await expect(page.getByText('Taksit kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    taksitTutari: '2000.00',
    aciklama: 'Yeni not',
    surum: 'ts-2',
  });
  expect(errors).toEqual([]);
});

test('filo plan: ilk okuma dönmeden yazan kullanıcı — hedef TAZE kayıttan gider (inceleme M1)', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  const written = await financeEndpoints(page, {
    planRow: () => fleetPlan({ hedefAdet: 10 }),
    plan: () => fleetPlan({ hedefAdet: 11, fark: 4, surum: 'fp-2' }),
    recordDelayMs: 1500,
    write: async (r) => {
      if (r.request().method() !== 'PUT') return false;
      await r.fulfill({ json: fleetPlan({ hedefAdet: 11, aciklama: 'Q4 hedefi', surum: 'fp-3' }) });
      return true;
    },
  });
  await page.goto(PLAN.yol);
  await waitReady(page, PLAN);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const form = page.getByRole('region', { name: 'Hedefi Düzenle' });
  await form.getByRole('textbox', { name: 'Açıklama' }).fill('Q4 hedefi');
  const save = form.getByRole('button', { name: 'Kaydet' });
  await expect(save).toBeEnabled({ timeout: 5000 });
  await save.click();
  await expect(page.getByText('Hedef kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    hedefAdet: 11,
    aciklama: 'Q4 hedefi',
    surum: 'fp-2',
  });
  expect(errors).toEqual([]);
});

test('müşteri taksit: TRY kaydı EUR yapılınca eski kur (1) gönderilmez — kur null (inceleme M2)', async ({
  page,
}) => {
  const written = await financeEndpoints(page, {
    write: async (r) => {
      if (r.request().method() !== 'PUT') return false;
      await r.fulfill({ json: installment({ doviz: 'EUR', kur: 35.1, surum: 'ts-2' }) });
      return true;
    },
  });
  await page.goto(INSTALLMENTS.yol);
  await waitReady(page, INSTALLMENTS);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const form = page.getByRole('region', { name: 'Taksiti Düzenle' });
  const save = form.getByRole('button', { name: 'Kaydet' });
  await expect(save).toBeEnabled();
  await form.getByRole('textbox', { name: 'Döviz' }).fill('EUR');
  await save.click();
  await expect(page.getByText('Taksit kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ doviz: 'EUR', kur: null });
});

test('sipariş: boş birim fiyat alan hatası verir, istek gitmez (inceleme L3)', async ({ page }) => {
  const written = await financeEndpoints(page);
  await page.goto(ORDER_NEW.yol);
  await waitReady(page, ORDER_NEW);
  await page.getByRole('combobox', { name: 'Tedarikçi', exact: true }).fill('Bayi B');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByRole('textbox', { name: 'Birim Fiyat (resmi)' })).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  expect(written).toHaveLength(0);
});

test('durum panosu "Tahsis" → BAF formu araç + çıkış KM + şube dolu açılır (F6.3 parite)', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await vehicleEndpoints(page);
  await page.route('**/api/ui/v1/araclar/durum**', (r) =>
    r.fulfill({
      json: {
        liste: {
          kayitlar: [
            {
              ...statusRow(),
              durum: 'Musait',
              kirada: false,
              aktifKiraId: null,
              kiraSozlesmeNo: null,
              musteriAd: null,
              musteriTel: null,
              kiraBitTar: null,
              kiraKalanGun: null,
              kiraBakiye: null,
            },
          ],
          toplam: 1,
          sayfaNo: 1,
          boyut: 100,
        },
        kirada: 0,
        serviste: 0,
        bafta: 0,
      },
    }),
  );
  const written = await financeEndpoints(page);
  await page.goto('/app/arac-durum');
  await page
    .getByRole('row')
    .filter({ hasText: '34 ABC 123' })
    .getByRole('link', { name: 'Tahsis' })
    .click();
  await expect(page).toHaveURL(/\/app\/baf$/);
  const form = page.getByRole('region', { name: 'Yeni Tahsis' });
  await expect(form.getByRole('combobox', { name: 'Araç', exact: true })).toHaveValue('34ABC123');
  await expect(form.getByRole('textbox', { name: 'Çıkış KM' })).toHaveValue('12.500');
  await expect(form.getByRole('combobox', { name: 'Şube (çıkış)' })).toHaveValue('Merkez');
  // Personel seçilmeden kayıt gitmez (zorunlu alan); dolu gelen form "kaydedilmemiş değişiklik" sayılmaz.
  await form.getByRole('button', { name: 'Tahsis Et', exact: true }).click();
  await expect(form.getByRole('combobox', { name: 'Personel', exact: true })).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  expect(written).toHaveLength(0);
  expect(errors).toEqual([]);
});

for (const s of PAGES) {
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await financeEndpoints(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await waitReady(page, s);
        expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await financeEndpoints(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await waitReady(page, s);
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
