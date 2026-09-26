import { expect, test, type Page } from '@playwright/test';

import { ACCOUNT_1, card, customerCrmEndpoints } from './customers-crm-fakes';
import { BEN, seriousViolations, collectErrors, logIn, problem, writeXsrf } from './ortak';
import { waitReady, measureOverflow, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F7.2 cari ekranları: liste, kart (yeni / düzenle), 360° detay + ekstre. Üç zorunlu senaryo (doğrulama hatasında
 * form korunur, oturum düşünce form kaybolmaz, `cakisma` formu silmez) + KVKK (TC kartta boş, PUT gidiş-dönüşünde
 * gizli alan `null` = korunur, tarayıcı deposuna PII yazılmaz, anonimleştirme kaldırma 403 mesajı) + axe iki tema +
 * 320/390/768/1440 taşma.
 */
const NETWORK_ERROR = [
  /Failed to load resource: the server responded with a status of 4\d\d/,
  /Failed to load resource: net::ERR_FAILED/,
];

export const CUSTOMER_PAGES: readonly VitrinSayfasi[] = [
  {
    ad: 'cariler',
    yol: '/app/cariler',
    baslik: 'Cariler',
    hazir: (page) => expect(page.getByRole('link', { name: 'Ayşe Yılmaz' })).toBeVisible(),
  },
  {
    ad: 'cari-kart',
    yol: `/app/cariler/${ACCOUNT_1}`,
    baslik: 'Cari: Ayşe Yılmaz',
    hazir: (page) =>
      expect(page.getByRole('textbox', { name: 'Ad', exact: true })).toHaveValue('Ayşe'),
  },
  { ad: 'cari-yeni', yol: '/app/cariler/yeni', baslik: 'Yeni Cari' },
  {
    ad: 'cari-detay',
    yol: `/app/cariler/${ACCOUNT_1}/detay`,
    baslik: 'Ayşe Yılmaz',
    hazir: (page) => expect(page.getByText('Pozitif = müşteri borçlu.')).toBeVisible(),
  },
];

const [LIST, CARD, NEW, DETAIL] = CUSTOMER_PAGES as [
  VitrinSayfasi,
  VitrinSayfasi,
  VitrinSayfasi,
  VitrinSayfasi,
];

test.beforeEach(async ({ page }) => {
  await logIn(page, { ...BEN, izinler: [...BEN.izinler, 'OperationsDelete'] });
});

test('cari sayfaları: içerik + axe iki tema, konsol hatası yok', async ({ page }) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  await customerCrmEndpoints(page);
  for (const s of CUSTOMER_PAGES) {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto(s.yol);
    await waitReady(page, s);
    expect(await seriousViolations(page), `${s.ad} açık`).toEqual([]);
    await page.emulateMedia({ colorScheme: 'dark' });
    expect(await seriousViolations(page), `${s.ad} koyu`).toEqual([]);
  }
  expect(errors).toEqual([]);
});

test('liste: TC sütunu yok, anonim müşteri etiketli, rozetler, silme onaylı', async ({ page }) => {
  const written = await customerCrmEndpoints(page);
  await page.goto(LIST.yol);
  await waitReady(page, LIST);
  await expect(page.getByRole('columnheader', { name: /TC/ })).toHaveCount(0);
  await expect(page.getByRole('gridcell', { name: /Anonim müşteri\s*KVKK anonim/ })).toBeVisible();
  await expect(page.getByText('Not', { exact: true })).toHaveAttribute('title', 'Ödeme gecikti');
  await expect(page.getByRole('gridcell', { name: '12.500,75 ₺' }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Sil', exact: true }).first().click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('Ayşe Yılmaz carisi kalıcı olarak silinsin mi?');
  await dialog.getByRole('button', { name: 'Sil' }).click();
  await expect(page.getByText('Ayşe Yılmaz carisi silindi.')).toBeVisible();
  expect(written).toHaveLength(1);
});

async function openCard(page: Page) {
  await page.goto(CARD.yol);
  await waitReady(page, CARD);
}

test('KVKK: TC kartta boş (yalnız "kayıtlı"), ehliyet maskeli; PUT gidiş-dönüşünde gizli alanlar null (korunur)', async ({
  page,
}) => {
  const written = await customerCrmEndpoints(page);
  await openCard(page);
  await page.getByRole('tab', { name: 'Kimlik ve Belge' }).click();
  const nationalId = page.getByRole('textbox', { name: 'TC Kimlik' });
  await expect(nationalId).toHaveValue('');
  await expect(page.getByText('Kayıtlı (gizli). Boş bırakılırsa korunur.')).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Ehliyet No' })).toHaveValue('');
  await expect(page.getByText('Kayıtlı: ****5678. Boş bırakılırsa korunur.')).toBeVisible();

  await page.getByRole('tab', { name: 'Genel' }).click();
  await page.getByRole('textbox', { name: 'Soyad' }).fill('Yılmaz Demir');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Ayşe Yılmaz carisi kaydedildi.')).toBeVisible();
  const body = JSON.parse(written[0]?.govde ?? '{}') as Record<string, unknown>;
  expect(body).toMatchObject({
    surum: 'c-1',
    soyad: 'Yılmaz Demir',
    tcKimlik: null,
    ehliyetNo: null,
    pasaportNo: null,
    vergiNo: null,
    sifre: null,
    riskLimiti: '15000.5',
  });
  expect(JSON.stringify(body)).not.toContain('Temizle');
});

test('KVKK: yazılan TC tarayıcı deposuna yazılmaz; "temizle" "" gönderir', async ({ page }) => {
  const written = await customerCrmEndpoints(page);
  await openCard(page);
  await page.getByRole('tab', { name: 'Kimlik ve Belge' }).click();
  await page.getByRole('textbox', { name: 'TC Kimlik' }).fill('10000000146');
  await page.getByRole('checkbox', { name: 'Kayıtlı değeri temizle' }).check();
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Ayşe Yılmaz carisi kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    tcKimlik: '10000000146',
    ehliyetNo: '',
  });
  const stored = await page.evaluate(() =>
    JSON.stringify({ ...window.localStorage, ...window.sessionStorage }),
  );
  expect(stored).not.toContain('10000000146');
  expect(stored).not.toContain('05321112233');
  // Kayıttan sonra gizli alan yeniden boş (kart numarayı taşımaz).
  await expect(page.getByRole('textbox', { name: 'TC Kimlik' })).toHaveValue('');
});

test('KVKK: anonimleştirmeyi kaldırma — sunucu 403, mesaj KVKK sekmesinde, form korunur', async ({
  page,
}) => {
  await customerCrmEndpoints(page, {
    card: () => card({ anonimTelefon: true, cepTel: null }),
    write: async (r) => {
      await problem(
        r,
        403,
        'yetki_yok',
        'KVKK anonimleştirmesini kaldırmak için kullanıcı yönetimi yetkisi gerekir.',
      );
      return true;
    },
  });
  await openCard(page);
  await expect(page.getByRole('textbox', { name: 'Cep Tel' })).toBeDisabled();
  await page.getByRole('tab', { name: 'KVKK' }).click();
  await expect(
    page.getByText('Kayıtlı bir anonimleştirmeyi kaldırmak kullanıcı yönetimi'),
  ).toBeVisible();
  const flag = page.getByRole('checkbox', { name: 'Telefon anonim' });
  await expect(flag).toBeChecked();
  await flag.uncheck();
  await page.getByRole('button', { name: 'Kaydet' }).click();
  const panel = page.getByRole('tabpanel', { name: 'KVKK' });
  await expect(panel.getByRole('alert')).toHaveText(
    'KVKK anonimleştirmesini kaldırmak için kullanıcı yönetimi yetkisi gerekir.',
  );
  await expect(flag).not.toBeChecked(); // form silinmedi
});

test('yeni cari: doğrulama hatasında form korunur, hata alana yazılır (gizli sekmeye geçer)', async ({
  page,
}) => {
  let n = 0;
  const written = await customerCrmEndpoints(page, {
    write: async (r) => {
      if (++n > 1) return false;
      await problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
        errors: { tcKimlik: ['TC Kimlik No geçersiz.'] },
      });
      return true;
    },
  });
  await page.goto(NEW.yol);
  await waitReady(page, NEW);
  await page.getByRole('textbox', { name: 'Ad', exact: true }).fill('Can');
  await page.getByRole('textbox', { name: 'Soyad' }).fill('Er');
  await page.getByRole('tab', { name: 'Kimlik ve Belge' }).click();
  await page.getByRole('textbox', { name: 'TC Kimlik' }).fill('12345678901');
  await page.getByRole('tab', { name: 'Genel' }).click();
  await page.getByRole('button', { name: 'Cari Oluştur' }).click();
  await expect(page.getByText('TC Kimlik No geçersiz.')).toBeVisible();
  await expect(page.getByRole('tab', { name: /Kimlik ve Belge/ })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByRole('textbox', { name: 'TC Kimlik' })).toHaveValue('12345678901');
  await expect(
    page.getByRole('textbox', { name: 'Ad', exact: true, includeHidden: true }),
  ).toHaveValue('Can');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    ad: 'Can',
    tcKimlik: '12345678901',
    tip: 'Bireysel',
  });
});

test('kart: oturum düşünce form kaybolmaz — yerinde giriş, AYNI istek (aynı anahtar + gövde)', async ({
  page,
}) => {
  let n = 0;
  const written = await customerCrmEndpoints(page, {
    write: async (r) => {
      if (++n === 1) {
        await problem(r, 401, 'oturum_yok', 'Oturum açık değil.');
        return true;
      }
      return false;
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
  await openCard(page);
  await page.getByRole('textbox', { name: 'Müşteri Temsilcisi' }).fill('Zeynep');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(
    page.getByRole('textbox', { name: 'Müşteri Temsilcisi', includeHidden: true }),
  ).toHaveValue('Zeynep');
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByText('Ayşe Yılmaz carisi kaydedildi.')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
});

test('kart: cakisma formu silmez — güncel kart birleşir, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  let version = 'c-1';
  let il = 'İstanbul';
  let put = 0;
  const written = await customerCrmEndpoints(page, {
    card: () => card({ surum: version, il }),
    write: async (r) => {
      if (++put === 1) {
        version = 'c-2';
        il = 'Ankara'; // başka oturum ili değiştirdi
        await problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti; güncel hâli yükleyin.');
        return true;
      }
      return false;
    },
  });
  await openCard(page);
  const rep = page.getByRole('textbox', { name: 'Müşteri Temsilcisi' });
  await rep.fill('Zeynep');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt siz düzenlerken değişti');
  await expect(rep).toHaveValue('Zeynep'); // form SİLİNMEDİ
  await page.getByRole('tab', { name: 'Adres' }).click();
  await expect(page.getByRole('combobox', { name: 'İl', exact: true })).toHaveValue('Ankara');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ surum: 'c-1', il: 'İstanbul' });
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Ayşe Yılmaz carisi kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({
    surum: 'c-2',
    il: 'Ankara',
    musteriTemsilcisi: 'Zeynep',
    tcKimlik: null,
  });
});

test('detay: finans yetkisi yoksa bakiye/hareket yerine not; ekstre sekmesi sunucu değerleriyle', async ({
  page,
}) => {
  await customerCrmEndpoints(page, { finance: false });
  await page.goto(DETAIL.yol);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Ayşe Yılmaz');
  await expect(
    page.getByText('Bakiye ve cari hareketleri finans ya da rapor yetkisi gerektirir.'),
  ).toBeVisible();
  await expect(page.getByText('Şifreli — cari kartında')).toBeVisible();
  await page.getByRole('tab', { name: 'Ekstre' }).click();
  const summary = page.locator('dl[aria-label="Ekstre özeti"]');
  await expect(summary.getByText('Toplam Borç').locator('..')).toContainText('3.000,00 ₺');
  await expect(summary.getByText('Bakiye').locator('..')).toContainText('250,00 ₺');
  await expect(page.getByRole('cell', { name: '2.750,00 ₺' }).first()).toBeVisible();
});

test('#295 M1: operatör (FinanceWrite/ViewReports yok) ekstre bağlantısı ve sekmesi görmez, ekstre istemez', async ({
  page,
}) => {
  await logIn(page, { ...BEN, rol: 'Operator', izinler: ['OperationsWrite'] });
  await customerCrmEndpoints(page, { finance: false });
  const statementCalls: string[] = [];
  page.on('request', (r) => {
    if (r.url().includes('/ekstre')) statementCalls.push(r.url());
  });
  await page.goto(LIST.yol);
  await waitReady(page, LIST);
  await expect(page.getByRole('link', { name: 'Detay' }).first()).toBeVisible();
  await expect(page.getByRole('link', { name: 'Ekstre' })).toHaveCount(0);
  await page.goto(`${DETAIL.yol}#sekme=ekstre`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Ayşe Yılmaz');
  await expect(page.getByRole('tab', { name: 'Özet' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('tab', { name: 'Ekstre' })).toHaveCount(0);
  expect(statementCalls).toEqual([]);
});

test('#295 M2: 11 haneli TC araması adres çubuğuna yazılmaz, istek yine gider', async ({
  page,
}) => {
  await customerCrmEndpoints(page);
  const searches: string[] = [];
  page.on('request', (r) => {
    const u = new URL(r.url());
    if (u.pathname === '/api/ui/v1/cariler') searches.push(u.searchParams.get('q') ?? '');
  });
  await page.goto(LIST.yol);
  await waitReady(page, LIST);
  const box = page.getByRole('searchbox', { name: 'Ara' });
  await box.fill('10000000146');
  await page.getByRole('button', { name: 'Filtrele', exact: true }).click();
  await expect.poll(() => searches.includes('10000000146')).toBe(true);
  expect(page.url()).not.toContain('10000000146');
  await expect(box).toHaveValue('10000000146');
  await box.fill('Ayşe');
  await page.getByRole('button', { name: 'Filtrele', exact: true }).click();
  await expect(page).toHaveURL(/q=Ay%C5%9Fe/);
});

test('#295 H1: tür değişince gizli vergi no zorunlu — istek gitmez; temizle ile gider', async ({
  page,
}) => {
  const written = await customerCrmEndpoints(page, {
    card: () => card({ vergiNoMaske: '******6780' }),
  });
  await openCard(page);
  await page.getByRole('combobox', { name: 'Tür' }).selectOption('Kurumsal');
  await expect(
    page.getByText(
      'Tür değişikliğinde vergi no yeniden girilmeli (ya da kayıtlı değeri temizleyin).',
    ),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByRole('textbox', { name: /Vergi No/ })).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  expect(written).toHaveLength(0);
  await page.getByRole('checkbox', { name: 'Kayıtlı değeri temizle' }).first().check();
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Ayşe Yılmaz carisi kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ tip: 'Kurumsal', vergiNo: '' });
});

for (const s of CUSTOMER_PAGES) {
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await customerCrmEndpoints(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await waitReady(page, s);
        expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await customerCrmEndpoints(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await waitReady(page, s);
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
