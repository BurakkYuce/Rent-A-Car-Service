import { expect, test, type Page } from '@playwright/test';

import { ciddiIhlaller, hatalariTopla, oturumAc } from './ortak';

/**
 * F3.2 kabuk: menü kayıttan (sahte `/api/ui/v1/menu`), etkin sayfa, Blazor öğesinin tam sayfa açılışı
 * (kirli formda önce soru), Ctrl+K komut paleti, 390 px çekmece, sekmeli çalışma alanı.
 */
test.beforeEach(async ({ page }) => oturumAc(page));

const FORM = '/app/vitrin/form';
const menu = (page: Page) => page.getByRole('navigation', { name: 'Ana menü' });
const sekmeler = (page: Page) => page.getByRole('navigation', { name: 'Açık sekmeler' });

/** Blazor ekranı (SPA dışı, tam sayfa): statik e2e sunucusunda yok, sahte HTML döner. */
async function blazorEkrani(page: Page, yol: string): Promise<void> {
  await page.route(`**${yol}`, (route) =>
    route.fulfill({
      contentType: 'text/html; charset=utf-8',
      body: '<!doctype html><html lang="tr"><title>Blazor</title><h1>Blazor ekranı</h1></html>',
    }),
  );
}

test('menü kayıttan: gruplar, hızlı bağlantı, rozet; etkin sayfa işaretli; iki temada axe temiz', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await page.goto(FORM);

  const etkin = menu(page).getByRole('link', { name: 'Form seti' });
  await expect(etkin).toHaveAttribute('aria-current', 'page');
  await expect(menu(page).locator('[aria-current="page"]')).toHaveCount(1);
  await expect(menu(page).getByRole('button', { name: 'Vitrin' })).toHaveAttribute(
    'aria-expanded',
    'true',
  );
  await expect(menu(page).getByRole('button', { name: 'Kira' })).toHaveAttribute(
    'aria-expanded',
    'false',
  );
  await expect(menu(page).getByRole('link', { name: 'Yeni Rezervasyon' })).toBeVisible();
  await expect(menu(page).getByRole('link', { name: /Bildirimler\s*4\s*yeni/ })).toBeVisible();
  // Sunucunun göndermediği öğe (Finans) yok; Blazor öğesi /app dışına bağlanır.
  await expect(menu(page).getByText('Faturalar')).toHaveCount(0);
  await menu(page).getByRole('button', { name: 'Kira' }).click();
  await expect(menu(page).getByRole('link', { name: 'Kiralar' })).toHaveAttribute(
    'href',
    '/kiralar',
  );

  // Akordeon (Yol v2 §5.1): Kira açılınca Vitrin kapandı — tek grup açık.
  await expect(menu(page).getByRole('button', { name: 'Vitrin' })).toHaveAttribute(
    'aria-expanded',
    'false',
  );

  // Başka SPA sayfası: işaret taşınır.
  await menu(page).getByRole('button', { name: 'Vitrin' }).click();
  await menu(page).getByRole('link', { name: 'Tanım CRUD' }).click();
  await expect(page).toHaveURL(/\/app\/vitrin\/tanim$/);
  await expect(menu(page).getByRole('link', { name: 'Tanım CRUD' })).toHaveAttribute(
    'aria-current',
    'page',
  );
  await expect(etkin).not.toHaveAttribute('aria-current', 'page');

  expect(await ciddiIhlaller(page)).toEqual([]);
  await page.getByRole('button', { name: 'Koyu tema' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(await ciddiIhlaller(page)).toEqual([]);
  expect(hatalar).toEqual([]);
});

test('Blazor öğesi tam sayfa açılır; kirli formda önce sorulur (beforeunload ikinci kez sormaz)', async ({
  page,
}) => {
  await blazorEkrani(page, '/is-emirleri');
  const tarayiciDiyaloglari: string[] = [];
  page.on('dialog', (d) => {
    tarayiciDiyaloglari.push(d.type());
    void d.dismiss();
  });
  await page.goto(FORM);
  await page.getByRole('textbox', { name: 'Plaka', exact: true }).fill('34 KBK 32');
  await menu(page).getByRole('button', { name: 'Servis' }).click();
  const baglanti = menu(page).getByRole('link', { name: 'İş Emirleri' });

  await baglanti.click();
  const soru = page.getByRole('alertdialog', { name: 'Sayfadan ayrılınsın mı?' });
  await soru.getByRole('button', { name: 'Sayfada kal' }).click();
  await expect(page).toHaveURL(/\/app\/vitrin\/form$/);
  await expect(page.getByRole('textbox', { name: 'Plaka', exact: true })).toHaveValue('34 KBK 32');

  await baglanti.click();
  await soru.getByRole('button', { name: 'Sayfadan ayrıl' }).click();
  await expect(page).toHaveURL(/\/is-emirleri$/);
  await expect(page.getByRole('heading', { name: 'Blazor ekranı' })).toBeVisible();
  expect(tarayiciDiyaloglari).toEqual([]);
});

test('Ctrl+K: palet açılır, Türkçe-gevşek arama, klavyeyle SPA sayfası açılır; Esc kapatır', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await page.goto('/app/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  await page.keyboard.press('Control+K');
  const palet = page.getByRole('dialog', { name: 'Komut paleti' });
  await expect(palet).toBeVisible();
  const girdi = palet.getByRole('combobox', { name: 'Ekran adı' });
  await expect(girdi).toBeFocused();

  await girdi.fill('is');
  await expect(palet.getByRole('option').first()).toContainText('İş Emirleri');
  await girdi.fill('İŞ EMİR');
  await expect(palet.getByRole('option')).toHaveCount(1);
  expect(await ciddiIhlaller(page)).toEqual([]);

  await page.keyboard.press('Escape');
  await expect(palet).toHaveCount(0);

  await page.keyboard.press('Control+K');
  await girdi.fill('vitrin');
  await expect(palet.getByRole('option').first()).toHaveAttribute('aria-selected', 'true');
  await page.keyboard.press('ArrowDown');
  await expect(palet.getByRole('option', { name: /Form seti/ })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await page.keyboard.press('Enter');
  await expect(palet).toHaveCount(0);
  await expect(page).toHaveURL(/\/app\/vitrin\/form$/);
  await expect(menu(page).getByRole('link', { name: 'Form seti' })).toHaveAttribute(
    'aria-current',
    'page',
  );
  expect(hatalar).toEqual([]);
});

test('390 px: yan menü çekmece — odak içeride, Esc kapatır ve odağı geri verir; taşma yok, axe temiz', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/app/');
  const dugme = page.getByRole('button', { name: 'Menüyü aç' });
  await expect(menu(page)).toBeHidden();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  expect(await ciddiIhlaller(page)).toEqual([]);

  await dugme.click();
  await expect(menu(page)).toBeVisible();
  await expect(dugme).toHaveAttribute('aria-expanded', 'true');
  await expect(menu(page).getByRole('button', { name: 'Menüyü kapat' })).toBeFocused();
  expect(await ciddiIhlaller(page)).toEqual([]);

  await page.keyboard.press('Escape');
  await expect(menu(page)).toBeHidden();
  await expect(dugme).toBeFocused();

  await dugme.click();
  await menu(page).getByRole('button', { name: 'Vitrin' }).click();
  await menu(page).getByRole('link', { name: 'Geri bildirim' }).click();
  await expect(page).toHaveURL(/\/app\/vitrin\/geri-bildirim$/);
  await expect(menu(page)).toBeHidden();
});

test('sekmeler: form durumu sekmeler arasında korunur; depoda yalnız rota + id; yenilemede geri gelir', async ({
  page,
}) => {
  await page.goto(FORM);
  await page.getByRole('textbox', { name: 'Plaka', exact: true }).fill('06 SKM 06');
  await menu(page).getByRole('link', { name: 'Geri bildirim' }).click();
  await expect(page).toHaveURL(/\/app\/vitrin\/geri-bildirim$/);
  await expect(sekmeler(page).getByRole('link')).toHaveCount(2);
  await expect(sekmeler(page).getByRole('link', { name: 'Geri bildirim vitrini' })).toHaveAttribute(
    'aria-current',
    'page',
  );

  await sekmeler(page).getByRole('link', { name: 'Form vitrini' }).click();
  await expect(page.getByRole('textbox', { name: 'Plaka', exact: true })).toHaveValue('06 SKM 06');

  const depo = await page.evaluate(() => localStorage.getItem('rc.sekmeler'));
  expect(JSON.parse(depo ?? 'null')).toEqual([
    { rota: '/vitrin/form', id: null },
    { rota: '/vitrin/geri-bildirim', id: null },
  ]);
  expect(depo).not.toContain('06 SKM 06');

  page.on('dialog', (d) => void d.accept()); // kirli form: yenilemede beforeunload
  await page.reload();
  await expect(sekmeler(page).getByRole('link')).toHaveCount(2);
  await sekmeler(page).getByRole('link', { name: 'Geri bildirim vitrini' }).click();
  await expect(page).toHaveURL(/\/app\/vitrin\/geri-bildirim$/);
});
