import { expect, test } from '@playwright/test';

import { RENTAL_1, customerCrmEndpoints, survey } from './customers-crm-fakes';
import { BEN, ciddiIhlaller, hatalariTopla, oturumAc, problem } from './ortak';
import { hazirBekle, tasmaOlc, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F7.2 CRM ekranları: anket, şikayet, assistans, hukuk, CRM analiz. Anket/şikayet/assistans/hukuk: liste + satırda
 * Düzenle (tekil okuma, `surum`) + onaylı Sil + Yeni formu. Senaryolar: doğrulama hatasında form korunur, `cakisma`
 * formu silmez, sayaçlar sunucudan, hukuk "bilgi" çiti + dışa aktarma süzgeçle; axe iki tema + taşma.
 */
const AG_HATASI = [
  /Failed to load resource: the server responded with a status of 4\d\d/,
  /Failed to load resource: net::ERR_FAILED/,
];

const PAGES: readonly VitrinSayfasi[] = [
  {
    ad: 'anketler',
    yol: '/app/anketler',
    baslik: 'Müşteri Anketleri',
    hazir: (page) => expect(page.getByRole('gridcell', { name: 'Memnun' })).toBeVisible(),
  },
  {
    ad: 'sikayetler',
    yol: '/app/sikayetler',
    baslik: 'Müşteri Şikayetleri',
    hazir: (page) =>
      expect(page.getByRole('gridcell', { name: 'Araç kirli teslim edildi' })).toBeVisible(),
  },
  {
    ad: 'assistans',
    yol: '/app/assistans',
    baslik: 'Assistans (Yol Yardım) Talepleri',
    hazir: (page) => expect(page.getByRole('gridcell', { name: 'Lastik patladı' })).toBeVisible(),
  },
  {
    ad: 'hukuk',
    yol: '/app/hukuk',
    baslik: 'Hukuk Dosyaları',
    hazir: (page) => expect(page.getByRole('gridcell', { name: '2026/15' })).toBeVisible(),
  },
  {
    ad: 'crm',
    yol: '/app/crm',
    baslik: 'CRM — Müşteri Segment & Personel Çalışma',
    hazir: (page) => expect(page.getByRole('gridcell', { name: 'Ayşe Yılmaz' })).toBeVisible(),
  },
];
const [SURVEYS, COMPLAINTS, ASSISTANCE, LEGAL, ANALYSIS] = PAGES as [
  VitrinSayfasi,
  VitrinSayfasi,
  VitrinSayfasi,
  VitrinSayfasi,
  VitrinSayfasi,
];

test.beforeEach(async ({ page }) => {
  await oturumAc(page, BEN);
});

// Sayfa başına ayrı test (#305 deseni): tek testte 5 sayfa × 2 tema axe taraması yük altında 30 sn sınırına dayanıyordu.
for (const s of PAGES) {
  test(`CRM ${s.ad}: içerik + axe iki tema, konsol hatası yok`, async ({ page }) => {
    const errors = hatalariTopla(page, AG_HATASI);
    await customerCrmEndpoints(page);
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto(s.yol);
    await hazirBekle(page, s);
    expect(await ciddiIhlaller(page), `${s.ad} açık`).toEqual([]);
    await page.emulateMedia({ colorScheme: 'dark' });
    expect(await ciddiIhlaller(page), `${s.ad} koyu`).toEqual([]);
    expect(errors).toEqual([]);
  });
}

test('anket: yeni kayıt — kira seçimi, varsayılan sorular, sorusu boş satır gitmez', async ({
  page,
}) => {
  const written = await customerCrmEndpoints(page, {
    write: async (r) => {
      await r.fulfill({ status: 201, json: { anket: survey(), surum: 'a-1', cevaplar: [] } });
      return true;
    },
  });
  await page.goto(SURVEYS.yol);
  await hazirBekle(page, SURVEYS);
  await expect(page.getByText('1 anket · 1 yapıldı · 1 yapılmadı')).toBeVisible();
  const form = page.getByRole('region', { name: 'Yeni Anket' });
  await form.getByRole('combobox', { name: 'Sözleşme (opsiyonel)' }).fill('2026');
  await page.getByRole('option', { name: /2026260801001 — 34ABC123/ }).click();
  await form.getByRole('textbox', { name: 'Cevap 1' }).fill('Evet');
  await form.getByRole('textbox', { name: 'Soru 2' }).fill('');
  await form.getByRole('button', { name: 'Ekle' }).click();
  await expect(page.getByText('Anket eklendi.')).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    rentalId: RENTAL_1,
    durum: 'Yapildi',
    puan: 0,
    cevaplar: [{ soruNo: 1, soru: 'Araç temiz miydi?', cevap: 'Evet', aciklama: null }],
  });
});

test('anket düzenleme: cakisma formu silmez — güncel kayıt birleşir, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  let surum = 'a-1';
  let puan = 9;
  let put = 0;
  const written = await customerCrmEndpoints(page, {
    survey: () => survey({ surum, puan }),
    write: async (r) => {
      if (++put === 1) {
        surum = 'a-2';
        puan = 4; // başka oturum puanı değiştirdi
        await problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti; güncel hâli yükleyin.');
      } else await r.fulfill({ json: { anket: survey({ puan: 4 }), surum: 'a-3', cevaplar: [] } });
      return true;
    },
  });
  await page.goto(SURVEYS.yol);
  await hazirBekle(page, SURVEYS);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const form = page.getByRole('region', { name: 'Anketi Düzenle' });
  const save = form.getByRole('button', { name: 'Kaydet' });
  await expect(save).toBeEnabled();
  await expect(form.getByRole('textbox', { name: 'Cevap 1' })).toHaveValue('Evet');
  const comment = form.getByRole('textbox', { name: 'Yorum' });
  await comment.fill('Çok memnun');
  await save.click();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt siz düzenlerken değişti');
  await expect(comment).toHaveValue('Çok memnun');
  await expect(form.getByRole('textbox', { name: 'Puan (0-10)' })).toHaveValue('4');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ surum: 'a-1', puan: 9 });
  await save.click();
  await expect(page.getByText('Anket kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({
    surum: 'a-2',
    puan: 4,
    yorum: 'Çok memnun',
  });
});

test('şikayet: doğrulama hatasında form korunur, hata alana yazılır', async ({ page }) => {
  const written = await customerCrmEndpoints(page, {
    write: async (r) => {
      await problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
        errors: { konu: ['Konu en fazla 256 karakter olabilir.'] },
      });
      return true;
    },
  });
  await page.goto(COMPLAINTS.yol);
  await hazirBekle(page, COMPLAINTS);
  await expect(page.getByText('1 şikayet · 1 açık')).toBeVisible();
  const form = page.getByRole('region', { name: 'Yeni Şikayet' });
  await form.getByRole('textbox', { name: 'Konu' }).fill('Klima çalışmıyor');
  await form.getByRole('textbox', { name: 'Puan (1-5)' }).fill('2');
  await form.getByRole('button', { name: 'Ekle' }).click();
  await expect(form.getByText('Konu en fazla 256 karakter olabilir.')).toBeVisible();
  await expect(form.getByRole('textbox', { name: 'Konu' })).toHaveValue('Klima çalışmıyor');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    konu: 'Klima çalışmıyor',
    puan: 2,
    durum: 'Acik',
  });
});

test('hukuk: bilgi çiti, kalan sunucudan, dışa aktarma süzgeçle, silme onaylı', async ({
  page,
}) => {
  const written = await customerCrmEndpoints(page);
  await page.goto(`${LEGAL.yol}?durum=Acik`);
  await hazirBekle(page, LEGAL);
  await expect(page.getByText('muhasebe defterine ve cari bakiyeye İŞLEMEZ')).toBeVisible();
  await expect(page.getByRole('gridcell', { name: '7.500,00 ₺' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Excel' })).toHaveAttribute(
    'href',
    '/listeler/export/hukuk?format=excel&durum=Acik',
  );
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const form = page.getByRole('region', { name: 'Düzenle: 2026/15' });
  await expect(form.getByText('7.500,00 ₺')).toBeVisible();
  await page.getByRole('button', { name: 'Sil', exact: true }).first().click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('2026/15 dosyası kalıcı olarak silinsin mi?');
  await dialog.getByRole('button', { name: 'Sil' }).click();
  await expect(page.getByText('2026/15 dosyası silindi.')).toBeVisible();
  expect(written).toHaveLength(1);
});

test('assistans + CRM analiz: rozetler ve sunucu özetleri', async ({ page }) => {
  await customerCrmEndpoints(page);
  await page.goto(ASSISTANCE.yol);
  await hazirBekle(page, ASSISTANCE);
  await expect(page.getByRole('gridcell', { name: 'Edemiyor' })).toBeVisible();
  await expect(page.getByText('1 talep · 1 açık · 1 araç hareket edemiyor')).toBeVisible();
  await page.goto(ANALYSIS.yol);
  await hazirBekle(page, ANALYSIS);
  await expect(
    page.locator('dl[aria-label="Segment özeti"]').getByText('Toplam Ciro').locator('..'),
  ).toContainText('12.500,75 ₺');
  await expect(page.getByRole('cell', { name: 'Ali Veli' })).toBeVisible();
});

for (const s of PAGES) {
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await customerCrmEndpoints(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await hazirBekle(page, s);
        expect(await tasmaOlc(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await customerCrmEndpoints(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await hazirBekle(page, s);
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
