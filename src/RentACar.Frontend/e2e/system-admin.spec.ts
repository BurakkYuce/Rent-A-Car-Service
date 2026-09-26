import { expect, test } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, logIn, problem } from './ortak';
import { ADMIN_BEN, USER_ADMIN, USER_OP, record, usersEndpoints, type Write } from './system-fakes';

/**
 * F11.2b kullanıcı yönetimi, ekran yetkileri, mesaj şablonları, denetim, ofisler: izin kapıları uçlarla birebir,
 * Admin hesabı yalnız Admin'e (M2), parola alanları new-password, tam PUT'ta surum, ofis adı çakışması alan hatası.
 */
const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];
const body = (w: Write | undefined) => JSON.parse(w?.govde ?? '{}') as Record<string, unknown>;

test('kullanıcılar (Admin): oluştur new-password + aktif şube; istisna ver; axe iki tema', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await logIn(page, ADMIN_BEN);
  const writes = await usersEndpoints(page);
  await page.goto('/app/kullanicilar');
  await expect(page.getByRole('cell', { name: 'operator1', exact: true }).first()).toBeVisible();
  expect(await seriousViolations(page), 'açık').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await seriousViolations(page), 'koyu').toEqual([]);

  const password = page.getByLabel(/^Parola/);
  await expect(password).toHaveAttribute('autocomplete', 'new-password');
  const branch = page.getByRole('combobox', { name: 'Şube (operatör kapsamı)' });
  await expect(branch.locator('option', { hasText: 'Eski Şube' })).toHaveCount(0);
  await page.getByRole('textbox', { name: 'Kullanıcı adı' }).fill('yeni.kullanici');
  await page.getByRole('combobox', { name: 'Rol' }).selectOption({ label: 'Admin' });
  await branch.selectOption({ label: 'Merkez' });
  await password.fill('gecici-parola-e2e');
  await page.getByRole('button', { name: 'Oluştur' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(body(writes[0])).toEqual({
    kullaniciAdi: 'yeni.kullanici',
    gorunenAd: null,
    rol: 'Admin',
    atanmisSube: 'Merkez',
    sifre: 'gecici-parola-e2e',
  });
  await expect(password).toHaveValue('');

  await page
    .getByRole('combobox', { name: 'Kullanıcı adı' })
    .selectOption({ label: 'operator1 (Operatör)' });
  await page.getByRole('combobox', { name: 'İzin' }).selectOption({ label: 'ViewReports' });
  await page
    .getByRole('combobox', { name: 'Tür' })
    .selectOption({ label: 'Yasak (rolünden geri al)' });
  await page.getByRole('button', { name: 'İstisnayı kaydet' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1]?.method).toBe('PUT');
  expect(writes[1]?.path).toBe(`/api/ui/v1/kullanicilar/${USER_OP}/istisnalar/ViewReports`);
  expect(body(writes[1])).toEqual({ ver: false });
  expect(errors).toEqual([]);
});

test('kullanıcılar (ManageUsers istisnalı Yönetici): Admin hesabı ve ManageUsers izni kapalı (M2)', async ({
  page,
}) => {
  await logIn(page, { ...ADMIN_BEN, rol: 'Yonetici' });
  await usersEndpoints(page);
  await page.goto('/app/kullanicilar');
  await expect(page.getByRole('cell', { name: 'patron', exact: true })).toBeVisible();
  await expect(page.getByText('yalnız Admin rolüne açıktır')).toBeVisible();
  const adminRow = page.getByRole('row').filter({ hasText: 'patron' }).first();
  await expect(adminRow.getByRole('button')).toHaveCount(0);
  const opRow = page.getByRole('row').filter({ hasText: 'operator1' }).first();
  await expect(opRow.getByRole('button', { name: 'Parola sıfırla' })).toBeVisible();
  await expect(
    page.getByRole('combobox', { name: 'Rol' }).locator('option', { hasText: 'Admin' }),
  ).toHaveCount(0);
  await expect(
    page.getByRole('combobox', { name: 'İzin' }).locator('option', { hasText: 'ManageUsers' }),
  ).toHaveCount(0);
  await expect(
    page.getByRole('combobox', { name: 'Kullanıcı adı' }).locator('option', { hasText: 'patron' }),
  ).toHaveCount(0);
});

test('kullanıcılar: ManageUsers yoksa rota açılmaz (uç kapısıyla birebir)', async ({ page }) => {
  await logIn(page, BEN);
  await page.goto('/app/kullanicilar');
  await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Kullanıcı Yönetimi' })).toHaveCount(0);
});

test('parola sıfırlama: satır formu new-password, gövde yalnız parola', async ({ page }) => {
  await logIn(page, ADMIN_BEN);
  const writes = await usersEndpoints(page);
  await page.goto('/app/kullanicilar');
  const opRow = page.getByRole('row').filter({ hasText: 'operator1' }).first();
  await opRow.getByRole('button', { name: 'Parola sıfırla' }).click();
  const field = page.getByLabel('operator1 için yeni parola');
  await expect(field).toHaveAttribute('autocomplete', 'new-password');
  await field.fill('yeni-gecici-parola');
  await page.getByRole('button', { name: 'Parolayı kaydet' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0]?.path).toBe(`/api/ui/v1/kullanicilar/${USER_OP}/sifre`);
  expect(body(writes[0])).toEqual({ sifre: 'yeni-gecici-parola' });
  expect(USER_ADMIN).not.toBe(USER_OP);
});

test('kendi satırı (M1): parola sıfırla / pasifleştir yok, profil parola sayfasına bağlantı', async ({
  page,
}) => {
  await logIn(page, ADMIN_BEN);
  await usersEndpoints(page);
  await page.goto('/app/kullanicilar');
  const selfRow = page.getByRole('row').filter({ hasText: 'Ayşe Yılmaz' }).first();
  await expect(selfRow).toBeVisible();
  await expect(selfRow.getByRole('button')).toHaveCount(0);
  const link = selfRow.getByRole('link', { name: 'Parolamı değiştir' });
  await expect(link).toHaveAttribute('href', '/app/profil/sifre-degistir');
});

test('ekran yetkileri: override kaydı rol listesiyle, rol kopyalama onaylı', async ({ page }) => {
  await logIn(page, ADMIN_BEN);
  const writes: Write[] = [];
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/yetki'),
    (r) => {
      const path = new URL(r.request().url()).pathname;
      if (r.request().method() !== 'GET') {
        writes.push(record(r));
        return r.fulfill({ json: path.endsWith('kopyala') ? { adet: 3 } : [] });
      }
      if (path.endsWith('matris'))
        return r.fulfill({ json: [{ rol: 'Admin', izinler: ['ManageUsers'] }] });
      if (path.endsWith('gruplar')) return r.fulfill({ json: [] });
      return r.fulfill({
        json: [
          {
            ekranKodu: 'personel',
            roller: ['Admin'],
            aktif: true,
            guncellemeUtc: '2026-09-01T09:00:00Z',
          },
        ],
      });
    },
  );
  await page.goto('/app/yetki');
  await expect(page.getByRole('cell', { name: 'personel' })).toBeVisible();
  expect(await seriousViolations(page)).toEqual([]);
  await page.getByRole('textbox', { name: 'Ekran kodu' }).fill('donem-kapanis');
  await page.getByRole('checkbox', { name: 'Muhasebe' }).check();
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(body(writes[0])).toEqual({
    ekranKodu: 'donem-kapanis',
    aktif: true,
    roller: ['Muhasebe'],
  });

  await page.getByRole('combobox', { name: 'Kaynak rol' }).selectOption({ label: 'Yonetici' });
  await page.getByRole('combobox', { name: 'Hedef rol' }).selectOption({ label: 'Muhasebe' });
  await page.getByRole('button', { name: 'Kopyala', exact: true }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Onayla' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(body(writes[1])).toEqual({ kaynak: 'Yonetici', hedef: 'Muhasebe' });
  await expect(page.getByText('3 ekran güncellendi.')).toBeVisible();
});

test('mesaj şablonları: tanımsız şablon surum null, kayıtlı şablon surum ile PUT', async ({
  page,
}) => {
  await logIn(page, ADMIN_BEN);
  const writes: Write[] = [];
  const templates = [
    {
      tur: 'TalepAlindi',
      kanal: 'Eposta',
      kayitli: true,
      konu: 'Talebiniz alındı',
      govde: 'Sayın {MusteriAd}',
      aktif: true,
      surum: 't-1',
    },
    {
      tur: 'TalepAlindi',
      kanal: 'Sms',
      kayitli: false,
      konu: null,
      govde: '',
      aktif: false,
      surum: null,
    },
  ];
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/mesaj-sablonlari'),
    (r) => {
      if (r.request().method() === 'GET') return r.fulfill({ json: templates });
      writes.push(record(r));
      return r.fulfill({ json: templates[0] });
    },
  );
  await page.goto('/app/mesaj-sablonlari');
  await expect(page.getByRole('cell', { name: 'Talebiniz alındı' })).toBeVisible();
  await page.getByRole('button', { name: 'Tanımla' }).click();
  await page.getByRole('textbox', { name: 'Gövde' }).fill('Talebiniz alındı {No}');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0]?.path).toBe('/api/ui/v1/mesaj-sablonlari/TalepAlindi/Sms');
  expect(body(writes[0])).toEqual({
    konu: null,
    govde: 'Talebiniz alındı {No}',
    aktif: true,
    surum: null,
  });

  await page.getByRole('button', { name: 'Düzenle' }).click();
  await page.getByRole('textbox', { name: 'Konu' }).fill('Talebiniz bize ulaştı');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(body(writes[1])).toEqual({
    konu: 'Talebiniz bize ulaştı',
    govde: 'Sayın {MusteriAd}',
    aktif: true,
    surum: 't-1',
  });
});

test('denetim: maskeli değerler düz metin, sayfalama sayfa=2 ister', async ({ page }) => {
  await logIn(page, ADMIN_BEN);
  const queries: string[] = [];
  await page.route('**/api/ui/v1/denetim?*', (r) => {
    queries.push(new URL(r.request().url()).search);
    return r.fulfill({
      json: {
        kayitlar: [
          {
            id: 'a1',
            tarihUtc: '2026-09-20T08:00:00Z',
            kullanici: 'patron',
            tablo: 'TenantSettings',
            kayitId: 'x1',
            islem: 'Update',
            eskiDegerler: '{"SmtpSifreEnc":"***","FirmaUnvan":"<b>Eski</b>"}',
            yeniDegerler: null,
          },
        ],
        toplam: 45,
        sayfaNo: 1,
        boyut: 30,
      },
    });
  });
  await page.goto('/app/denetim');
  await expect(page.getByText('{"SmtpSifreEnc":"***","FirmaUnvan":"<b>Eski</b>"}')).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Güncelleme' })).toBeVisible();
  await page.getByRole('button', { name: 'Sonraki' }).click();
  await expect.poll(() => queries.some((q) => q.includes('sayfa=2'))).toBe(true);
});

test('ofisler: tam PUT surum + gizli haftalık saatler korunur; ad çakışması alan hatası', async ({
  page,
}) => {
  collectErrors(page, NETWORK_ERROR);
  await logIn(page, ADMIN_BEN);
  const writes: Write[] = [];
  const office = {
    id: 'o1',
    kod: 'IST',
    ad: 'İstanbul Havalimanı',
    adres: null,
    telefon: null,
    eposta: null,
    calismaSaatleri: null,
    teslimUcreti: null,
    sube: 'Merkez',
    subeId: 'b1',
    ingilizceAd: null,
    bulusmaNoktasi: null,
    iata: 'IST',
    webdeGizle: false,
    lokasyonTuru: null,
    binaNo: null,
    tarif: null,
    ulke: null,
    postaKodu: null,
    mapsKonumu: null,
    ekAciklama: null,
    webSira: null,
    dropKarsilamaTuru: null,
    dropCalismaSekli: null,
    ozelMail: null,
    ozelTelefon: null,
    haftalikCalismaSaatleri: [{ gun: 1, acilis: '08:00', kapanis: '20:00', kapali: false }],
    aktif: true,
    surum: 'o-1',
  };
  await page.route('**/api/ui/v1/secim/sube?*', (r) => r.fulfill({ json: [] }));
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/lokasyonlar'),
    (r) => {
      const path = new URL(r.request().url()).pathname;
      if (r.request().method() === 'GET')
        return r.fulfill({
          json: path.endsWith('lokasyonlar')
            ? { kayitlar: [{ ...office, surum: null }], toplam: 1, sayfaNo: 1, boyut: 200 }
            : office,
        });
      writes.push(record(r));
      return writes.length === 1
        ? problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
            errors: { ad: ["'İstanbul Havalimanı' adlı ofis zaten var."] },
          })
        : r.fulfill({ json: office });
    },
  );
  await page.goto('/app/lokasyonlar');
  await page.getByRole('button', { name: 'Düzenle' }).click();
  await page.getByRole('textbox', { name: 'Telefon', exact: true }).fill('0212 111 11 11');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText("'İstanbul Havalimanı' adlı ofis zaten var.")).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Telefon', exact: true })).toHaveValue(
    '0212 111 11 11',
  );
  const put = body(writes[0]);
  expect(put['surum']).toBe('o-1');
  expect(put['haftalikCalismaSaatleri']).toEqual(office.haftalikCalismaSaatleri);
  expect(put['telefon']).toBe('0212 111 11 11');
});
