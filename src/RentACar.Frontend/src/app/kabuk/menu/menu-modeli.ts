import type { MenuResponse } from '@core/api/ui-tipleri';
import { trSearchKey } from '@core/metin/tr-normalize';

/** `GET /api/ui/v1/menu` öğesi (sunucu izin ve modüle göre süzmüş olarak gönderir). */
export type MenuItem = MenuResponse['ogeler'][number];

/** SPA sayfası (router) ya da Blazor ekranı (tam sayfa, `/app` dışı). */
export type MenuTarget =
  | { readonly tur: 'spa'; readonly yol: string }
  | { readonly tur: 'blazor'; readonly adres: string };

export interface MenuKaydi {
  /** Menü içinde tekil (aynı rota birden çok grupta olabilir). */
  readonly kimlik: string;
  readonly etiket: string;
  readonly grup: string;
  readonly sira: number;
  readonly hizli: boolean;
  readonly rozetKodu: string | null;
  readonly hedef: MenuTarget;
  /** `trAramaAnahtari(etiket)`. */
  readonly aramaEtiketi: string;
  readonly aramaGrubu: string;
}

export type MenuBlock =
  | { readonly tur: 'oge'; readonly kayit: MenuKaydi }
  | { readonly tur: 'grup'; readonly ad: string; readonly kayitlar: readonly MenuKaydi[] };

export interface MenuModeli {
  /** Hızlı bağlantılar (menünün üstünde). */
  readonly hizli: readonly MenuKaydi[];
  /** Gruplar ve grupsuz öğeler, `sira` düzeninde (grup ilk öğesinin sırasıyla yer alır). */
  readonly bloklar: readonly MenuBlock[];
  /** Tüm öğeler (palet araması), `sira` düzeninde. */
  readonly tumu: readonly MenuKaydi[];
  /** Rozet kodu → sayaç. */
  readonly rozetler: ReadonlyMap<string, number>;
}

/** Menü kaydında `sahip` değerleri. Bilinmeyen sahip Blazor sayılır (tam sayfa — güvenli varsayılan). */
export const OWNER_SPA = 'spa';

/** SPA'nın kök yolu; `spa` öğesinin rotası bu önekle de gelebilir. */
const SPA_PREFIX = '/app';

/**
 * Öğenin hedefi. Rota yalnız kök-göreli yol olabilir (`/` ile başlar, `//` ya da `\` içermez — açık
 * yönlendirme kapısı kapalı); değilse öğe gösterilmez (`null`). `spa` rotası router yoludur (`/kiralar`,
 * `/app/kiralar` da kabul); Blazor rotası sunucudaki adresidir, olduğu gibi açılır.
 */
export function menuTarget(oge: Pick<MenuItem, 'rota' | 'sahip'>): MenuTarget | null {
  const route = oge.rota.trim();
  if (!route.startsWith('/') || route.startsWith('//') || route.includes('\\')) return null;
  if (oge.sahip !== OWNER_SPA) return { tur: 'blazor', adres: route };
  const path =
    route === SPA_PREFIX || route.startsWith(`${SPA_PREFIX}/`)
      ? route.slice(SPA_PREFIX.length)
      : route;
  return { tur: 'spa', yol: path || '/' };
}

/** Sunucu yanıtından menü modeli. Süzme YAPMAZ: görünürlük (izin, modül) sunucunun kararıdır. */
export function buildMenuModel(response: MenuResponse): MenuModeli {
  const all: MenuKaydi[] = [];
  response.ogeler.forEach((oge, order) => {
    const target = menuTarget(oge);
    if (!target) return;
    all.push({
      kimlik: `m${order}`,
      etiket: oge.etiket,
      grup: oge.grup,
      sira: Number(oge.sira),
      hizli: oge.hizliBaglanti,
      rozetKodu: oge.rozetKodu,
      hedef: target,
      aramaEtiketi: trSearchKey(oge.etiket),
      aramaGrubu: trSearchKey(oge.grup),
    });
  });
  all.sort((a, b) => a.sira - b.sira);

  const blocks: MenuBlock[] = [];
  const groups = new Map<string, MenuKaydi[]>();
  for (const record of all) {
    if (record.hizli) continue;
    if (record.grup === '') {
      blocks.push({ tur: 'oge', kayit: record });
      continue;
    }
    let group = groups.get(record.grup);
    if (!group) {
      group = [];
      groups.set(record.grup, group);
      blocks.push({ tur: 'grup', ad: record.grup, kayitlar: group });
    }
    group.push(record);
  }

  const badges = new Map<string, number>();
  for (const [code, count] of Object.entries(response.rozetler)) {
    const value = Number(count);
    if (Number.isFinite(value)) badges.set(code, value);
  }
  return { hizli: all.filter((k) => k.hizli), bloklar: blocks, tumu: all, rozetler: badges };
}

/**
 * Geçerli SPA yoluna (sorgu/fragment yok) karşılık gelen menü öğesi: tam eşleşme ya da en uzun
 * `/`-sınırlı önek (`/kiralar/5` → "Kiralar"). Ana sayfa (`/`) yalnız tam eşleşir. Hızlı bağlantı
 * yalnız başka eşleşme yoksa seçilir (aynı rota menüde de varsa işaret menüdekinde).
 */
export function activeEntry(model: MenuModeli, path: string): MenuKaydi | null {
  let enIyi: MenuKaydi | null = null;
  let bestScore = -1;
  for (const record of model.tumu) {
    if (record.hedef.tur !== 'spa') continue;
    const route = record.hedef.yol;
    const matches =
      path === route ||
      (route !== '/' && path.startsWith(route.endsWith('/') ? route : `${route}/`));
    if (!matches) continue;
    const score = route.length * 2 + (record.hizli ? 0 : 1);
    if (score > bestScore) {
      enIyi = record;
      bestScore = score;
    }
  }
  return enIyi;
}

/**
 * Komut paleti araması: Türkçe-gevşek (`İş` = `iş` = `is`), her kelime etikette ya da grupta
 * geçmeli. Sıra: etiket sorguyla başlıyor → etiketteki bir kelime başlıyor → etikette geçiyor →
 * yalnız grupta geçiyor; eşitlikte menü sırası. Aynı rota + etiket (iki grupta aynı ekran) tek sonuç.
 */
export function searchMenu(
  records: readonly MenuKaydi[],
  query: string,
  maximum = 50,
): MenuKaydi[] {
  const key = trSearchKey(query).replace(/\s+/g, ' ');
  const words = key.split(' ').filter(Boolean);
  const seen = new Set<string>();
  const scored: { kayit: MenuKaydi; puan: number }[] = [];
  for (const record of records) {
    const unique = `${targetText(record.hedef)}|${record.aramaEtiketi}`;
    if (seen.has(unique)) continue;
    const text = `${record.aramaEtiketi} ${record.aramaGrubu}`;
    if (!words.every((k) => text.includes(k))) continue;
    seen.add(unique);
    scored.push({ kayit: record, puan: score(record, key) });
  }
  return scored
    .sort((a, b) => a.puan - b.puan || a.kayit.sira - b.kayit.sira)
    .slice(0, maximum)
    .map((p) => p.kayit);
}

function score(record: MenuKaydi, key: string): number {
  if (!key) return 0;
  const label = record.aramaEtiketi;
  if (label.startsWith(key)) return 0;
  if (label.includes(` ${key}`) || label.includes(`-${key}`)) return 1;
  if (label.includes(key)) return 2;
  return 3;
}

function targetText(target: MenuTarget): string {
  return target.tur === 'spa' ? `spa:${target.yol}` : `blazor:${target.adres}`;
}
