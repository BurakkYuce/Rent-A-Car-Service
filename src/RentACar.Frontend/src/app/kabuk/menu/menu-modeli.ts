import type { MenuYaniti } from '@core/api/ui-tipleri';
import { trAramaAnahtari } from '@core/metin/tr-normalize';

/** `GET /api/ui/v1/menu` öğesi (sunucu izin ve modüle göre süzmüş olarak gönderir). */
export type MenuOgesi = MenuYaniti['ogeler'][number];

/** SPA sayfası (router) ya da Blazor ekranı (tam sayfa, `/app` dışı). */
export type MenuHedefi =
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
  readonly hedef: MenuHedefi;
  /** `trAramaAnahtari(etiket)`. */
  readonly aramaEtiketi: string;
  readonly aramaGrubu: string;
}

export type MenuBlogu =
  | { readonly tur: 'oge'; readonly kayit: MenuKaydi }
  | { readonly tur: 'grup'; readonly ad: string; readonly kayitlar: readonly MenuKaydi[] };

export interface MenuModeli {
  /** Hızlı bağlantılar (menünün üstünde). */
  readonly hizli: readonly MenuKaydi[];
  /** Gruplar ve grupsuz öğeler, `sira` düzeninde (grup ilk öğesinin sırasıyla yer alır). */
  readonly bloklar: readonly MenuBlogu[];
  /** Tüm öğeler (palet araması), `sira` düzeninde. */
  readonly tumu: readonly MenuKaydi[];
  /** Rozet kodu → sayaç. */
  readonly rozetler: ReadonlyMap<string, number>;
}

/** Menü kaydında `sahip` değerleri. Bilinmeyen sahip Blazor sayılır (tam sayfa — güvenli varsayılan). */
export const SAHIP_SPA = 'spa';

/** SPA'nın kök yolu; `spa` öğesinin rotası bu önekle de gelebilir. */
const SPA_ONEKI = '/app';

/**
 * Öğenin hedefi. Rota yalnız kök-göreli yol olabilir (`/` ile başlar, `//` ya da `\` içermez — açık
 * yönlendirme kapısı kapalı); değilse öğe gösterilmez (`null`). `spa` rotası router yoludur (`/kiralar`,
 * `/app/kiralar` da kabul); Blazor rotası sunucudaki adresidir, olduğu gibi açılır.
 */
export function menuHedefi(oge: Pick<MenuOgesi, 'rota' | 'sahip'>): MenuHedefi | null {
  const rota = oge.rota.trim();
  if (!rota.startsWith('/') || rota.startsWith('//') || rota.includes('\\')) return null;
  if (oge.sahip !== SAHIP_SPA) return { tur: 'blazor', adres: rota };
  const yol =
    rota === SPA_ONEKI || rota.startsWith(`${SPA_ONEKI}/`) ? rota.slice(SPA_ONEKI.length) : rota;
  return { tur: 'spa', yol: yol || '/' };
}

/** Sunucu yanıtından menü modeli. Süzme YAPMAZ: görünürlük (izin, modül) sunucunun kararıdır. */
export function menuModeliKur(yanit: MenuYaniti): MenuModeli {
  const tumu: MenuKaydi[] = [];
  yanit.ogeler.forEach((oge, sira) => {
    const hedef = menuHedefi(oge);
    if (!hedef) return;
    tumu.push({
      kimlik: `m${sira}`,
      etiket: oge.etiket,
      grup: oge.grup,
      sira: Number(oge.sira),
      hizli: oge.hizliBaglanti,
      rozetKodu: oge.rozetKodu,
      hedef,
      aramaEtiketi: trAramaAnahtari(oge.etiket),
      aramaGrubu: trAramaAnahtari(oge.grup),
    });
  });
  tumu.sort((a, b) => a.sira - b.sira);

  const bloklar: MenuBlogu[] = [];
  const gruplar = new Map<string, MenuKaydi[]>();
  for (const kayit of tumu) {
    if (kayit.hizli) continue;
    if (kayit.grup === '') {
      bloklar.push({ tur: 'oge', kayit });
      continue;
    }
    let grup = gruplar.get(kayit.grup);
    if (!grup) {
      grup = [];
      gruplar.set(kayit.grup, grup);
      bloklar.push({ tur: 'grup', ad: kayit.grup, kayitlar: grup });
    }
    grup.push(kayit);
  }

  const rozetler = new Map<string, number>();
  for (const [kod, sayi] of Object.entries(yanit.rozetler)) {
    const deger = Number(sayi);
    if (Number.isFinite(deger)) rozetler.set(kod, deger);
  }
  return { hizli: tumu.filter((k) => k.hizli), bloklar, tumu, rozetler };
}

/**
 * Geçerli SPA yoluna (sorgu/fragment yok) karşılık gelen menü öğesi: tam eşleşme ya da en uzun
 * `/`-sınırlı önek (`/kiralar/5` → "Kiralar"). Ana sayfa (`/`) yalnız tam eşleşir. Hızlı bağlantı
 * yalnız başka eşleşme yoksa seçilir (aynı rota menüde de varsa işaret menüdekinde).
 */
export function etkinKayit(model: MenuModeli, yol: string): MenuKaydi | null {
  let enIyi: MenuKaydi | null = null;
  let enIyiPuan = -1;
  for (const kayit of model.tumu) {
    if (kayit.hedef.tur !== 'spa') continue;
    const rota = kayit.hedef.yol;
    const eslesir =
      yol === rota || (rota !== '/' && yol.startsWith(rota.endsWith('/') ? rota : `${rota}/`));
    if (!eslesir) continue;
    const puan = rota.length * 2 + (kayit.hizli ? 0 : 1);
    if (puan > enIyiPuan) {
      enIyi = kayit;
      enIyiPuan = puan;
    }
  }
  return enIyi;
}

/**
 * Komut paleti araması: Türkçe-gevşek (`İş` = `iş` = `is`), her kelime etikette ya da grupta
 * geçmeli. Sıra: etiket sorguyla başlıyor → etiketteki bir kelime başlıyor → etikette geçiyor →
 * yalnız grupta geçiyor; eşitlikte menü sırası. Aynı rota + etiket (iki grupta aynı ekran) tek sonuç.
 */
export function menuAra(kayitlar: readonly MenuKaydi[], sorgu: string, enFazla = 50): MenuKaydi[] {
  const anahtar = trAramaAnahtari(sorgu).replace(/\s+/g, ' ');
  const kelimeler = anahtar.split(' ').filter(Boolean);
  const gorulen = new Set<string>();
  const puanli: { kayit: MenuKaydi; puan: number }[] = [];
  for (const kayit of kayitlar) {
    const tekil = `${hedefMetni(kayit.hedef)}|${kayit.aramaEtiketi}`;
    if (gorulen.has(tekil)) continue;
    const metin = `${kayit.aramaEtiketi} ${kayit.aramaGrubu}`;
    if (!kelimeler.every((k) => metin.includes(k))) continue;
    gorulen.add(tekil);
    puanli.push({ kayit, puan: puan(kayit, anahtar) });
  }
  return puanli
    .sort((a, b) => a.puan - b.puan || a.kayit.sira - b.kayit.sira)
    .slice(0, enFazla)
    .map((p) => p.kayit);
}

function puan(kayit: MenuKaydi, anahtar: string): number {
  if (!anahtar) return 0;
  const etiket = kayit.aramaEtiketi;
  if (etiket.startsWith(anahtar)) return 0;
  if (etiket.includes(` ${anahtar}`) || etiket.includes(`-${anahtar}`)) return 1;
  if (etiket.includes(anahtar)) return 2;
  return 3;
}

function hedefMetni(hedef: MenuHedefi): string {
  return hedef.tur === 'spa' ? `spa:${hedef.yol}` : `blazor:${hedef.adres}`;
}
