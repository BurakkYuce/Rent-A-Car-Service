import { expect, test, type Page } from '@playwright/test';

import {
  apiGet,
  apiGetState,
  apiPost,
  oneCustomer,
  NO_ACTUAL,
  login,
  addDays,
  isoDay,
  ROOT,
  randomStart,
  select,
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
test.skip(NO_ACTUAL, 'gerçek backend ortamı yok (RACAR_E2E_KOK / RACAR_E2E_SIFRE)');
test.describe.configure({ mode: 'serial' });

const LOAN = '/api/ui/v1/arac-kredileri';
const INSTALLMENT = '/api/ui/v1/musteri-taksitleri';
const ORDER = '/api/ui/v1/arac-siparisleri';
const extra = () => String(Math.floor(Math.random() * 1e8)).padStart(8, '0');

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
  await login(page, ORTAM.gercekAdmin);
  const file = `E2E-KR-${extra()}`;

  await page.goto(`${ROOT}/app/arac-kredi`);
  await page.getByRole('button', { name: 'Yeni Kredi' }).click();
  const newItem = page.getByRole('region', { name: 'Yeni Kredi' });
  await newItem.getByRole('textbox', { name: 'Banka', exact: true }).fill('E2E Bankası');
  await newItem.getByRole('textbox', { name: 'Dosya No', exact: true }).fill(file);
  await newItem.getByRole('textbox', { name: 'Kredi Tutarı' }).fill('12000');
  await newItem.getByRole('textbox', { name: 'Taksit Sayısı' }).fill('12');
  await newItem.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText(/numaralı kredi kaydedildi\./)).toBeVisible();

  const list = await apiGet<Satirli<{ id: string; no: string; aylikTaksit: number | string }>>(
    page,
    LOAN,
    { dosyaNo: file },
  );
  expect(list.kayitlar).toHaveLength(1);
  const loanId = list.kayitlar[0]!.id;
  const no = list.kayitlar[0]!.no;
  expect(Number(list.kayitlar[0]!.aylikTaksit)).toBe(1000);

  // Taksit Öde (SPA, varsayılan Kasa) → 1. taksit.
  await page.goto(`${ROOT}/app/arac-kredi/${loanId}`);
  await page.getByRole('button', { name: 'Taksit Öde' }).click();
  await expect(page.getByText(/^1\. taksit ödendi \(Gider No /)).toBeVisible();
  const d1 = await apiGet<KrediDetay>(page, `${LOAN}/${loanId}`);
  expect(d1.odenenTaksit).toBe(1);

  // Defter: bu kredinin taksidi için Kasa'da TEK çıkış satırı, 1.000,00.
  const g = bugun();
  interface DefterSatiri {
    aciklama: string | null;
    borc: number | string;
    alacak: number | string;
  }
  const ledger = await apiGet<{ satirlar: Satirli<DefterSatiri> }>(
    page,
    '/api/ui/v1/raporlar/kasa-banka',
    { hesap: 'Kasa', bas: isoDay(g), bit: isoDay(g), tur: 'Gider', boyut: '200' },
  );
  const rows = ledger.satirlar.kayitlar.filter((s) =>
    s.aciklama?.startsWith(`Kredi taksiti ${no} #`),
  );
  expect(rows).toHaveLength(1);
  expect(Number(rows[0]!.alacak)).toBe(1000);
  expect(Number(rows[0]!.borc)).toBe(0);

  // Aynı sıra ikinci kez ödenmez (bayat sıra → reddedilir); ödenen sayısı değişmez.
  const repeat = await apiPost(page, `${LOAN}/${loanId}/taksit-ode`, { sira: 1, hesap: 'Kasa' });
  expect(repeat.ok()).toBe(false);
  expect((await apiGet<KrediDetay>(page, `${LOAN}/${loanId}`)).odenenTaksit).toBe(1);

  // İptal (SPA, onaylı).
  await page.reload();
  await page.getByRole('button', { name: 'Krediyi İptal Et' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Krediyi İptal Et' }).click();
  await expect(
    page.getByText(`${no} numaralı kredinin kalan taksitleri iptal edildi.`),
  ).toBeVisible();
  const d2 = await apiGet<KrediDetay>(page, `${LOAN}/${loanId}`);
  expect(d2.durum).toBe('Iptal');
  expect(d2.yetkiler.taksitOde).toBe(false);
  expect(
    (await apiPost(page, `${LOAN}/${loanId}/taksit-ode`, { sira: 2, hesap: 'Kasa' })).ok(),
  ).toBe(false);
  await expect(page.getByRole('button', { name: 'Taksit Öde' })).toHaveCount(0);
});

test('müşteri taksit: plan 1.000 / 3 → 333,33 + 333,33 + 333,34 → Ödendi → Geri Al; temizlik', async ({
  page,
}) => {
  await login(page, ORTAM.gercekAdmin);
  const customer = await oneCustomer(page);
  const first = randomStart();

  await page.goto(`${ROOT}/app/musteri-taksit`);
  const plan = page.getByRole('region', { name: 'Taksit Planı Üret' });
  await select(page, 'Müşteri', customer.etiket.slice(0, 4), customer.etiket, plan);
  await plan.getByRole('textbox', { name: 'Toplam Tutar' }).fill('1000');
  await plan.getByRole('textbox', { name: 'Taksit Sayısı' }).fill('3');
  const firstDue = plan.getByRole('textbox', { name: 'İlk Vade', exact: true });
  await firstDue.fill(
    `${String(first.gun).padStart(2, '0')}.${String(first.ay).padStart(2, '0')}.${first.yil}`,
  );
  await firstDue.blur();
  await plan.getByRole('button', { name: 'Plan Üret' }).click();
  await expect(page.getByText('3 taksitlik plan üretildi.')).toBeVisible();

  const query = {
    cariId: customer.id,
    vadeMin: isoDay(first),
    vadeMax: isoDay(addDays(first, 70)),
  };
  interface Satir {
    id: string;
    sira: number;
    taksitTutari: number | string;
    durum: string;
  }
  const rows = async () =>
    (await apiGet<Satirli<Satir>>(page, INSTALLMENT, { ...query, sirala: 'sira' })).kayitlar;
  const created = await rows();
  expect(created.map((s) => [s.sira, Number(s.taksitTutari)])).toEqual([
    [1, 333.33],
    [2, 333.33],
    [3, 333.34],
  ]);

  await page.goto(
    `${ROOT}/app/musteri-taksit?cariId=${customer.id}&vadeMin=${query.vadeMin}&vadeMax=${query.vadeMax}&sirala=sira`,
  );
  const row1 = page.getByRole('row').filter({ hasText: '333,33' }).first();
  await row1.getByRole('button', { name: 'Ödendi', exact: true }).click();
  await expect(page.getByText('1. taksit ödendi olarak işaretlendi.')).toBeVisible();
  await expect.poll(async () => (await rows())[0]!.durum).toBe('Odendi');
  await page
    .getByRole('row')
    .filter({ hasText: '333,33' })
    .first()
    .getByRole('button', { name: 'Geri Al' })
    .click();
  await expect(page.getByText('1. taksitin ödeme işareti geri alındı.')).toBeVisible();
  await expect.poll(async () => (await rows())[0]!.durum).toBe('Bekliyor');

  for (const s of await rows()) {
    const c = (await page.context().cookies(ROOT)).find((x) => x.name === 'XSRF-TOKEN');
    const remove = await page.context().request.delete(`${ROOT}${INSTALLMENT}/${s.id}`, {
      headers: {
        'X-XSRF-TOKEN': c ? decodeURIComponent(c.value) : '',
        'Idempotency-Key': crypto.randomUUID(),
      },
    });
    expect(remove.ok(), `taksit sil ${s.sira}: ${remove.status()}`).toBe(true);
  }
  expect(await rows()).toHaveLength(0);
});

async function newOrder(page: Page, file: string): Promise<string> {
  await page.goto(`${ROOT}/app/arac-siparis/yeni`);
  await page.getByRole('combobox', { name: 'Tedarikçi', exact: true }).fill('E2E Tedarik A.Ş.');
  await page.getByRole('textbox', { name: 'Dosya No', exact: true }).fill(file);
  await page.getByRole('textbox', { name: 'Adet', exact: true }).fill('2');
  await page.getByRole('textbox', { name: 'Birim Fiyat (resmi)' }).fill('750000');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/arac-siparis\/[0-9a-f-]{36}$/);
  return page.url().split('/').pop()!;
}

test('sipariş: Bekliyor → Onayla → Teslim Al; ikinci sipariş İptal (onaylı); geçersiz geçiş reddi', async ({
  page,
}) => {
  await login(page, ORTAM.gercekAdmin);
  interface Siparis {
    durum: string;
    toplam: number | string;
    yetkiler: Record<string, boolean>;
  }

  const a = await newOrder(page, `E2E-SP-${extra()}`);
  const s0 = await apiGet<Siparis>(page, `${ORDER}/${a}`);
  expect(s0.durum).toBe('Bekliyor');
  expect(Number(s0.toplam)).toBe(1_500_000); // 2 × 750.000 (elle)
  // Servisin tek geçiş tablosu: Bekliyor → Onaylandı | TeslimAlındı | İptal (Blazor Teslim Al'ı yalnız onaylıda
  // gösteriyordu; SPA sunucunun `yetkiler`ini izler — bilinçli fark, F6-parite.md).
  expect(s0.yetkiler).toMatchObject({ onayla: true, teslimAl: true, iptal: true });

  await page.getByRole('button', { name: 'Onayla', exact: true }).click();
  await expect
    .poll(async () => (await apiGet<Siparis>(page, `${ORDER}/${a}`)).durum)
    .toBe('Onaylandi');
  await page.getByRole('button', { name: 'Teslim Al', exact: true }).click();
  await expect
    .poll(async () => (await apiGet<Siparis>(page, `${ORDER}/${a}`)).durum)
    .toBe('TeslimAlindi');
  // Teslim alınmış sipariş terminal: geri onaylanamaz, iptal edilemez.
  expect((await apiPost(page, `${ORDER}/${a}/iptal`)).ok()).toBe(false);
  expect((await apiPost(page, `${ORDER}/${a}/onayla`)).ok()).toBe(false);
  expect((await apiGet<Siparis>(page, `${ORDER}/${a}`)).durum).toBe('TeslimAlindi');
  await expect(page.getByRole('button', { name: 'İptal', exact: true })).toHaveCount(0);

  const b = await newOrder(page, `E2E-SP-${extra()}`);
  await page.getByRole('button', { name: 'İptal', exact: true }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'İptal', exact: true }).click();
  await expect.poll(async () => (await apiGet<Siparis>(page, `${ORDER}/${b}`)).durum).toBe('Iptal');
  expect((await apiPost(page, `${ORDER}/${b}/onayla`)).ok()).toBe(false);
});

test('izin + kapsam: Operatör kredi formunu görür, şubesiz kredi açamaz, taksit ödeyemez; müşteri taksidi kapalı', async ({
  page,
  browser,
}) => {
  // Admin filo geneli (araçsız) kredi açar — operatörün kapsamı dışında.
  const admin = await browser.newPage();
  await login(admin, ORTAM.gercekAdmin);
  const r = await apiPost(admin, LOAN, {
    bankaAdi: 'E2E Kapsam Bankası',
    krediTutari: 1200,
    faizOran: 0,
    taksitSayisi: 12,
    dosyaNo: `E2E-KR-${extra()}`,
  });
  expect(r.status(), await r.text()).toBe(201);
  const { id } = (await r.json()) as { id: string };

  await login(page, ORTAM.gercekOperator);
  expect(await apiGetState(page, INSTALLMENT)).toBe(403);
  await page.goto(`${ROOT}/app/musteri-taksit`);
  await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();

  // Oluşturma OperationsWrite: form açık; ama şubeye bağlı kullanıcı KENDİ şubesinin aracını seçmek zorunda.
  await page.goto(`${ROOT}/app/arac-kredi`);
  await expect(page.getByRole('button', { name: 'Yeni Kredi' })).toBeVisible();
  const op = await apiPost(page, LOAN, { bankaAdi: 'X', krediTutari: 1200, taksitSayisi: 12 });
  expect(op.status()).toBe(400);
  expect(((await op.json()) as { errors?: Record<string, unknown> }).errors).toHaveProperty(
    'vehicleId',
  );
  // Filo geneli kredi kapsam dışı; ödeme FinanceWrite, iptal OperationsDelete ister.
  expect([403, 404]).toContain(await apiGetState(page, `${LOAN}/${id}`));
  const list = await apiGet<Satirli<{ id: string }>>(page, LOAN);
  expect(list.kayitlar.map((k) => k.id)).not.toContain(id);
  expect(
    (await apiPost(page, `${LOAN}/${id}/taksit-ode`, { sira: 1, hesap: 'Kasa' })).status(),
  ).toBe(403);
  expect((await apiPost(page, `${LOAN}/${id}/iptal`)).status()).toBe(403);

  // Temizlik: Admin iptal eder (ödeme yok; defter kaydı oluşmadı).
  expect((await apiPost(admin, `${LOAN}/${id}/iptal`)).ok()).toBe(true);
  await admin.close();
});

test('izin: Muhasebe taksit öder yetkisini görür ama kredi açamaz; BAF/hasar/sipariş formu kapalı', async ({
  page,
  browser,
}) => {
  const admin = await browser.newPage();
  await login(admin, ORTAM.gercekAdmin);
  const r = await apiPost(admin, LOAN, {
    bankaAdi: 'E2E Muhasebe Bankası',
    krediTutari: 1200,
    faizOran: 0,
    taksitSayisi: 12,
    dosyaNo: `E2E-KR-${extra()}`,
  });
  expect(r.status(), await r.text()).toBe(201);
  const { id } = (await r.json()) as { id: string };

  await login(page, ORTAM.gercekMuhasebe);
  // Ödeme FinanceWrite (Muhasebe'de var), iptal OperationsDelete (yok) — sunucu bayrakları + SPA düğmeleri.
  expect((await apiGet<KrediDetay>(page, `${LOAN}/${id}`)).yetkiler).toEqual({
    taksitOde: true,
    iptal: false,
  });
  await page.goto(`${ROOT}/app/arac-kredi/${id}`);
  await expect(page.getByRole('button', { name: 'Taksit Öde' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Krediyi İptal Et' })).toHaveCount(0);
  expect((await apiPost(admin, `${LOAN}/${id}/iptal`)).ok()).toBe(true); // temizlik: ödemesiz
  await admin.close();

  expect(
    (await apiPost(page, LOAN, { bankaAdi: 'X', krediTutari: 1, taksitSayisi: 1 })).status(),
  ).toBe(403);
  expect(await apiGetState(page, INSTALLMENT)).toBe(200);
  expect(await apiGetState(page, '/api/ui/v1/baflar')).toBe(403);
  expect(await apiGetState(page, '/api/ui/v1/hasar-dosyalari')).toBe(403);

  await page.goto(`${ROOT}/app/arac-kredi`);
  await expect(page.getByRole('button', { name: 'Yeni Kredi' })).toHaveCount(0);
  for (const path of ['/app/baf', '/app/hasar', '/app/arac-siparis/yeni']) {
    await page.goto(`${ROOT}${path}`);
    await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();
    await expect(page).toHaveURL(`${ROOT}/app/`);
  }
});
