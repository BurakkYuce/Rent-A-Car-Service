import { expect, test, type Page } from '@playwright/test';

import {
  apiGet,
  apiGetDurum,
  apiPost,
  birMusteri,
  GERCEK_YOK,
  gir,
  gunEkle,
  isoGun,
  KOK,
  rastgeleBaslangic,
  sec,
  type Gun,
} from './gercek';
import { ORTAM } from './ortam';

/**
 * F6.3 — GERÇEK backend: araç finans ekranları.
 * - Kredi (SPA): oluştur → "Taksit Öde" TEK ödeme → gider (Finansman) + TEK Kasa defter satırı → iptal; iptalden
 *   sonra ödeme reddedilir. Beklenen tutar elle: 12.000 ₺, faiz 0, 12 taksit → 1.000,00 ₺.
 * - Müşteri taksit (SPA): 1.000 ₺ / 3 taksit planı → 333,33 + 333,33 + 333,34 (kalan-yöntemi, elle) → Ödendi → Geri Al.
 * - Sipariş (SPA): Bekliyor → Onayla → Teslim Al; ikinci sipariş İptal (onaylı); geçersiz geçiş reddi.
 * - İzin farkları: Operatör kredi açar ama taksit ödeyemez, müşteri taksidini göremez; Muhasebe taksit öder ama
 *   kredi açamaz, BAF/hasar ekranlarına giremez.
 * Temizlik: kredi İPTAL (ödenen taksidin gideri/defteri değişmez — değişmez mali kayıt), müşteri taksitleri silinir
 * (deftere yazmayan takip kaydı), siparişler iptal/teslim durumunda kalır (defter postlamaz).
 */
test.skip(GERCEK_YOK, 'gerçek backend ortamı yok (RACAR_E2E_KOK / RACAR_E2E_SIFRE)');
test.describe.configure({ mode: 'serial' });

const KREDI = '/api/ui/v1/arac-kredileri';
const TAKSIT = '/api/ui/v1/musteri-taksitleri';
const SIPARIS = '/api/ui/v1/arac-siparisleri';
const ek = () => String(Math.floor(Math.random() * 1e8)).padStart(8, '0');

function bugun(): Gun {
  const s = new Date(Date.now() + 3 * 3600_000);
  return { yil: s.getUTCFullYear(), ay: s.getUTCMonth() + 1, gun: s.getUTCDate() };
}

interface KrediDetay {
  readonly id: string;
  readonly no: string;
  readonly durum: string;
  readonly odenenTaksit: number;
  readonly yetkiler: { readonly taksitOde: boolean; readonly iptal: boolean };
}
interface Satirli<T> {
  readonly kayitlar: T[];
}

test('kredi: oluştur → Taksit Öde (tek gider + tek Kasa defter satırı) → iptal; iptalden sonra ödeme yok', async ({
  page,
}) => {
  await gir(page, ORTAM.gercekAdmin);
  const dosya = `E2E-KR-${ek()}`;

  await page.goto(`${KOK}/app/arac-kredi`);
  await page.getByRole('button', { name: 'Yeni Kredi' }).click();
  const yeni = page.getByRole('region', { name: 'Yeni Kredi' });
  await yeni.getByRole('textbox', { name: 'Banka', exact: true }).fill('E2E Bankası');
  await yeni.getByRole('textbox', { name: 'Dosya No', exact: true }).fill(dosya);
  await yeni.getByRole('textbox', { name: 'Kredi Tutarı' }).fill('12000');
  await yeni.getByRole('textbox', { name: 'Taksit Sayısı' }).fill('12');
  await yeni.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText(/numaralı kredi kaydedildi\./)).toBeVisible();

  const liste = await apiGet<Satirli<{ id: string; no: string; aylikTaksit: number | string }>>(
    page,
    KREDI,
    { dosyaNo: dosya },
  );
  expect(liste.kayitlar).toHaveLength(1);
  const krediId = liste.kayitlar[0]!.id;
  const no = liste.kayitlar[0]!.no;
  expect(Number(liste.kayitlar[0]!.aylikTaksit)).toBe(1000);

  // Taksit Öde (SPA, varsayılan Kasa) → 1. taksit.
  await page.goto(`${KOK}/app/arac-kredi/${krediId}`);
  await page.getByRole('button', { name: 'Taksit Öde' }).click();
  await expect(page.getByText(/^1\. taksit ödendi \(Gider No /)).toBeVisible();
  const d1 = await apiGet<KrediDetay>(page, `${KREDI}/${krediId}`);
  expect(d1.odenenTaksit).toBe(1);

  // Defter: bu kredinin taksidi için Kasa'da TEK çıkış satırı, 1.000,00.
  const g = bugun();
  interface DefterSatiri {
    aciklama: string | null;
    borc: number | string;
    alacak: number | string;
  }
  const defter = await apiGet<{ satirlar: Satirli<DefterSatiri> }>(
    page,
    '/api/ui/v1/raporlar/kasa-banka',
    { hesap: 'Kasa', bas: isoGun(g), bit: isoGun(g), tur: 'Gider', boyut: '200' },
  );
  const satirlar = defter.satirlar.kayitlar.filter((s) =>
    s.aciklama?.startsWith(`Kredi taksiti ${no} #`),
  );
  expect(satirlar).toHaveLength(1);
  expect(Number(satirlar[0]!.alacak)).toBe(1000);
  expect(Number(satirlar[0]!.borc)).toBe(0);

  // Aynı sıra ikinci kez ödenmez (bayat sıra → reddedilir); ödenen sayısı değişmez.
  const tekrar = await apiPost(page, `${KREDI}/${krediId}/taksit-ode`, { sira: 1, hesap: 'Kasa' });
  expect(tekrar.ok()).toBe(false);
  expect((await apiGet<KrediDetay>(page, `${KREDI}/${krediId}`)).odenenTaksit).toBe(1);

  // İptal (SPA, onaylı).
  await page.reload();
  await page.getByRole('button', { name: 'Krediyi İptal Et' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Krediyi İptal Et' }).click();
  await expect(
    page.getByText(`${no} numaralı kredinin kalan taksitleri iptal edildi.`),
  ).toBeVisible();
  const d2 = await apiGet<KrediDetay>(page, `${KREDI}/${krediId}`);
  expect(d2.durum).toBe('Iptal');
  expect(d2.yetkiler.taksitOde).toBe(false);
  expect(
    (await apiPost(page, `${KREDI}/${krediId}/taksit-ode`, { sira: 2, hesap: 'Kasa' })).ok(),
  ).toBe(false);
  await expect(page.getByRole('button', { name: 'Taksit Öde' })).toHaveCount(0);
});

test('müşteri taksit: plan 1.000 / 3 → 333,33 + 333,33 + 333,34 → Ödendi → Geri Al; temizlik', async ({
  page,
}) => {
  await gir(page, ORTAM.gercekAdmin);
  const musteri = await birMusteri(page);
  const ilk = rastgeleBaslangic();

  await page.goto(`${KOK}/app/musteri-taksit`);
  const plan = page.getByRole('region', { name: 'Taksit Planı Üret' });
  await sec(page, 'Müşteri', musteri.etiket.slice(0, 4), musteri.etiket, plan);
  await plan.getByRole('textbox', { name: 'Toplam Tutar' }).fill('1000');
  await plan.getByRole('textbox', { name: 'Taksit Sayısı' }).fill('3');
  const ilkVade = plan.getByRole('textbox', { name: 'İlk Vade', exact: true });
  await ilkVade.fill(
    `${String(ilk.gun).padStart(2, '0')}.${String(ilk.ay).padStart(2, '0')}.${ilk.yil}`,
  );
  await ilkVade.blur();
  await plan.getByRole('button', { name: 'Plan Üret' }).click();
  await expect(page.getByText('3 taksitlik plan üretildi.')).toBeVisible();

  const sorgu = { cariId: musteri.id, vadeMin: isoGun(ilk), vadeMax: isoGun(gunEkle(ilk, 70)) };
  interface Satir {
    id: string;
    sira: number;
    taksitTutari: number | string;
    durum: string;
  }
  const satirlar = async () =>
    (await apiGet<Satirli<Satir>>(page, TAKSIT, { ...sorgu, sirala: 'sira' })).kayitlar;
  const olusan = await satirlar();
  expect(olusan.map((s) => [s.sira, Number(s.taksitTutari)])).toEqual([
    [1, 333.33],
    [2, 333.33],
    [3, 333.34],
  ]);

  await page.goto(
    `${KOK}/app/musteri-taksit?cariId=${musteri.id}&vadeMin=${sorgu.vadeMin}&vadeMax=${sorgu.vadeMax}&sirala=sira`,
  );
  const satir1 = page.getByRole('row').filter({ hasText: '333,33' }).first();
  await satir1.getByRole('button', { name: 'Ödendi', exact: true }).click();
  await expect(page.getByText('1. taksit ödendi olarak işaretlendi.')).toBeVisible();
  await expect.poll(async () => (await satirlar())[0]!.durum).toBe('Odendi');
  await page
    .getByRole('row')
    .filter({ hasText: '333,33' })
    .first()
    .getByRole('button', { name: 'Geri Al' })
    .click();
  await expect(page.getByText('1. taksitin ödeme işareti geri alındı.')).toBeVisible();
  await expect.poll(async () => (await satirlar())[0]!.durum).toBe('Bekliyor');

  for (const s of await satirlar()) {
    const c = (await page.context().cookies(KOK)).find((x) => x.name === 'XSRF-TOKEN');
    const sil = await page.context().request.delete(`${KOK}${TAKSIT}/${s.id}`, {
      headers: {
        'X-XSRF-TOKEN': c ? decodeURIComponent(c.value) : '',
        'Idempotency-Key': crypto.randomUUID(),
      },
    });
    expect(sil.ok(), `taksit sil ${s.sira}: ${sil.status()}`).toBe(true);
  }
  expect(await satirlar()).toHaveLength(0);
});

async function yeniSiparis(page: Page, dosya: string): Promise<string> {
  await page.goto(`${KOK}/app/arac-siparis/yeni`);
  await page.getByRole('combobox', { name: 'Tedarikçi', exact: true }).fill('E2E Tedarik A.Ş.');
  await page.getByRole('textbox', { name: 'Dosya No', exact: true }).fill(dosya);
  await page.getByRole('textbox', { name: 'Adet', exact: true }).fill('2');
  await page.getByRole('textbox', { name: 'Birim Fiyat (resmi)' }).fill('750000');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/arac-siparis\/[0-9a-f-]{36}$/);
  return page.url().split('/').pop()!;
}

test('sipariş: Bekliyor → Onayla → Teslim Al; ikinci sipariş İptal (onaylı); geçersiz geçiş reddi', async ({
  page,
}) => {
  await gir(page, ORTAM.gercekAdmin);
  interface Siparis {
    durum: string;
    toplam: number | string;
    yetkiler: Record<string, boolean>;
  }

  const a = await yeniSiparis(page, `E2E-SP-${ek()}`);
  const s0 = await apiGet<Siparis>(page, `${SIPARIS}/${a}`);
  expect(s0.durum).toBe('Bekliyor');
  expect(Number(s0.toplam)).toBe(1_500_000); // 2 × 750.000 (elle)
  // Servisin tek geçiş tablosu: Bekliyor → Onaylandı | TeslimAlındı | İptal (Blazor Teslim Al'ı yalnız onaylıda
  // gösteriyordu; SPA sunucunun `yetkiler`ini izler — bilinçli fark, F6-parite.md).
  expect(s0.yetkiler).toMatchObject({ onayla: true, teslimAl: true, iptal: true });

  await page.getByRole('button', { name: 'Onayla', exact: true }).click();
  await expect
    .poll(async () => (await apiGet<Siparis>(page, `${SIPARIS}/${a}`)).durum)
    .toBe('Onaylandi');
  await page.getByRole('button', { name: 'Teslim Al', exact: true }).click();
  await expect
    .poll(async () => (await apiGet<Siparis>(page, `${SIPARIS}/${a}`)).durum)
    .toBe('TeslimAlindi');
  // Teslim alınmış sipariş terminal: geri onaylanamaz, iptal edilemez.
  expect((await apiPost(page, `${SIPARIS}/${a}/iptal`)).ok()).toBe(false);
  expect((await apiPost(page, `${SIPARIS}/${a}/onayla`)).ok()).toBe(false);
  expect((await apiGet<Siparis>(page, `${SIPARIS}/${a}`)).durum).toBe('TeslimAlindi');
  await expect(page.getByRole('button', { name: 'İptal', exact: true })).toHaveCount(0);

  const b = await yeniSiparis(page, `E2E-SP-${ek()}`);
  await page.getByRole('button', { name: 'İptal', exact: true }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'İptal', exact: true }).click();
  await expect
    .poll(async () => (await apiGet<Siparis>(page, `${SIPARIS}/${b}`)).durum)
    .toBe('Iptal');
  expect((await apiPost(page, `${SIPARIS}/${b}/onayla`)).ok()).toBe(false);
});

test('izin + kapsam: Operatör kredi formunu görür, şubesiz kredi açamaz, taksit ödeyemez; müşteri taksidi kapalı', async ({
  page,
  browser,
}) => {
  // Admin filo geneli (araçsız) kredi açar — operatörün kapsamı dışında.
  const admin = await browser.newPage();
  await gir(admin, ORTAM.gercekAdmin);
  const r = await apiPost(admin, KREDI, {
    bankaAdi: 'E2E Kapsam Bankası',
    krediTutari: 1200,
    faizOran: 0,
    taksitSayisi: 12,
    dosyaNo: `E2E-KR-${ek()}`,
  });
  expect(r.status(), await r.text()).toBe(201);
  const { id } = (await r.json()) as { id: string };

  await gir(page, ORTAM.gercekOperator);
  expect(await apiGetDurum(page, TAKSIT)).toBe(403);
  await page.goto(`${KOK}/app/musteri-taksit`);
  await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();

  // Oluşturma OperationsWrite: form açık; ama şubeye bağlı kullanıcı KENDİ şubesinin aracını seçmek zorunda.
  await page.goto(`${KOK}/app/arac-kredi`);
  await expect(page.getByRole('button', { name: 'Yeni Kredi' })).toBeVisible();
  const op = await apiPost(page, KREDI, { bankaAdi: 'X', krediTutari: 1200, taksitSayisi: 12 });
  expect(op.status()).toBe(400);
  expect(((await op.json()) as { errors?: Record<string, unknown> }).errors).toHaveProperty(
    'vehicleId',
  );
  // Filo geneli kredi kapsam dışı; ödeme FinanceWrite, iptal OperationsDelete ister.
  expect([403, 404]).toContain(await apiGetDurum(page, `${KREDI}/${id}`));
  const liste = await apiGet<Satirli<{ id: string }>>(page, KREDI);
  expect(liste.kayitlar.map((k) => k.id)).not.toContain(id);
  expect(
    (await apiPost(page, `${KREDI}/${id}/taksit-ode`, { sira: 1, hesap: 'Kasa' })).status(),
  ).toBe(403);
  expect((await apiPost(page, `${KREDI}/${id}/iptal`)).status()).toBe(403);

  // Temizlik: Admin iptal eder (ödeme yok; defter kaydı oluşmadı).
  expect((await apiPost(admin, `${KREDI}/${id}/iptal`)).ok()).toBe(true);
  await admin.close();
});

test('izin: Muhasebe taksit öder yetkisini görür ama kredi açamaz; BAF/hasar/sipariş formu kapalı', async ({
  page,
  browser,
}) => {
  const admin = await browser.newPage();
  await gir(admin, ORTAM.gercekAdmin);
  const r = await apiPost(admin, KREDI, {
    bankaAdi: 'E2E Muhasebe Bankası',
    krediTutari: 1200,
    faizOran: 0,
    taksitSayisi: 12,
    dosyaNo: `E2E-KR-${ek()}`,
  });
  expect(r.status(), await r.text()).toBe(201);
  const { id } = (await r.json()) as { id: string };

  await gir(page, ORTAM.gercekMuhasebe);
  // Ödeme FinanceWrite (Muhasebe'de var), iptal OperationsDelete (yok) — sunucu bayrakları + SPA düğmeleri.
  expect((await apiGet<KrediDetay>(page, `${KREDI}/${id}`)).yetkiler).toEqual({
    taksitOde: true,
    iptal: false,
  });
  await page.goto(`${KOK}/app/arac-kredi/${id}`);
  await expect(page.getByRole('button', { name: 'Taksit Öde' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Krediyi İptal Et' })).toHaveCount(0);
  expect((await apiPost(admin, `${KREDI}/${id}/iptal`)).ok()).toBe(true); // temizlik: ödemesiz
  await admin.close();

  expect(
    (await apiPost(page, KREDI, { bankaAdi: 'X', krediTutari: 1, taksitSayisi: 1 })).status(),
  ).toBe(403);
  expect(await apiGetDurum(page, TAKSIT)).toBe(200);
  expect(await apiGetDurum(page, '/api/ui/v1/baflar')).toBe(403);
  expect(await apiGetDurum(page, '/api/ui/v1/hasar-dosyalari')).toBe(403);

  await page.goto(`${KOK}/app/arac-kredi`);
  await expect(page.getByRole('button', { name: 'Yeni Kredi' })).toHaveCount(0);
  for (const yol of ['/app/baf', '/app/hasar', '/app/arac-siparis/yeni']) {
    await page.goto(`${KOK}${yol}`);
    await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();
    await expect(page).toHaveURL(`${KOK}/app/`);
  }
});
