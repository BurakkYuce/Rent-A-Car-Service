import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

import { logIn } from './ortak';

/**
 * Tablo motoru vitrini (`/app/vitrin/tablo`): 49 sütun, sabit plaka sütunu + sabit başlık, yatay
 * kaydırma, sağa yaslı tr para, klavye gezinmesi, kullanıcı düzeninin sunucuya yazılması. Veri
 * vitrinin bellek içi ucundan; `tablo-duzenleri` uçları route ile taklit edilir.
 */

const LAYOUT_ENDPOINT = '**/api/ui/v1/tablo-duzenleri/vitrin.araclar';

// Vitrin oturum ister (F3.3 oturumGuard; FetchPolicy oturum bağlamı yokken yüklemez): `ben` sahte.
test.beforeEach(async ({ page }) => logIn(page));

interface DuzenKaydi {
  sutunlar: { kod: string; gorunur: boolean; genislik: number | null }[];
  siralama: { kod: string; azalan: boolean }[];
}

async function mockLayoutEndpoint(page: Page, saved: DuzenKaydi | null = null) {
  const writtenItems: DuzenKaydi[] = [];
  await page.route(LAYOUT_ENDPOINT, async (route) => {
    const request = route.request();
    if (request.method() === 'PUT') writtenItems.push(request.postDataJSON() as DuzenKaydi);
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        tabloKodu: 'vitrin.araclar',
        duzen: request.method() === 'PUT' ? request.postDataJSON() : saved,
        guncellemeUtc: null,
      }),
    });
  });
  return writtenItems;
}

function collectErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') errors.push(m.text());
  });
  page.on('pageerror', (h) => errors.push(h.message));
  return errors;
}

const cell = (page: Page, row: number, column: number) =>
  page.locator(`[data-hucre="${row}:${column}"]`);

test('49 sütun: sabit başlık + sabit plaka, yatay kaydırma, sağa yaslı tr para, 36 px satır (Yol v2 §4), axe temiz', async ({
  page,
}) => {
  const errors = collectErrors(page);
  await mockLayoutEndpoint(page);
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/app/vitrin/tablo');

  const grid = page.getByRole('grid', { name: 'Araç listesi' });
  await expect(grid).toBeVisible();
  await expect(cell(page, 1, 1)).toHaveText(/\S/); // ilk veri satırı çizildi

  // 49 veri sütunu + seçim sütunu (kayitNo tanımda gizli → 48 görünür + 1 seçim).
  await expect(page.locator('th[data-kod]')).toHaveCount(49);
  await expect(grid).toHaveAttribute('aria-rowcount', '5001');

  // Yoğun satır: 32 px.
  const rowHeight = await page
    .locator('tbody tr.satir')
    .first()
    .evaluate((tr) => tr.getBoundingClientRect().height);
  expect(rowHeight).toBe(36);

  // Sağa yaslı, tr biçimli para.
  const daily = page.locator('th[data-kod="gunlukFiyat"]');
  const dailyOrder = Number((await daily.getAttribute('data-hucre'))?.split(':')[1]);
  const money = cell(page, 1, dailyOrder);
  await expect(money).toHaveText(/^\s*\d{1,3}(\.\d{3})*,\d{2} ₺\s*$/);
  expect(await money.evaluate((td) => getComputedStyle(td).textAlign)).toMatch(/^(right|end)$/);
  const km = page.locator('th[data-kod="km"]');
  const kmOrder = Number((await km.getAttribute('data-hucre'))?.split(':')[1]);
  await expect(cell(page, 1, kmOrder)).toHaveText(/^\s*\d{1,3}(\.\d{3})*\s*$/);

  // Yatay kaydırma: ızgara görünümden geniş; kaydırınca plaka yerinde, orta sütun kayar.
  const scroller = page.locator('.kaydirici');
  const [width, visible] = await scroller.evaluate((el) => [el.scrollWidth, el.clientWidth]);
  expect(width).toBeGreaterThan(visible * 3);
  const plate = cell(page, 1, 1);
  const plateFirst = await plate.boundingBox();
  const brandFirst = await cell(page, 1, 2).boundingBox();
  await scroller.evaluate((el) => (el.scrollLeft = 2500));
  await expect
    .poll(async () => (await cell(page, 1, 2).boundingBox())?.x ?? 0)
    .toBeLessThan((brandFirst?.x ?? 0) - 2000);
  expect((await plate.boundingBox())?.x).toBeCloseTo(plateFirst?.x ?? -1, 0);

  // Sabit başlık: dikey kaydırmada başlık yerinde, satırlar sanal (hepsi DOM'da değil).
  const headerFirst = await page.locator('th[data-kod="plaka"]').boundingBox();
  await scroller.evaluate((el) => (el.scrollTop = 1500));
  await expect(page.locator('tbody tr.satir[aria-rowindex="2"]')).toHaveCount(0);
  expect((await page.locator('th[data-kod="plaka"]').boundingBox())?.y).toBeCloseTo(
    headerFirst?.y ?? -1,
    0,
  );
  expect(await page.locator('tbody tr.satir').count()).toBeLessThan(100);

  const axe = await new AxeBuilder({ page }).include('rc-tablo').analyze();
  const serious = axe.violations
    .filter((v) => v.impact === 'serious' || v.impact === 'critical')
    .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`);
  expect(serious).toEqual([]);
  expect(errors).toEqual([]);
});

test('klavye: oklarla hücre gezinmesi, Enter satırı açar, başlıkta sıralama sunucuya gider', async ({
  page,
}) => {
  await mockLayoutEndpoint(page);
  await page.goto('/app/vitrin/tablo');
  await expect(cell(page, 1, 1)).toHaveText(/\S/);

  await cell(page, 1, 1).click();
  await expect(cell(page, 1, 1)).toBeFocused();
  await page.keyboard.press('ArrowRight');
  await expect(cell(page, 1, 2)).toBeFocused();
  await page.keyboard.press('ArrowDown');
  await expect(cell(page, 2, 2)).toBeFocused();
  const plate = (await cell(page, 2, 1).textContent())?.trim() ?? '';
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('acilan')).toHaveText(`Açılan: ${plate}`);

  // Odak görünür (klavyeyle gelindi).
  const frame = await cell(page, 2, 2).evaluate((td) => getComputedStyle(td).outlineStyle);
  expect(frame).toBe('solid');

  // Başlığa çık, Günlük fiyat'a git, Enter → azalan değil artan; URL'e sirala yazılır.
  await page.keyboard.press('Control+Home');
  await expect(cell(page, 0, 0).locator('input')).toBeFocused();
  const dailyOrder = Number(
    (await page.locator('th[data-kod="gunlukFiyat"]').getAttribute('data-hucre'))?.split(':')[1],
  );
  for (let i = 0; i < dailyOrder; i++) await page.keyboard.press('ArrowRight');
  await expect(cell(page, 0, dailyOrder).locator('button')).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/sirala=gunlukFiyat/);
  // Yeni sayfa gelene kadar önceki veri soluk kalır (aria-busy); sonra artan sırada.
  await expect(page.getByRole('grid')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('th[data-kod="gunlukFiyat"]')).toHaveAttribute(
    'aria-sort',
    'ascending',
  );
  const amounts = await page
    .locator(`tbody tr.satir td[data-hucre$=":${dailyOrder}"]`)
    .allTextContents();
  const numbers = amounts
    .slice(0, 5)
    .map((t) => Number(t.replace(/[^\d,]/g, '').replace(',', '.')));
  expect(numbers).toEqual([...numbers].sort((a, b) => a - b));
});

test('düzen: kayıtlı düzen açılışta uygulanır, sütun gizleme sunucuya yazılır', async ({
  page,
}) => {
  const writtenItems = await mockLayoutEndpoint(page, {
    sutunlar: [
      { kod: 'netKar', gorunur: true, genislik: 180 },
      { kod: 'marka', gorunur: true, genislik: null },
    ],
    siralama: [],
  });
  await page.goto('/app/vitrin/tablo');
  await expect(cell(page, 1, 1)).toHaveText(/\S/);

  // Kayıttaki sıra: plaka (sabit) → netKar → … → marka.
  const codes = await page
    .locator('th[data-kod]')
    .evaluateAll((ths) => ths.map((th) => th.getAttribute('data-kod')));
  expect(codes.slice(0, 3)).toEqual(['__secim', 'plaka', 'netKar']);
  expect(codes.indexOf('marka')).toBeGreaterThan(codes.indexOf('netKar'));
  // Kayıtta olmayan (yeni) sütun tanımdaki öncelinin ardına: model → marka'nın hemen ardı.
  expect(codes.indexOf('model')).toBe(codes.indexOf('marka') + 1);
  expect(
    await page.locator('th[data-kod="netKar"]').evaluate((th) => th.getBoundingClientRect().width),
  ).toBe(180);

  await page.getByRole('button', { name: 'Sütunlar' }).click();
  await page.getByRole('checkbox', { name: 'Marka' }).uncheck();
  await expect(page.locator('th[data-kod="marka"]')).toHaveCount(0);

  await expect.poll(() => writtenItems.length).toBe(1);
  const brand = writtenItems[0].sutunlar.find((s) => s.kod === 'marka');
  expect(brand).toEqual({ kod: 'marka', gorunur: false, genislik: null });
  expect(writtenItems[0].sutunlar[1]).toEqual({ kod: 'netKar', gorunur: true, genislik: 180 });
});

test('hata ≠ boş: hata senaryosunda hata bandı, boş senaryoda "Kayıt bulunamadı"', async ({
  page,
}) => {
  await mockLayoutEndpoint(page);
  await page.goto('/app/vitrin/tablo?senaryo=hata');
  await expect(page.locator('rc-tablo').getByRole('alert')).toContainText('Liste yüklenemedi');
  await expect(page.getByText('Kayıt bulunamadı')).toHaveCount(0);

  await page.goto('/app/vitrin/tablo?senaryo=bos');
  await expect(page.getByText('Kayıt bulunamadı')).toBeVisible();
  await expect(page.locator('rc-tablo').getByRole('alert')).toHaveCount(0);
});
