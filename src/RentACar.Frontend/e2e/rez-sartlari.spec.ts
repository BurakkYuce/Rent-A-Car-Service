import { expect, test, type Page, type Route } from '@playwright/test';

import {
  BEN,
  ciddiIhlaller,
  hatalariTopla,
  kaydet,
  type KayitliIstek,
  oturumAc,
  problem,
  xsrfYaz,
} from './ortak';
import { MUSTERI_1, SART_1, SART_2, ortakUclar, rezSart } from './planlama-sahte';
import { hazirBekle, tasmaOlc, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F5.2b rez şartları (`/app/rez-sartlari`): üç zorunlu senaryo ("doğrulama hatasında form korunur",
 * "oturum düşünce form kaybolmaz", "`cakisma` formu silmez") + oluştur/karşılandı/sil + axe + taşma.
 */
const AG_HATASI = [/Failed to load resource: the server responded with a status of 4\d\d/];
const LISTE = [
  rezSart(SART_1),
  rezSart(SART_2, {
    sart: 'Teslim havalimanında',
    karsilandi: true,
    karsilamaTarihi: '2026-09-21T09:00:00Z',
  }),
];

const SAYFA: VitrinSayfasi = {
  ad: 'rez-sartlari',
  yol: '/app/rez-sartlari',
  baslik: 'Rez Şartları (Müşteri Özel Talepleri)',
  hazir: async (page) => {
    await expect(page.getByRole('grid', { name: 'Rez şartları' })).not.toHaveAttribute(
      'aria-busy',
      'true',
    );
    await expect(page.getByRole('gridcell', { name: 'Bebek koltuğu', exact: true })).toBeVisible();
  },
};

interface Uclar {
  yazma: (route: Route) => Promise<void> | void;
  detay?: () => unknown;
}

async function sartUclari(page: Page, uclar: Uclar): Promise<KayitliIstek[]> {
  const yazilan: KayitliIstek[] = [];
  await page.route(
    (url) => url.pathname === '/api/ui/v1/rez-sartlari',
    (route) => {
      if (route.request().method() === 'GET')
        return route.fulfill({ json: { kayitlar: LISTE, toplam: 2, sayfaNo: 1, boyut: 50 } });
      yazilan.push(kaydet(route.request()));
      return uclar.yazma(route);
    },
  );
  await page.route('**/api/ui/v1/rez-sartlari/gruplar', (route) =>
    route.fulfill({ json: ['Ekipman'] }),
  );
  await page.route(
    (url) => url.pathname.startsWith('/api/ui/v1/rez-sartlari/b'),
    (route) => {
      if (route.request().method() === 'GET')
        return route.fulfill({ json: uclar.detay?.() ?? rezSart(SART_1, { surum: 'surum-1' }) });
      yazilan.push(kaydet(route.request()));
      return uclar.yazma(route);
    },
  );
  return yazilan;
}

async function yeniDoldur(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Yeni şart / talep' }).click();
  const duzenleyici = page.getByRole('region', { name: 'Yeni şart / talep' });
  await duzenleyici.getByRole('combobox', { name: 'Müşteri' }).click();
  await duzenleyici.getByRole('combobox', { name: 'Müşteri' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  await duzenleyici.getByRole('textbox', { name: 'Şart / talep' }).fill('Çocuk koltuğu (9 kg)');
}

async function formKorunduMu(page: Page): Promise<void> {
  // Diyalog açıkken arka plan erişilebilirlik ağacından gizlidir (modal): gizliler de aranır.
  const gizli = { includeHidden: true } as const;
  const duzenleyici = page.getByRole('region', { name: 'Yeni şart / talep', ...gizli });
  await expect(duzenleyici.getByRole('combobox', { name: 'Müşteri', ...gizli })).toHaveValue(
    'Ayşe Yılmaz',
  );
  await expect(duzenleyici.getByRole('textbox', { name: 'Şart / talep', ...gizli })).toHaveValue(
    'Çocuk koltuğu (9 kg)',
  );
}

test.beforeEach(async ({ page }) => {
  await oturumAc(page);
  await ortakUclar(page);
});

test('liste + oluştur: gövde API adlarıyla, talep tarihi bugün (İstanbul gece yarısı); axe iki tema', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  const yazilan = await sartUclari(page, {
    yazma: (r) => r.fulfill({ status: 201, json: rezSart(SART_1) }),
  });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);
  await expect(page.getByText('2 kayıt · 2 bekleyen')).toBeVisible();
  await expect(page.getByText('Karşılandı 21.09.2026')).toBeVisible();
  expect(await ciddiIhlaller(page), 'açık tema').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await ciddiIhlaller(page), 'koyu tema').toEqual([]);

  await yeniDoldur(page);
  expect(await ciddiIhlaller(page), 'form açık').toEqual([]);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText('Şart / talep oluşturuldu.')).toBeVisible();
  const govde = JSON.parse(yazilan[0]?.govde ?? '{}') as Record<string, unknown>;
  expect(govde).toMatchObject({
    musteriId: MUSTERI_1,
    sart: 'Çocuk koltuğu (9 kg)',
    karsilamaTarihi: null,
  });
  expect(String(govde['talepTarihi'])).toMatch(/T21:00:00\.000Z$/);
  expect(yazilan[0]?.anahtar).toBeTruthy();
  expect(hatalar).toEqual([]);
});

test('doğrulama hatasında form korunur: alan işaretlenir, değerler yerinde', async ({ page }) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await sartUclari(page, {
    yazma: (r) =>
      problem(r, 400, 'dogrulama', 'Şart metni en çok 512 karakter olabilir.', {
        errors: { sart: ['Şart metni en çok 512 karakter olabilir.'] },
      }),
  });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);
  await yeniDoldur(page);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  const alan = page.getByRole('textbox', { name: 'Şart / talep' });
  await expect(alan).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByText('Şart metni en çok 512 karakter olabilir.')).toBeVisible();
  await formKorunduMu(page);
  expect(hatalar).toEqual([]);
});

test('oturum düşünce form kaybolmaz: yerinde giriş → AYNI istek (aynı anahtar) tekrarlanır', async ({
  page,
}) => {
  let sayac = 0;
  const yazilan = await sartUclari(page, {
    yazma: (r) =>
      ++sayac === 1
        ? problem(r, 401, 'oturum_yok', 'Oturum açık değil.')
        : r.fulfill({ status: 201, json: rezSart(SART_1) }),
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await xsrfYaz(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await xsrfYaz(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);
  await yeniDoldur(page);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  const diyalog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(diyalog).toBeVisible();
  await formKorunduMu(page);
  await diyalog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await diyalog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByText('Şart / talep oluşturuldu.')).toBeVisible();
  expect(yazilan).toHaveLength(2);
  expect(yazilan[1]?.govde).toBe(yazilan[0]?.govde);
  expect(yazilan[1]?.anahtar).toBe(yazilan[0]?.anahtar);
});

test('cakisma formu silmez: bayat sürüm 409 → güncel kayıt birleşir (dokunulan korunur), sonraki PUT yeni sürümle', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let surum = 'surum-1';
  let teslimEden: string | null = null;
  let putSayisi = 0;
  const yazilan = await sartUclari(page, {
    detay: () => rezSart(SART_1, { surum, teslimEden }),
    yazma: (r) => {
      if (++putSayisi === 1) {
        // Başka oturum bu arada teslim edeni yazdı → sürüm değişti.
        surum = 'surum-2';
        teslimEden = 'Veli';
        return problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti; güncel hâli yükleyin.');
      }
      return r.fulfill({ json: rezSart(SART_1, { surum: 'surum-3' }) });
    },
  });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);
  await page.getByRole('button', { name: 'Düzenle Bebek koltuğu' }).click();
  const duzenleyici = page.getByRole('region', { name: 'Şartı düzenle' });
  const sart = duzenleyici.getByRole('textbox', { name: 'Şart / talep' });
  await expect(sart).toHaveValue('Bebek koltuğu');
  await sart.fill('Bebek koltuğu + yükseltici');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt siz düzenlerken değişti');
  await expect(sart).toHaveValue('Bebek koltuğu + yükseltici'); // form SİLİNMEDİ
  await expect(duzenleyici.getByRole('textbox', { name: 'Teslim eden' })).toHaveValue('Veli'); // dokunulmayan güncellendi
  expect(JSON.parse(yazilan[0]?.govde ?? '{}')).toMatchObject({
    surum: 'surum-1',
    teslimEden: null,
  });

  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText('Şart / talep kaydedildi.')).toBeVisible();
  expect(JSON.parse(yazilan[1]?.govde ?? '{}')).toMatchObject({
    surum: 'surum-2',
    sart: 'Bebek koltuğu + yükseltici',
    teslimEden: 'Veli',
    basTar: '2026-09-30T21:00:00Z', // dokunulmayan tarih sunucunun anıyla aynen
  });
  expect(hatalar).toEqual([]);
});

test('karşılandı / geri al / sil (onaylı) satır işlemleri doğru uca gider', async ({ page }) => {
  const yollar: string[] = [];
  await sartUclari(page, {
    yazma: (r) => {
      yollar.push(`${r.request().method()} ${new URL(r.request().url()).pathname}`);
      return r.request().method() === 'DELETE'
        ? r.fulfill({ status: 204 })
        : r.fulfill({ json: rezSart(SART_1) });
    },
  });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);
  await page.getByRole('button', { name: 'Karşılandı Bebek koltuğu' }).click();
  await expect(page.getByText('Karşılandı olarak işaretlendi.')).toBeVisible();
  await page.getByRole('button', { name: 'Geri al Teslim havalimanında' }).click();
  await expect(page.getByText('Karşılama geri alındı.')).toBeVisible();
  await page.getByRole('button', { name: 'Sil Bebek koltuğu' }).click();
  const onay = page.getByRole('alertdialog').or(page.getByRole('dialog'));
  await expect(onay).toContainText('“Bebek koltuğu” talebi silinecek.');
  await onay.getByRole('button', { name: 'Sil' }).click();
  await expect(page.getByText('Talep silindi.')).toBeVisible();
  expect(yollar).toEqual([
    `POST /api/ui/v1/rez-sartlari/${SART_1}/karsilandi`,
    `POST /api/ui/v1/rez-sartlari/${SART_2}/geri-al`,
    `DELETE /api/ui/v1/rez-sartlari/${SART_1}`,
  ]);
});

test.describe('mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
  test('/app/rez-sartlari: 320/390/768 px gövde yatay taşması yok (form açıkken dahil)', async ({
    page,
  }) => {
    await sartUclari(page, { yazma: (r) => r.fulfill({ status: 201, json: rezSart(SART_1) }) });
    for (const genislik of [320, 390, 768]) {
      await page.setViewportSize({ width: genislik, height: 844 });
      await page.goto(SAYFA.yol);
      await hazirBekle(page, SAYFA);
      expect(await tasmaOlc(page), `${genislik}px`).toEqual({ tasma: 0, suclular: [] });
      await page.getByRole('button', { name: 'Yeni şart / talep' }).click();
      expect(await tasmaOlc(page), `${genislik}px form`).toEqual({ tasma: 0, suclular: [] });
    }
  });
});

test('/app/rez-sartlari: 1440 px gövde yatay taşması yok', async ({ page }) => {
  await sartUclari(page, { yazma: (r) => r.fulfill({ status: 201, json: rezSart(SART_1) }) });
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(SAYFA.yol);
  await hazirBekle(page, SAYFA);
  expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
});
