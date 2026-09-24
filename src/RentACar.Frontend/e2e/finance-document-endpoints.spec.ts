import { expect, test } from '@playwright/test';

import { CATEGORY_1, RENTAL_1, documentEndpoints, incomingRow } from './finance-document-fakes';
import { BEN, hatalariTopla, oturumAc, problem } from './ortak';

/**
 * #300 eksik uçların SPA bağları: fatura döviz toplamı (`/faturalar/ozet`), gider "Sözleşme" alanı + sütunu
 * (`/crm/secim/kira`), araç satışında satılabilir araç seçimi, gelen e-faturada aranabilir gider kategorisi ve toplu
 * faturalamada görünmeyen kiranın seçimden düşmesi (L5). Değerler elle kurulmuş sahte yanıtlardan.
 */
const AG_HATASI = [/Failed to load resource: the server responded with a status of \d\d\d/];

test.beforeEach(async ({ page }) => {
  await oturumAc(page, { ...BEN, izinler: [...BEN.izinler, 'FinanceReverse'] });
});

test('fatura listesi: döviz bazında toplam satırı sunucudan; süzgeç özete de gider, sayfalama gitmez', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  const summaryUrls: string[] = [];
  page.on('request', (r) => {
    if (r.url().includes('/api/ui/v1/faturalar/ozet')) summaryUrls.push(r.url());
  });
  await documentEndpoints(page);
  await page.goto('/app/faturalar?sayfa=2&doviz=USD');
  const totals = page.getByTestId('fatura-toplamlari');
  await expect(totals).toContainText('Süzgeçteki 3 faturanın toplamı (döviz bazında):');
  await expect(totals).toContainText('1.260,00 ₺ (2 fatura)');
  await expect(totals).toContainText('iade payı 60,00 ₺');
  await expect(totals).toContainText('1.200,00 $ (1 fatura)'); // farklı döviz TOPLANMAZ
  const url = new URL(summaryUrls.at(-1) ?? 'http://x');
  expect(url.searchParams.get('doviz')).toBe('USD');
  expect(url.searchParams.has('sayfa')).toBe(false);
  expect(hatalar).toEqual([]);
});

test('gider: Sözleşme alanı kiraId gönderir, liste sütununda sözleşme no görünür', async ({
  page,
}) => {
  const written = await documentEndpoints(page, {
    read: async (r, path) => {
      if (path !== '/api/ui/v1/giderler') return false;
      await r.fulfill({
        json: {
          kayitlar: [
            {
              id: 'g9',
              no: 'GD-000009',
              tip: 'Genel',
              tarih: '2026-09-03T00:00:00Z',
              aracId: null,
              plaka: null,
              cariId: null,
              cariAd: null,
              sube: null,
              evrakNo: null,
              netTutar: 10,
              kdvOrani: 0,
              kdvTutar: 0,
              genelToplam: 10,
              doviz: 'TRY',
              kur: 1,
              odemeYontemi: 'Nakit',
              kasaBankaHesap: 'Kasa',
              aciklama: null,
              kiraId: RENTAL_1,
              vade: null,
              odemeTarihi: null,
              odenen: 10,
              kalan: 0,
              takipEdilir: false,
              sozlesmeNo: '2026010901001',
            },
          ],
          toplam: 1,
          sayfaNo: 1,
          boyut: 50,
        },
      });
      return true;
    },
  });
  await page.goto('/app/giderler');
  await expect(page.getByRole('gridcell', { name: '2026010901001' })).toBeVisible();
  await page.getByRole('button', { name: 'Yeni Gider' }).click();
  const form = page.getByRole('region', { name: 'Yeni Gider' });
  await form.getByRole('combobox', { name: 'Sözleşme' }).fill('2026');
  await page.getByRole('option', { name: /2026010901001 — 34ABC123/ }).click();
  await form.getByRole('textbox', { name: 'Net Tutar' }).fill('10');
  await form.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => written.length).toBe(1);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ kiraId: RENTAL_1 });
});

test('araç satışı: araç seçimi satılabilir araç ucundan gelir', async ({ page }) => {
  const picks: string[] = [];
  page.on('request', (r) => {
    if (r.url().includes('/api/ui/v1/secim/')) picks.push(new URL(r.url()).pathname);
  });
  const written = await documentEndpoints(page);
  await page.goto('/app/satislar');
  await page.getByRole('button', { name: 'Yeni Satış' }).click();
  const form = page.getByRole('region', { name: 'Yeni Satış' });
  await form.getByRole('combobox', { name: 'Araç' }).fill('34');
  await page.getByRole('option', { name: '34ABC123' }).click();
  expect(picks).toContain('/api/ui/v1/secim/satilabilir-arac');
  expect(picks).not.toContain('/api/ui/v1/secim/arac');
  expect(written).toHaveLength(0);
  await expect(form.getByRole('combobox', { name: 'Araç' })).toHaveValue('34ABC123');
});

test('gelen e-fatura: kategori aranabilir seçim, kayıtlı adla ön-dolu; PUT kimliği taşır', async ({
  page,
}) => {
  const written = await documentEndpoints(page, {
    incoming: () => ({
      fatura: incomingRow({ giderKategoriId: CATEGORY_1, giderKategoriAd: 'Yakıt' }),
      surum: 'v-1',
    }),
  });
  await page.goto('/app/gelen-efatura');
  await page.getByRole('button', { name: 'KDV Kırılımı / Bağla' }).click();
  const form = page.getByRole('region', { name: 'KDV Kırılımı / Bağla — ETTN-0001' });
  const category = form.getByRole('combobox', { name: 'Gider Kategorisi' });
  await expect(category).toHaveValue('Yakıt');
  await form.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Kırılım ve bağlar kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ giderKategoriId: CATEGORY_1 });
});

test('toplu faturalama: hata sonrası yeniden yüklenen listede olmayan kira seçimde kalmaz (L5)', async ({
  page,
}) => {
  const rental = (id: string, no: string) => ({
    id,
    sozlesmeNo: no,
    musteriAd: 'Ayşe Yılmaz',
    plaka: '34ABC123',
    durum: 'Acik',
    tutar: 300,
    doviz: 'TRY',
  });
  let posts = 0;
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/faturalar/toplu') return false;
      if (++posts === 1) await problem(r, 400, 'dogrulama', 'Kira faturalanamadı.');
      else await r.fulfill({ json: { kesilen: [{ id: 'f1', no: 'F-1' }], atlananlar: [] } });
      return true;
    },
  });
  // Sahtelerden SONRA: Playwright'ta son kaydedilen rota önce eşleşir.
  let loads = 0;
  await page.route('**/api/ui/v1/kiralar*', (r) =>
    r.fulfill({
      json: {
        kayitlar:
          ++loads === 1 ? [rental('k1', 'K-001'), rental('k2', 'K-002')] : [rental('k1', 'K-001')], // K-002 başka oturumda faturalandı
        toplam: 2,
        sayfaNo: 1,
        boyut: 200,
      },
    }),
  );
  await page.goto('/app/faturalar');
  await page.getByRole('button', { name: 'Faturasız kiraları göster' }).click();
  await page.getByRole('checkbox', { name: 'K-001 sözleşmesini seç' }).check();
  await page.getByRole('checkbox', { name: 'K-002 sözleşmesini seç' }).check();
  await page.getByRole('button', { name: 'Seçilenleri Faturala' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Seçilenleri Faturala' }).click();
  await expect(page.getByRole('checkbox', { name: 'K-002 sözleşmesini seç' })).toHaveCount(0);

  await page.getByRole('button', { name: 'Seçilenleri Faturala' }).click();
  await expect(page.getByRole('alertdialog')).toContainText('Seçili 1 kira için fatura kesilecek.');
  await page.getByRole('alertdialog').getByRole('button', { name: 'Seçilenleri Faturala' }).click();
  await expect(page.getByText('1 fatura kesildi.').first()).toBeVisible();
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({ kiraIds: ['k1'] });
});
