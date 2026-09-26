import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Request } from '@playwright/test';

import { logIn } from './ortak';

/**
 * F3.6 form seti — sahte arka uçla (route mock) üretim derlemesi üstünde:
 * doğrulama hatasında form korunur, kaydedilmemiş değişiklik sorulur, çift tık tek istek.
 */
const FORM = '/app/vitrin/form';

// Ana sayfa oturum ister (F3.3 oturumGuard): `ben` sahte API'den.
test.beforeEach(async ({ page }) => logIn(page));
const GONDER = '**/api/ui/v1/vitrin/form';

const CUSTOMERS = [
  { id: '0f8fad5b-d9cb-469f-a165-70867728950e', etiket: 'Ahmet Yılmaz', tip: 'Bireysel' },
  { id: '7c9e6679-7425-40de-944b-e07fc1f90ae7', etiket: 'Işık Lojistik', tip: 'Kurumsal' },
];

function collectErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (m) => {
    // Sahte 400 yanıtı tarayıcı konsoluna ağ hatası olarak düşer; beklenen.
    if (m.type() === 'error' && !m.text().includes('status of 400')) errors.push(m.text());
  });
  page.on('pageerror', (h) => errors.push(h.message));
  return errors;
}

async function mockCustomerSelection(page: Page): Promise<void> {
  await page.route('**/api/ui/v1/secim/musteri**', (route) => route.fulfill({ json: CUSTOMERS }));
}

async function seriousViolations(page: Page): Promise<string[]> {
  const result = await new AxeBuilder({ page }).analyze();
  return result.violations
    .filter((i) => i.impact === 'serious' || i.impact === 'critical')
    .map((i) => `${i.id}: ${i.nodes.map((n) => n.target.join(' ')).join('; ')}`);
}

const sekme = (page: Page, name: string) => page.getByRole('tab', { name: name });

/** Zorunlu alanları geçerli doldurur (Genel + Tarih ve tutar + Seçenekler). */
async function fillValid(page: Page): Promise<void> {
  await page.getByRole('textbox', { name: 'Plaka', exact: true }).fill('34 ABC 123');
  await page.getByRole('combobox', { name: 'Müşteri' }).click();
  await page.getByRole('combobox', { name: 'Müşteri' }).fill('Ah');
  await page.getByRole('option', { name: 'Ahmet Yılmaz' }).click();
  await page.getByLabel('Açıklama').fill('Uzun dönem');
  await sekme(page, 'Tarih ve tutar').click();
  await page.getByRole('textbox', { name: 'Tutar', exact: true }).fill('1.234,56');
  await page.getByLabel('Çıkış tarihi').fill('01.10.2026');
  await page.getByLabel('Dönüş zamanı').fill('02.10.2026');
  await page.getByRole('textbox', { name: 'Saat' }).fill('10:30');
  await sekme(page, 'Seçenekler').click();
  await page.getByLabel('Kredi kartı').check();
  await page.getByLabel('Kiralama koşullarını okudum').check();
}

test('sunucu 400 + alanlar: yazılan her değer korunur, hatalar doğru alanın altında', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await mockCustomerSelection(page);
  let request: Request | undefined;
  await page.route(GONDER, (route) => {
    request = route.request();
    return route.fulfill({
      status: 400,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        title: 'Doğrulama hatası',
        status: 400,
        detail: 'Bu plaka zaten kayıtlı.',
        kod: 'dogrulama',
        errors: { Plaka: ['Bu plaka zaten kayıtlı.'], Tutar: ['Tutar limiti aşıyor.'] },
      }),
    });
  });

  await page.goto(FORM);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Form vitrini');
  expect(await seriousViolations(page)).toEqual([]);

  await fillValid(page);
  await page.getByRole('button', { name: 'Kaydet' }).click();

  // İlk hatalı alan gizli "Genel" sekmesinde: o sekmeye geçilir ve alana odaklanılır.
  const plate = page.getByRole('textbox', { name: 'Plaka', exact: true });
  await expect(sekme(page, 'Genel')).toHaveAttribute('aria-selected', 'true');
  await expect(plate).toBeFocused();
  await expect(plate).toHaveAttribute('aria-invalid', 'true');
  const plateError = page.locator(
    '#' + ((await plate.getAttribute('aria-describedby')) ?? '').split(' ').at(-1),
  );
  await expect(plateError).toHaveText('Bu plaka zaten kayıtlı.');
  await expect(plate).toHaveValue('34 ABC 123');
  await expect(page.getByRole('combobox', { name: 'Müşteri' })).toHaveValue('Ahmet Yılmaz');
  await expect(page.getByLabel('Açıklama')).toHaveValue('Uzun dönem');
  await expect(sekme(page, 'Tarih ve tutar')).toContainText('hatalı alan var');

  await sekme(page, 'Tarih ve tutar').click();
  const amount = page.getByRole('textbox', { name: 'Tutar', exact: true });
  await expect(amount).toHaveValue('1.234,56');
  await expect(amount).toHaveAttribute('aria-invalid', 'true');
  await expect(page.locator('rc-alan', { has: amount })).toContainText('Tutar limiti aşıyor.');
  await expect(page.getByLabel('Çıkış tarihi')).toHaveValue('01.10.2026');
  await expect(page.getByLabel('Dönüş zamanı')).toHaveValue('02.10.2026');
  await expect(page.getByRole('textbox', { name: 'Saat' })).toHaveValue('10:30');

  await sekme(page, 'Seçenekler').click();
  await expect(page.getByLabel('Kredi kartı')).toBeChecked();
  await expect(page.getByLabel('Kiralama koşullarını okudum')).toBeChecked();

  // Gövde: para invariant metin, gün yerel takvim günü, an UTC; başlıkta işlem anahtarı.
  expect(request?.postDataJSON()).toMatchObject({
    plaka: '34 ABC 123',
    musteriId: CUSTOMERS[0]?.id,
    tutar: '1234.56',
    cikisTarihi: '2026-10-01',
    donusAni: '2026-10-02T07:30:00.000Z',
    odemeTuru: 'kart',
  });
  expect(request?.headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/);

  expect(await seriousViolations(page)).toEqual([]);
  expect(errors).toEqual([]);
});

test('kaydedilmemiş değişiklik: başka sekmeye geçiş korur, sekmeyi kapatmak sorar, sayfa kapatma beforeunload', async ({
  page,
}) => {
  await page.goto(FORM);
  await page.getByRole('textbox', { name: 'Plaka', exact: true }).fill('06 XYZ 1');
  const tabs = page.getByRole('navigation', { name: 'Açık sekmeler' });

  // Uygulama içi gezinme (F3.2 sekmeli çalışma alanı): form sekmesi açık kalır → sorulmaz, değer korunur.
  await page.getByRole('link', { name: 'Ana sayfa' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni arayüz yapım aşamasında');
  await expect(page.getByRole('alertdialog')).toHaveCount(0);
  await tabs.getByRole('link', { name: 'Form vitrini' }).click();
  await expect(page.getByRole('textbox', { name: 'Plaka', exact: true })).toHaveValue('06 XYZ 1');

  // Sekmeyi kapatmak veriyi atar: F3.3 CDK onay diyaloğu (ONAY_ISTEMI), ilk odak "Sayfada kal".
  const close = tabs.getByRole('button', { name: 'Form vitrini sekmesini kapat' });
  await close.click();
  const question = page.getByRole('alertdialog', { name: 'Sayfadan ayrılınsın mı?' });
  await expect(question).toContainText('Kaydedilmemiş değişiklikler var');
  await expect(question.getByRole('button', { name: 'Sayfada kal' })).toBeFocused();
  await question.getByRole('button', { name: 'Sayfada kal' }).click();
  await expect(question).toHaveCount(0);
  await expect(page).toHaveURL(/\/app\/vitrin\/form/);
  await expect(page.getByRole('textbox', { name: 'Plaka', exact: true })).toHaveValue('06 XYZ 1');
  await expect(tabs.getByRole('link', { name: 'Form vitrini' })).toBeVisible();

  await close.click();
  await question.getByRole('button', { name: 'Sayfadan ayrıl' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni arayüz yapım aşamasında');
  await expect(tabs.getByRole('link', { name: 'Form vitrini' })).toHaveCount(0);

  // Tam sayfa terk (sekme kapatma / Blazor ekranına geçiş): beforeunload.
  await page.goto(FORM);
  await page.getByRole('textbox', { name: 'Plaka', exact: true }).fill('06 XYZ 2');
  const beforeunload = page.waitForEvent('dialog');
  await page.close({ runBeforeUnload: true });
  const d = await beforeunload;
  expect(d.type()).toBe('beforeunload');
  await d.accept();
});

test('gönder çift tıklanınca tek istek; başarıdan sonra form temiz, gezinme sormaz', async ({
  page,
}) => {
  await mockCustomerSelection(page);
  const keys: string[] = [];
  await page.route(GONDER, async (route) => {
    keys.push(route.request().headers()['idempotency-key'] ?? '');
    await new Promise((r) => setTimeout(r, 400));
    await route.fulfill({ json: { no: '2026220901001' } });
  });

  await page.goto(FORM);
  await fillValid(page);
  const save = page.getByRole('button', { name: 'Kaydet' });
  await save.dblclick();
  await expect(page.getByRole('button', { name: 'Gönderiliyor…' })).toBeDisabled();
  await expect(page.getByRole('status').filter({ hasText: 'Kaydedildi' })).toHaveText(
    'Kaydedildi: 2026220901001',
  );
  expect(keys).toHaveLength(1);

  // İkinci meşru gönderim (aynı sayfada) YENİ anahtarla — mükerrer sayılmaz.
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => keys.length).toBe(2);
  expect(keys[1]).not.toBe(keys[0]);
  await expect(page.getByRole('button', { name: 'Kaydet' })).toBeEnabled();

  let wasAsked = false;
  page.on('dialog', (d) => {
    wasAsked = true;
    void d.dismiss();
  });
  await page.getByRole('link', { name: 'Ana sayfa' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni arayüz yapım aşamasında');
  expect(wasAsked).toBe(false);
});
