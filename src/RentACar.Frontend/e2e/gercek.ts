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
export const ROOT = ORTAM.gercekKok.replace(/\/$/, '');
export const NO_ACTUAL = ROOT === '' || ORTAM.gercekSifre === '';

async function xsrf(page: Page): Promise<string> {
  const c = (await page.context().cookies(ROOT)).find((x) => x.name === 'XSRF-TOKEN');
  return c ? decodeURIComponent(c.value) : '';
}

interface Ben {
  readonly pilot: boolean;
}

/** Kullanıcı başına TEK giriş: `/login` hız sınırı (429) her testte yeniden girişe izin vermez. */
const sessions = new Map<string, { cerezler: Cookie[]; ben: Ben }>();

/** SPA giriş formunun kullandığı AYNI uçlarla girer (`oturum/xsrf` → `oturum/giris`), aynı çerez kavanozu. */
export async function login(page: Page, user: string): Promise<Ben> {
  const previous = sessions.get(user);
  if (previous) {
    await page.context().addCookies(previous.cerezler);
    return previous.ben;
  }
  const r = page.context().request;
  expect((await r.get(`${ROOT}/api/ui/v1/oturum/xsrf`)).status()).toBe(204);
  const response = await r.post(`${ROOT}/api/ui/v1/oturum/giris`, {
    headers: { 'X-XSRF-TOKEN': await xsrf(page) },
    data: { firma: ORTAM.gercekFirma, kullanici: user, sifre: ORTAM.gercekSifre },
  });
  expect(response.ok(), `giriş ${user}: HTTP ${response.status()}`).toBe(true);
  const ben = (await response.json()) as Ben;
  expect(ben.pilot, 'kiracı pilotta olmalı (yoksa /api/ui kapalı)').toBe(true);
  sessions.set(user, { cerezler: await page.context().cookies(ROOT), ben });
  return ben;
}

export async function apiGet<T>(
  page: Page,
  path: string,
  query?: Record<string, string>,
): Promise<T> {
  const y = await page.context().request.get(`${ROOT}${path}`, { params: query });
  expect(y.ok(), `GET ${path}: HTTP ${y.status()} ${await y.text()}`).toBe(true);
  return (await y.json()) as T;
}

export async function apiGetState(page: Page, path: string): Promise<number> {
  return (await page.context().request.get(`${ROOT}${path}`)).status();
}

export async function apiPost(page: Page, path: string, body?: unknown): Promise<APIResponse> {
  return page.context().request.post(`${ROOT}${path}`, {
    headers: { 'X-XSRF-TOKEN': await xsrf(page), 'Idempotency-Key': crypto.randomUUID() },
    ...(body === undefined ? {} : { data: body }),
  });
}

/** Takvim günü (İstanbul) — yalnız yıl/ay/gün taşır; saat yok. */
export interface Gun {
  readonly yil: number;
  readonly ay: number; // 1–12
  readonly gun: number;
}

export function addDays(g: Gun, n: number): Gun {
  const d = new Date(Date.UTC(g.yil, g.ay - 1, g.gun + n));
  return { yil: d.getUTCFullYear(), ay: d.getUTCMonth() + 1, gun: d.getUTCDate() };
}

const iki = (n: number) => String(n).padStart(2, '0');
export const isoDay = (g: Gun) => `${g.yil}-${iki(g.ay)}-${iki(g.gun)}`;
export const trDay = (g: Gun) => `${iki(g.gun)}.${iki(g.ay)}.${g.yil}`;
export const isoMonth = (g: Gun) => `${g.yil}-${iki(g.ay)}`;

/**
 * Rastgele ileri pencere başlangıcı: bugünden (İstanbul) 40–300 gün sonra (rezervasyon en çok 1 yıl ileri —
 * `TarihPolitikasi`), ayın 3–24'ü arası (3 günlük pencere + dönüş günü aynı ayda kalsın — takvim hücre sayısı
 * tek ayda doğrulanır).
 */
export function randomStart(): Gun {
  const now = new Date(Date.now() + 3 * 3600_000);
  const today = {
    yil: now.getUTCFullYear(),
    ay: now.getUTCMonth() + 1,
    gun: now.getUTCDate(),
  };
  const candidate = addDays(today, 40 + Math.floor(Math.random() * 260));
  return { ...candidate, gun: 3 + Math.floor(Math.random() * 22) };
}

interface MusaitArac {
  readonly id: string;
  readonly plaka: string;
}

/** Pencerede (09:00 → +3 gün 09:00) SUNUCUNUN müsait dediği araçlardan rastgele biri. */
export async function availableVehicle(page: Page, start: Gun): Promise<MusaitArac> {
  const y = await apiGet<{ araclar: MusaitArac[] }>(page, '/api/ui/v1/musaitlik', {
    basGun: isoDay(start),
    gun: '3',
    basSaat: '09:00',
    bitSaat: '09:00',
  });
  expect(y.araclar.length, 'pencerede müsait araç yok').toBeGreaterThan(0);
  return y.araclar[Math.floor(Math.random() * y.araclar.length)]!;
}

/** Seçim ucundan bir müşteri (etiket = combobox seçeneğinin adı). */
export async function oneCustomer(page: Page): Promise<{ id: string; etiket: string }> {
  const m = await apiGet<{ id: string; etiket: string }[]>(page, '/api/ui/v1/secim/musteri', {
    limit: '10',
  });
  expect(m.length).toBeGreaterThan(0);
  return m[Math.floor(Math.random() * m.length)]!;
}

/** `rc-arama-secim` (typeahead): yaz → seçeneği tıkla. `kapsam`: aynı adlı süzgeç kutusu varsa form bölgesi. */
export async function select(
  page: Page,
  alan: string,
  text: string,
  option: RegExp | string,
  scope: Locator | Page = page,
) {
  const box = scope.getByRole('combobox', { name: alan, exact: true });
  await box.click();
  await box.fill(text);
  await page.getByRole('option', { name: option }).first().click();
}

/** `rc-tarih-saat-secici`nin gün kutusu (saat varsayılan 09:00 kalır). */
export async function writeDay(page: Page, alan: string, g: Gun) {
  const box = page.getByRole('textbox', { name: alan, exact: true });
  await box.fill(trDay(g));
  await box.blur();
}
