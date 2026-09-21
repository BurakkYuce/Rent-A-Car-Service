import {
  SECIM_SUTUNU,
  SECIM_SUTUNU_GENISLIGI,
  TABLO_SINIRLARI,
  VARSAYILAN_EN_AZ_GENISLIK,
  VARSAYILAN_SUTUN_GENISLIGI,
  type TabloDuzeni,
  type TabloSiralamaDuzeni,
  type TabloSutunDuzeni,
  type TabloSutunu,
} from './tablo-modeli';
import { siralamaAlani } from './tablo-siralama';

/**
 * Sütun düzeni (saf). Kullanıcının kayıtlı düzeni tanımlarla HER ZAMAN uzlaştırılır: sunucudaki
 * düzen eski bir sürümden gelebilir (sütun silinmiş/eklenmiş/sabitlenmiş) — bilinmeyen kod düşer,
 * yeni sütun tanımdaki yerine girer, sabit sütun en solda ve görünür kalır. Böylece bozuk ya da
 * bayat bir kayıt ekranı asla kıramaz.
 */

type Sutunlar = readonly TabloSutunu<never>[];

/** Sabit ve gizlenemez sütunlar kullanıcı tarafından gizlenemez. */
export function gizlenebilirMi(sutun: TabloSutunu<never>): boolean {
  return !sutun.sabit && !sutun.gizlenemez;
}

export function enAzGenislik(sutun: TabloSutunu<never>): number {
  return Math.max(TABLO_SINIRLARI.enAzGenislik, sutun.enAzGenislik ?? VARSAYILAN_EN_AZ_GENISLIK);
}

/** Genişliği sütunun alt sınırı ile sunucu üst sınırı arasına kırpar (tamsayı px). */
export function genislikKirp(sutun: TabloSutunu<never>, px: number): number {
  const enAz = enAzGenislik(sutun);
  if (!Number.isFinite(px)) return Math.max(enAz, sutun.genislik ?? VARSAYILAN_SUTUN_GENISLIGI);
  return Math.min(TABLO_SINIRLARI.enFazlaGenislik, Math.max(enAz, Math.round(px)));
}

/** Tanımdan varsayılan düzen: tanım sırası, `gizli` olanlar kapalı, genişlik tanımdan. */
export function varsayilanDuzen(sutunlar: Sutunlar): TabloDuzeni {
  const sabitler = sutunlar.filter((s) => s.sabit);
  const digerleri = sutunlar.filter((s) => !s.sabit);
  return {
    sutunlar: [...sabitler, ...digerleri].map((s) => ({
      kod: s.kod,
      gorunur: !gizlenebilirMi(s) || !s.gizli,
      genislik: null,
    })),
    siralama: [],
  };
}

/**
 * Kayıtlı düzeni güncel tanımlarla uzlaştırır. `kayitli` null/bozuksa varsayılan döner.
 * Kural: sabitler tanım sırasıyla en solda + görünür; kayıttaki bilinmeyen/yinelenen kod atılır;
 * kayıtta olmayan yeni sütun tanımdaki önceli (kayıtta varsa) hemen ardına, yoksa sabitlerin ardına
 * girer ve tanımdaki `gizli` değerini alır; genişlik kırpılır; sıralama yalnız sıralanabilir sütunda.
 */
export function duzeniBirlestir(sutunlar: Sutunlar, kayitli: TabloDuzeni | null): TabloDuzeni {
  const varsayilan = varsayilanDuzen(sutunlar);
  if (kayitli === null || !Array.isArray(kayitli.sutunlar)) return varsayilan;

  const tanim = new Map(sutunlar.map((s) => [s.kod, s] as const));
  const gorulen = new Set<string>();
  const kayittan: TabloSutunDuzeni[] = [];
  for (const k of kayitli.sutunlar) {
    const s = tanim.get(k?.kod);
    if (s === undefined || s.sabit || gorulen.has(s.kod)) continue;
    gorulen.add(s.kod);
    kayittan.push({
      kod: s.kod,
      gorunur: !gizlenebilirMi(s) || k.gorunur !== false,
      genislik:
        typeof k.genislik === 'number' && Number.isFinite(k.genislik)
          ? genislikKirp(s, k.genislik)
          : null,
    });
  }

  // Kayıtta olmayan (yeni eklenmiş) sabit olmayan sütunlar tanımdaki yerlerine.
  const sabitsiz = sutunlar.filter((s) => !s.sabit);
  sabitsiz.forEach((s, i) => {
    if (gorulen.has(s.kod)) return;
    const onceki = sabitsiz
      .slice(0, i)
      .reverse()
      .find((o) => gorulen.has(o.kod));
    const yer = onceki === undefined ? 0 : kayittan.findIndex((k) => k.kod === onceki.kod) + 1;
    kayittan.splice(yer, 0, {
      kod: s.kod,
      gorunur: !gizlenebilirMi(s) || !s.gizli,
      genislik: null,
    });
    gorulen.add(s.kod);
  });

  const sabitler = varsayilan.sutunlar.filter((d) => tanim.get(d.kod)?.sabit);
  const sabitGenislik = new Map(
    (kayitli.sutunlar ?? [])
      .filter((k) => tanim.get(k?.kod)?.sabit && typeof k.genislik === 'number')
      .map((k) => [k.kod, k.genislik] as const),
  );
  const sabitDuzen = sabitler.map((d) => {
    const px = sabitGenislik.get(d.kod);
    const s = tanim.get(d.kod);
    return {
      ...d,
      genislik: px === undefined || px === null || s === undefined ? null : genislikKirp(s, px),
    };
  });

  const sonuc = [...sabitDuzen, ...kayittan];
  // En az bir görünür sütun (tümü gizlenmiş bir kayıt boş tablo çizmesin).
  if (!sonuc.some((d) => d.gorunur) && sonuc.length > 0) {
    sonuc[0] = { ...sonuc[0], gorunur: true };
  }
  return { sutunlar: sonuc, siralama: siralamaSuz(sutunlar, kayitli.siralama) };
}

function siralamaSuz(sutunlar: Sutunlar, siralama: unknown): TabloSiralamaDuzeni[] {
  if (!Array.isArray(siralama)) return [];
  const sonuc: TabloSiralamaDuzeni[] = [];
  for (const oge of siralama as readonly Partial<TabloSiralamaDuzeni>[]) {
    const s = sutunlar.find((x) => x.kod === oge?.kod);
    if (s === undefined || siralamaAlani(s) === null || sonuc.some((x) => x.kod === s.kod)) {
      continue;
    }
    sonuc.push({ kod: s.kod, azalan: oge.azalan === true });
    if (sonuc.length === TABLO_SINIRLARI.enFazlaSiralama) break;
  }
  return sonuc;
}

/** Eşitlik anahtarı: aynı anahtar = sunucuya yeniden yazmaya gerek yok. */
export function duzenAnahtari(duzen: TabloDuzeni): string {
  return JSON.stringify(duzen);
}

export function gorunurlukAyarla(
  sutunlar: Sutunlar,
  duzen: TabloDuzeni,
  kod: string,
  gorunur: boolean,
): TabloDuzeni {
  const s = sutunlar.find((x) => x.kod === kod);
  if (s === undefined || (!gorunur && !gizlenebilirMi(s))) return duzen;
  const yeni = duzen.sutunlar.map((d) => (d.kod === kod ? { ...d, gorunur } : d));
  if (!yeni.some((d) => d.gorunur)) return duzen; // son görünür sütun gizlenemez
  return { ...duzen, sutunlar: yeni };
}

export function genislikAyarla(
  sutunlar: Sutunlar,
  duzen: TabloDuzeni,
  kod: string,
  px: number,
): TabloDuzeni {
  const s = sutunlar.find((x) => x.kod === kod);
  if (s === undefined) return duzen;
  const genislik = genislikKirp(s, px);
  return {
    ...duzen,
    sutunlar: duzen.sutunlar.map((d) => (d.kod === kod ? { ...d, genislik } : d)),
  };
}

/** Sabit olmayan sütunu bir adım sola/sağa taşır (sabitlerin önüne geçemez). */
export function sutunuKaydir(
  sutunlar: Sutunlar,
  duzen: TabloDuzeni,
  kod: string,
  yon: -1 | 1,
): TabloDuzeni {
  const sabit = new Set(sutunlar.filter((s) => s.sabit).map((s) => s.kod));
  const liste = [...duzen.sutunlar];
  const i = liste.findIndex((d) => d.kod === kod);
  const j = i + yon;
  if (i < 0 || sabit.has(kod) || j < 0 || j >= liste.length || sabit.has(liste[j].kod)) {
    return duzen;
  }
  [liste[i], liste[j]] = [liste[j], liste[i]];
  return { ...duzen, sutunlar: liste };
}

/** Sürükle-bırak: `kod`'u `hedef`'in önüne ya da ardına koyar (ikisi de sabit olmamalı). */
export function sutunuYerlestir(
  sutunlar: Sutunlar,
  duzen: TabloDuzeni,
  kod: string,
  hedef: string,
  konum: 'once' | 'sonra',
): TabloDuzeni {
  const sabit = new Set(sutunlar.filter((s) => s.sabit).map((s) => s.kod));
  if (kod === hedef || sabit.has(kod) || sabit.has(hedef)) return duzen;
  const tasinan = duzen.sutunlar.find((d) => d.kod === kod);
  if (tasinan === undefined) return duzen;
  const liste = duzen.sutunlar.filter((d) => d.kod !== kod);
  const h = liste.findIndex((d) => d.kod === hedef);
  if (h < 0) return duzen;
  liste.splice(konum === 'once' ? h : h + 1, 0, tasinan);
  return { ...duzen, sutunlar: liste };
}

export function siralamaAyarla(
  duzen: TabloDuzeni,
  siralama: TabloSiralamaDuzeni | null,
): TabloDuzeni {
  return { ...duzen, siralama: siralama === null ? [] : [siralama] };
}

/** TanStack durumu (sütun sırası/görünürlük/genişlik/sabitleme). Seçim sütunu en solda sabit. */
export interface TanstackSutunDurumu {
  readonly columnOrder: string[];
  readonly columnVisibility: Record<string, boolean>;
  readonly columnSizing: Record<string, number>;
  readonly columnPinning: { left: string[]; right: string[] };
}

export function tanstackDurumu(
  sutunlar: Sutunlar,
  duzen: TabloDuzeni,
  secilebilir: boolean,
): TanstackSutunDurumu {
  const tanim = new Map(sutunlar.map((s) => [s.kod, s] as const));
  const columnVisibility: Record<string, boolean> = {};
  const columnSizing: Record<string, number> = {};
  for (const d of duzen.sutunlar) {
    const s = tanim.get(d.kod);
    if (s === undefined) continue;
    columnVisibility[d.kod] = d.gorunur;
    columnSizing[d.kod] = d.genislik ?? genislikKirp(s, s.genislik ?? VARSAYILAN_SUTUN_GENISLIGI);
  }
  const secim = secilebilir ? [SECIM_SUTUNU] : [];
  if (secilebilir) columnSizing[SECIM_SUTUNU] = SECIM_SUTUNU_GENISLIGI;
  return {
    columnOrder: [...secim, ...duzen.sutunlar.map((d) => d.kod)],
    columnVisibility,
    columnSizing,
    columnPinning: {
      left: [...secim, ...sutunlar.filter((s) => s.sabit).map((s) => s.kod)],
      right: [],
    },
  };
}
