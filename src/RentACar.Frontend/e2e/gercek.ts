import { expect, type APIResponse, type Cookie, type Locator, type Page } from '@playwright/test';

import { ORTAM } from './ortam';

/**
 * Gerçek backend e2e yardımcıları (F5.3). Sahte `/api/ui/v1` YOK: istekler çalışan Web sunucusuna gider,
 * SPA o sunucunun `/app`'inden yüklenir (`Spa__Dizin`). Ortam yoksa spec'ler atlanır (`GERCEK_YOK`).
 *
 * Veri çakışmasın diye her koşum rastgele, ileri bir pencere seçer (rezervasyon geçmişe açılamaz) ve
 * aracı sunucunun müsaitlik ucundan alır; oluşturulan rezervasyon/kira koşum sonunda İPTAL edilir
 * (silme yok — CLAUDE.md: mali ve operasyonel kayıtlar iptal/ters kayıtla kapanır).
 */
export const KOK = ORTAM.gercekKok.replace(/\/$/, '');
export const GERCEK_YOK = KOK === '' || ORTAM.gercekSifre === '';

async function xsrf(page: Page): Promise<string> {
  const c = (await page.context().cookies(KOK)).find((x) => x.name === 'XSRF-TOKEN');
  return c ? decodeURIComponent(c.value) : '';
}

interface Ben {
  readonly pilot: boolean;
}

/** Kullanıcı başına TEK giriş: `/login` hız sınırı (429) her testte yeniden girişe izin vermez. */
const oturumlar = new Map<string, { cerezler: Cookie[]; ben: Ben }>();

/** SPA giriş formunun kullandığı AYNI uçlarla girer (`oturum/xsrf` → `oturum/giris`), aynı çerez kavanozu. */
export async function gir(page: Page, kullanici: string): Promise<Ben> {
  const onceki = oturumlar.get(kullanici);
  if (onceki) {
    await page.context().addCookies(onceki.cerezler);
    return onceki.ben;
  }
  const r = page.context().request;
  expect((await r.get(`${KOK}/api/ui/v1/oturum/xsrf`)).status()).toBe(204);
  const yanit = await r.post(`${KOK}/api/ui/v1/oturum/giris`, {
    headers: { 'X-XSRF-TOKEN': await xsrf(page) },
    data: { firma: ORTAM.gercekFirma, kullanici, sifre: ORTAM.gercekSifre },
  });
  expect(yanit.ok(), `giriş ${kullanici}: HTTP ${yanit.status()}`).toBe(true);
  const ben = (await yanit.json()) as Ben;
  expect(ben.pilot, 'kiracı pilotta olmalı (yoksa /api/ui kapalı)').toBe(true);
  oturumlar.set(kullanici, { cerezler: await page.context().cookies(KOK), ben });
  return ben;
}

export async function apiGet<T>(page: Page, yol: string, sorgu?: Record<string, string>): Promise<T> {
  const y = await page.context().request.get(`${KOK}${yol}`, { params: sorgu });
  expect(y.ok(), `GET ${yol}: HTTP ${y.status()} ${await y.text()}`).toBe(true);
  return (await y.json()) as T;
}

export async function apiGetDurum(page: Page, yol: string): Promise<number> {
  return (await page.context().request.get(`${KOK}${yol}`)).status();
}

export async function apiPost(page: Page, yol: string, govde?: unknown): Promise<APIResponse> {
  return page.context().request.post(`${KOK}${yol}`, {
    headers: { 'X-XSRF-TOKEN': await xsrf(page), 'Idempotency-Key': crypto.randomUUID() },
    ...(govde === undefined ? {} : { data: govde }),
  });
}

/** Takvim günü (İstanbul) — yalnız yıl/ay/gün taşır; saat yok. */
export interface Gun {
  readonly yil: number;
  readonly ay: number; // 1–12
  readonly gun: number;
}

export function gunEkle(g: Gun, n: number): Gun {
  const d = new Date(Date.UTC(g.yil, g.ay - 1, g.gun + n));
  return { yil: d.getUTCFullYear(), ay: d.getUTCMonth() + 1, gun: d.getUTCDate() };
}

const iki = (n: number) => String(n).padStart(2, '0');
export const isoGun = (g: Gun) => `${g.yil}-${iki(g.ay)}-${iki(g.gun)}`;
export const trGun = (g: Gun) => `${iki(g.gun)}.${iki(g.ay)}.${g.yil}`;
export const isoAy = (g: Gun) => `${g.yil}-${iki(g.ay)}`;

/**
 * Rastgele ileri pencere başlangıcı: bugünden (İstanbul) 40–300 gün sonra (rezervasyon en çok 1 yıl ileri —
 * `TarihPolitikasi`), ayın 3–24'ü arası (3 günlük pencere + dönüş günü aynı ayda kalsın — takvim hücre sayısı
 * tek ayda doğrulanır).
 */
export function rastgeleBaslangic(): Gun {
  const simdi = new Date(Date.now() + 3 * 3600_000);
  const bugun = { yil: simdi.getUTCFullYear(), ay: simdi.getUTCMonth() + 1, gun: simdi.getUTCDate() };
  const aday = gunEkle(bugun, 40 + Math.floor(Math.random() * 260));
  return { ...aday, gun: 3 + Math.floor(Math.random() * 22) };
}

interface MusaitArac {
  readonly id: string;
  readonly plaka: string;
}

/** Pencerede (09:00 → +3 gün 09:00) SUNUCUNUN müsait dediği araçlardan rastgele biri. */
export async function musaitArac(page: Page, bas: Gun): Promise<MusaitArac> {
  const y = await apiGet<{ araclar: MusaitArac[] }>(page, '/api/ui/v1/musaitlik', {
    basGun: isoGun(bas),
    gun: '3',
    basSaat: '09:00',
    bitSaat: '09:00',
  });
  expect(y.araclar.length, 'pencerede müsait araç yok').toBeGreaterThan(0);
  return y.araclar[Math.floor(Math.random() * y.araclar.length)]!;
}

/** Seçim ucundan bir müşteri (etiket = combobox seçeneğinin adı). */
export async function birMusteri(page: Page): Promise<{ id: string; etiket: string }> {
  const m = await apiGet<{ id: string; etiket: string }[]>(page, '/api/ui/v1/secim/musteri', {
    limit: '10',
  });
  expect(m.length).toBeGreaterThan(0);
  return m[Math.floor(Math.random() * m.length)]!;
}

/** `rc-arama-secim` (typeahead): yaz → seçeneği tıkla. `kapsam`: aynı adlı süzgeç kutusu varsa form bölgesi. */
export async function sec(
  page: Page,
  alan: string,
  yazi: string,
  secenek: RegExp | string,
  kapsam: Locator | Page = page,
) {
  const kutu = kapsam.getByRole('combobox', { name: alan, exact: true });
  await kutu.click();
  await kutu.fill(yazi);
  await page.getByRole('option', { name: secenek }).first().click();
}

/** `rc-tarih-saat-secici`nin gün kutusu (saat varsayılan 09:00 kalır). */
export async function gunYaz(page: Page, alan: string, g: Gun) {
  const kutu = page.getByRole('textbox', { name: alan, exact: true });
  await kutu.fill(trGun(g));
  await kutu.blur();
}
