import { formatDate } from '@angular/common';

import type { TahsilatIstegi, TahsilatBilgisi } from '@core/api/ui-tipleri';
import { trKucukHarf } from '@core/metin/tr-normalize';
import { ISTANBUL_OFSETI, YEREL } from '@core/yerel/tr-yerel';

/**
 * Panel (F4.5) saf kuralları: sekme seçimi, sayı çevirisi, otomatik tazeleme kararı ve tahsilat gövdesi.
 * Bileşenden ayrı tutulur ki birim testi DOM'suz koşsun.
 */

/** Gün kovaları — Blazor `PanelSekme` sabitleriyle aynı değerler (`?df=`/`?cf=` sorgu sözleşmesi). */
export const PANEL_SEKMELERI = ['gec', 'bugun', 'yarin'] as const;
export type PanelSekme = (typeof PANEL_SEKMELERI)[number];

/** Kullanıcının AÇIKÇA seçtiği sekme (büyük/küçük harf duyarsız); tanınmayan/boş → `null` (seçim yok). */
export function sekmeCoz(ham: unknown): PanelSekme | null {
  if (typeof ham !== 'string') return null;
  const deger = trKucukHarf(ham.trim());
  return (PANEL_SEKMELERI as readonly string[]).includes(deger) ? (deger as PanelSekme) : null;
}

interface Kovalar {
  readonly gecikmis: readonly unknown[];
  readonly varsayilanSekme: string;
}

/**
 * DÖNÜŞLER kartında açık sekme. Seçim yoksa sunucunun `varsayilanSekme`'si (Blazor `PanelSekme.Etkin`:
 * gecikmiş dönüş varsa "gec", yoksa "bugun"); sunucu tanınmayan değer gönderirse aynı kural burada.
 *
 * Seçim ham tutulur (etkin değer yazılmaz): seçimsiz açılan panel her tazelemede kuralı yeniden işler —
 * gecikmişler kapanınca kendiliğinden Bugün'e döner, "Gecikmiş 0 — Kayıt yok."ta takılı kalmaz.
 */
export function donusEtkinSekme(secilen: PanelSekme | null, kovalar: Kovalar): PanelSekme {
  return (
    secilen ?? sekmeCoz(kovalar.varsayilanSekme) ?? (kovalar.gecikmis.length > 0 ? 'gec' : 'bugun')
  );
}

/**
 * ÇIKIŞLAR kartında açık sekme: seçim yoksa DAİMA "bugun" (Blazor `PanelSekme.CikisEtkin`). Gecikmiş çıkış
 * kapanmayan bayat no-show'dur; varsayılan olsaydı günün asıl işi gizlenirdi. Gecikmiş çipi yine "acil" görünür.
 */
export function cikisEtkinSekme(secilen: PanelSekme | null): PanelSekme {
  return secilen ?? 'bugun';
}

/** Kovadaki satırlar (sekmeye göre). */
export function kovaSatirlari<T>(
  kovalar: {
    readonly gecikmis: readonly T[];
    readonly bugun: readonly T[];
    readonly yarin: readonly T[];
  },
  sekme: PanelSekme,
): readonly T[] {
  return sekme === 'gec' ? kovalar.gecikmis : sekme === 'yarin' ? kovalar.yarin : kovalar.bugun;
}

/**
 * OpenAPI tipleri sayıları `number | string` bildirir (sunucu sayı olarak yazar). Gösterim için sayıya çevirir;
 * çevrilemeyen değer `null` (ekranda boş/tire). PARA HESABI YAPILMAZ — yalnız biçimleme ve karşılaştırma.
 */
export function sayi(deger: number | string | null | undefined): number | null {
  if (deger === null || deger === undefined || deger === '') return null;
  const n = typeof deger === 'number' ? deger : Number(deger);
  return Number.isFinite(n) ? n : null;
}

/** Toplamın yüzdesi (tam sayı, Blazor `Yuzde`); toplam 0 → 0. */
export function yuzde(parca: number | string, toplam: number | string): number {
  const p = sayi(parca) ?? 0;
  const t = sayi(toplam) ?? 0;
  return t <= 0 ? 0 : Math.round((100 * p) / t);
}

/** Gelir trendi ay etiketi (`Eyl 26`), İstanbul ayına göre (ayBaşı UTC gece yarısına yakın olabilir). */
export function ayEtiketi(ayBas: string): string {
  try {
    return formatDate(ayBas, 'MMM yy', YEREL, ISTANBUL_OFSETI);
  } catch {
    return '';
  }
}

/**
 * Band alt metnindeki gün başlığı (`Cuma, 25 Eylül 2026`). Girdi sunucunun İstanbul takvim günü
 * (`yyyy-MM-dd`); saat dilimi uygulanmaz (takvim günü yerel güne çevrilip kaydırılmaz).
 */
export function gunBasligi(gun: string): string {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(gun)) return '';
  try {
    return formatDate(gun, 'EEEE, d MMMM y', YEREL);
  } catch {
    return '';
  }
}

// ------------------------------------------------------------------ KPI kartları

export type Ton = 'notr' | 'uyari' | 'hata';

/**
 * Hatırlatma rozet satırı (Blazor `Home.razor` ek kutuları: KM geçen bakım, site talebi, görülmeyen rezervasyon).
 * `rota` yeni arayüz ekranıdır (SPA içinde `routerLink`); `sorgu` rotaya sorgu parametresi olarak eklenir
 * (F11.3: gelen talepler `?durum=Yeni`).
 */
export interface VadeKutusu {
  readonly sayi: number;
  readonly etiket: string;
  readonly rota: string;
  readonly sorgu?: Readonly<Record<string, string>>;
  readonly ton: Ton;
  readonly ipucu?: string;
}

/** Hatırlatma matrisi satırı (Blazor `VadeTier`): 1 hafta / 30 gün kalan ve tarihi geçen sayıları. */
export interface VadeSatiri {
  readonly kod: 'trafik' | 'kasko' | 'muayene';
  readonly etiket: string;
  readonly rota: string;
  readonly yediGun: number;
  readonly otuzGun: number;
  readonly gecmis: number;
}

/** Filo durum kartı (tabela kartı): toplamdan yüzde (tam sayı) ve alt metin. */
export interface KpiKarti {
  readonly kod: 'kirada' | 'musait' | 'serviste' | 'rezervasyon';
  readonly etiket: string;
  readonly sayi: number;
  readonly yuzde: number;
  readonly alt: string;
}

// ------------------------------------------------------------------ otomatik tazeleme

/** Blazor `data-rc-tazele="120"` karşılığı. */
export const TAZELEME_SURESI_MS = 120_000;
/** Koşul denetimi sıklığı: ertelenen tazeleme koşul kalkınca en geç bu kadar sonra yapılır. */
export const TAZELEME_DENETIM_MS = 15_000;

export interface TazelemeDurumu {
  /** Son yüklemeden bu yana geçen süre (ms). */
  readonly gecen: number;
  /** Tarayıcı sekmesi görünür mü (`document.visibilityState`). */
  readonly belgeGorunur: boolean;
  /** Panel, uygulama içi sekmelerden görünür olanı mı (arka plandaki sekme yüklemez). */
  readonly sekmeAktif: boolean;
  /** Kullanıcı bir alana yazıyor ya da tahsilat formu açık. */
  readonly yaziyor: boolean;
  /** Yükleme ya da gönderim sürüyor. */
  readonly mesgul: boolean;
}

/**
 * Otomatik tazeleme ŞİMDİ yapılsın mı? Meta-refresh YOK (enhanced navigation'da kullanıcıyı formun ortasında
 * panoya fırlatıyordu); zamanlayıcı sayfa bileşenine bağlı, sayfa kapanınca ölür. Yazarken ertelenir: açık
 * tahsilat formunun anahtarı ve tutarı tazelemeyle değişmesin.
 */
export function tazelemeZamaniMi(d: TazelemeDurumu): boolean {
  return d.gecen >= TAZELEME_SURESI_MS && d.belgeGorunur && d.sekmeAktif && !d.yaziyor && !d.mesgul;
}

/** Odaktaki eleman yazılabilir bir alan mı (girdi, metin alanı, seçim, contenteditable). */
export function yazilabilirAlanMi(eleman: Element | null): boolean {
  if (eleman === null) return false;
  if (eleman instanceof HTMLTextAreaElement || eleman instanceof HTMLSelectElement) return true;
  if (eleman instanceof HTMLInputElement) {
    return !['button', 'submit', 'reset', 'checkbox', 'radio', 'hidden', 'image'].includes(
      eleman.type,
    );
  }
  return eleman instanceof HTMLElement && eleman.isContentEditable === true;
}

// ------------------------------------------------------------------ tahsilat

export type HesapTuru = 'Kasa' | 'Banka';

/** `POST /api/ui/v1/finans/tahsilat` gövdesi (F4.4a `TahsilatIstegi`): panelin doldurduğu alanlar zorunlu. */
export type PanelTahsilatGovdesi = Required<
  Pick<
    TahsilatIstegi,
    'cariId' | 'kiraId' | 'hesapId' | 'doviz' | 'kanal' | 'aciklama' | 'tahsilatAnahtar'
  >
> & { readonly tutar: string; readonly hesap: HesapTuru };

/**
 * Tahsilat gövdesi. PARA KURALLARI:
 * - `tahsilatAnahtar` sunucunun panel yanıtında verdiği deterministik anahtardır; AYNEN geri gönderilir
 *   (istemci anahtar üretmez, değiştirmez). İki sekme/iki kullanıcı aynı satırı tahsil ederse ikincisi 409.
 * - `cariId`/`kiraId`/`doviz` de aynı yanıttan (satırın müşterisi ve kira dövizi); kur GÖNDERİLMEZ, sunucu çözer.
 * - `tutar` invariant ondalık metin (`rc-para-girdisi` değeri); kayan noktaya girmez.
 * - `kanal` "Masaüstü" (Blazor panosu FAZ-84: tek tık hızlı tahsilat).
 */
export function tahsilatGovdesi(
  bilgi: TahsilatBilgisi,
  secim: { readonly tutar: string; readonly hesap: HesapTuru; readonly hesapId: string | null },
  aciklama: string,
): PanelTahsilatGovdesi {
  return {
    cariId: bilgi.cariId,
    kiraId: bilgi.rentalId,
    tutar: secim.tutar,
    hesap: secim.hesap,
    hesapId: secim.hesapId,
    doviz: bilgi.doviz,
    kanal: 'Masaüstü',
    aciklama,
    tahsilatAnahtar: bilgi.anahtar,
  };
}
