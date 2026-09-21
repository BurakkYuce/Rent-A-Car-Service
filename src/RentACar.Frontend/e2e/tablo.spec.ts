import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

/**
 * Tablo motoru vitrini (`/app/vitrin/tablo`): 49 sütun, sabit plaka sütunu + sabit başlık, yatay
 * kaydırma, sağa yaslı tr para, klavye gezinmesi, kullanıcı düzeninin sunucuya yazılması. Veri
 * vitrinin bellek içi ucundan; `tablo-duzenleri` uçları route ile taklit edilir.
 */

const DUZEN_UCU = '**/api/ui/v1/tablo-duzenleri/vitrin.araclar';

interface DuzenKaydi {
  sutunlar: { kod: string; gorunur: boolean; genislik: number | null }[];
  siralama: { kod: string; azalan: boolean }[];
}

async function duzenUcunuTaklitEt(page: Page, kayitli: DuzenKaydi | null = null) {
  const yazilanlar: DuzenKaydi[] = [];
  await page.route(DUZEN_UCU, async (route) => {
    const istek = route.request();
    if (istek.method() === 'PUT') yazilanlar.push(istek.postDataJSON() as DuzenKaydi);
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        tabloKodu: 'vitrin.araclar',
        duzen: istek.method() === 'PUT' ? istek.postDataJSON() : kayitli,
        guncellemeUtc: null,
      }),
    });
  });
  return yazilanlar;
}

function hatalariTopla(page: Page): string[] {
  const hatalar: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') hatalar.push(m.text());
  });
  page.on('pageerror', (h) => hatalar.push(h.message));
  return hatalar;
}

const hucre = (page: Page, satir: number, sutun: number) =>
  page.locator(`[data-hucre="${satir}:${sutun}"]`);

test('49 sütun: sabit başlık + sabit plaka, yatay kaydırma, sağa yaslı tr para, 32 px satır, axe temiz', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await duzenUcunuTaklitEt(page);
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/app/vitrin/tablo');

  const izgara = page.getByRole('grid', { name: 'Araç listesi' });
  await expect(izgara).toBeVisible();
  await expect(hucre(page, 1, 1)).toHaveText(/\S/); // ilk veri satırı çizildi

  // 49 veri sütunu + seçim sütunu (kayitNo tanımda gizli → 48 görünür + 1 seçim).
  await expect(page.locator('th[data-kod]')).toHaveCount(49);
  await expect(izgara).toHaveAttribute('aria-rowcount', '5001');

  // Yoğun satır: 32 px.
  const satirYuksekligi = await page
    .locator('tbody tr.satir')
    .first()
    .evaluate((tr) => tr.getBoundingClientRect().height);
  expect(satirYuksekligi).toBe(32);

  // Sağa yaslı, tr biçimli para.
  const gunluk = page.locator('th[data-kod="gunlukFiyat"]');
  const gunlukSira = Number((await gunluk.getAttribute('data-hucre'))?.split(':')[1]);
  const para = hucre(page, 1, gunlukSira);
  await expect(para).toHaveText(/^\s*\d{1,3}(\.\d{3})*,\d{2} ₺\s*$/);
  expect(await para.evaluate((td) => getComputedStyle(td).textAlign)).toMatch(/^(right|end)$/);
  const km = page.locator('th[data-kod="km"]');
  const kmSira = Number((await km.getAttribute('data-hucre'))?.split(':')[1]);
  await expect(hucre(page, 1, kmSira)).toHaveText(/^\s*\d{1,3}(\.\d{3})*\s*$/);

  // Yatay kaydırma: ızgara görünümden geniş; kaydırınca plaka yerinde, orta sütun kayar.
  const kaydirici = page.locator('.kaydirici');
  const [genislik, gorunur] = await kaydirici.evaluate((el) => [el.scrollWidth, el.clientWidth]);
  expect(genislik).toBeGreaterThan(gorunur * 3);
  const plaka = hucre(page, 1, 1);
  const plakaOnce = await plaka.boundingBox();
  const markaOnce = await hucre(page, 1, 2).boundingBox();
  await kaydirici.evaluate((el) => (el.scrollLeft = 2500));
  await expect
    .poll(async () => (await hucre(page, 1, 2).boundingBox())?.x ?? 0)
    .toBeLessThan((markaOnce?.x ?? 0) - 2000);
  expect((await plaka.boundingBox())?.x).toBeCloseTo(plakaOnce?.x ?? -1, 0);

  // Sabit başlık: dikey kaydırmada başlık yerinde, satırlar sanal (hepsi DOM'da değil).
  const baslikOnce = await page.locator('th[data-kod="plaka"]').boundingBox();
  await kaydirici.evaluate((el) => (el.scrollTop = 1500));
  await expect(page.locator('tbody tr.satir[aria-rowindex="2"]')).toHaveCount(0);
  expect((await page.locator('th[data-kod="plaka"]').boundingBox())?.y).toBeCloseTo(
    baslikOnce?.y ?? -1,
    0,
  );
  expect(await page.locator('tbody tr.satir').count()).toBeLessThan(100);

  const axe = await new AxeBuilder({ page }).include('rc-tablo').analyze();
  const ciddi = axe.violations
    .filter((v) => v.impact === 'serious' || v.impact === 'critical')
    .map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`);
  expect(ciddi).toEqual([]);
  expect(hatalar).toEqual([]);
});

test('klavye: oklarla hücre gezinmesi, Enter satırı açar, başlıkta sıralama sunucuya gider', async ({
  page,
}) => {
  await duzenUcunuTaklitEt(page);
  await page.goto('/app/vitrin/tablo');
  await expect(hucre(page, 1, 1)).toHaveText(/\S/);

  await hucre(page, 1, 1).click();
  await expect(hucre(page, 1, 1)).toBeFocused();
  await page.keyboard.press('ArrowRight');
  await expect(hucre(page, 1, 2)).toBeFocused();
  await page.keyboard.press('ArrowDown');
  await expect(hucre(page, 2, 2)).toBeFocused();
  const plaka = (await hucre(page, 2, 1).textContent())?.trim() ?? '';
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('acilan')).toHaveText(`Açılan: ${plaka}`);

  // Odak görünür (klavyeyle gelindi).
  const cerceve = await hucre(page, 2, 2).evaluate((td) => getComputedStyle(td).outlineStyle);
  expect(cerceve).toBe('solid');

  // Başlığa çık, Günlük fiyat'a git, Enter → azalan değil artan; URL'e sirala yazılır.
  await page.keyboard.press('Control+Home');
  await expect(hucre(page, 0, 0).locator('input')).toBeFocused();
  const gunlukSira = Number(
    (await page.locator('th[data-kod="gunlukFiyat"]').getAttribute('data-hucre'))?.split(':')[1],
  );
  for (let i = 0; i < gunlukSira; i++) await page.keyboard.press('ArrowRight');
  await expect(hucre(page, 0, gunlukSira).locator('button')).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/sirala=gunlukFiyat/);
  // Yeni sayfa gelene kadar önceki veri soluk kalır (aria-busy); sonra artan sırada.
  await expect(page.getByRole('grid')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('th[data-kod="gunlukFiyat"]')).toHaveAttribute(
    'aria-sort',
    'ascending',
  );
  const tutarlar = await page
    .locator(`tbody tr.satir td[data-hucre$=":${gunlukSira}"]`)
    .allTextContents();
  const sayilar = tutarlar
    .slice(0, 5)
    .map((t) => Number(t.replace(/[^\d,]/g, '').replace(',', '.')));
  expect(sayilar).toEqual([...sayilar].sort((a, b) => a - b));
});

test('düzen: kayıtlı düzen açılışta uygulanır, sütun gizleme sunucuya yazılır', async ({
  page,
}) => {
  const yazilanlar = await duzenUcunuTaklitEt(page, {
    sutunlar: [
      { kod: 'netKar', gorunur: true, genislik: 180 },
      { kod: 'marka', gorunur: true, genislik: null },
    ],
    siralama: [],
  });
  await page.goto('/app/vitrin/tablo');
  await expect(hucre(page, 1, 1)).toHaveText(/\S/);

  // Kayıttaki sıra: plaka (sabit) → netKar → … → marka.
  const kodlar = await page
    .locator('th[data-kod]')
    .evaluateAll((ths) => ths.map((th) => th.getAttribute('data-kod')));
  expect(kodlar.slice(0, 3)).toEqual(['__secim', 'plaka', 'netKar']);
  expect(kodlar.indexOf('marka')).toBeGreaterThan(kodlar.indexOf('netKar'));
  // Kayıtta olmayan (yeni) sütun tanımdaki öncelinin ardına: model → marka'nın hemen ardı.
  expect(kodlar.indexOf('model')).toBe(kodlar.indexOf('marka') + 1);
  expect(
    await page.locator('th[data-kod="netKar"]').evaluate((th) => th.getBoundingClientRect().width),
  ).toBe(180);

  await page.getByRole('button', { name: 'Sütunlar' }).click();
  await page.getByRole('checkbox', { name: 'Marka' }).uncheck();
  await expect(page.locator('th[data-kod="marka"]')).toHaveCount(0);

  await expect.poll(() => yazilanlar.length).toBe(1);
  const marka = yazilanlar[0].sutunlar.find((s) => s.kod === 'marka');
  expect(marka).toEqual({ kod: 'marka', gorunur: false, genislik: null });
  expect(yazilanlar[0].sutunlar[1]).toEqual({ kod: 'netKar', gorunur: true, genislik: 180 });
});

test('hata ≠ boş: hata senaryosunda hata bandı, boş senaryoda "Kayıt bulunamadı"', async ({
  page,
}) => {
  await duzenUcunuTaklitEt(page);
  await page.goto('/app/vitrin/tablo?senaryo=hata');
  await expect(page.getByRole('alert')).toContainText('Liste yüklenemedi');
  await expect(page.getByText('Kayıt bulunamadı')).toHaveCount(0);

  await page.goto('/app/vitrin/tablo?senaryo=bos');
  await expect(page.getByText('Kayıt bulunamadı')).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});
