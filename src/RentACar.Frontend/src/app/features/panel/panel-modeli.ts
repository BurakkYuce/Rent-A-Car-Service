import { formatDate } from '@angular/common';

import type { CollectionRequest, CollectionInfo } from '@core/api/ui-tipleri';
import { trLowerCase } from '@core/metin/tr-normalize';
import { ISTANBUL_OFFSET, LOCALE } from '@core/yerel/tr-yerel';

/**
 * Panel (F4.5) saf kuralları: sekme seçimi, sayı çevirisi, otomatik tazeleme kararı ve tahsilat gövdesi.
 * Bileşenden ayrı tutulur ki birim testi DOM'suz koşsun.
 */

/** Gün kovaları — Blazor `PanelSekme` sabitleriyle aynı değerler (`?df=`/`?cf=` sorgu sözleşmesi). */
export const PANEL_TABS = ['gec', 'bugun', 'yarin'] as const;
export type PanelTab = (typeof PANEL_TABS)[number];

/** Kullanıcının AÇIKÇA seçtiği sekme (büyük/küçük harf duyarsız); tanınmayan/boş → `null` (seçim yok). */
export function resolveTab(raw: unknown): PanelTab | null {
  if (typeof raw !== 'string') return null;
  const value = trLowerCase(raw.trim());
  return (PANEL_TABS as readonly string[]).includes(value) ? (value as PanelTab) : null;
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
export function returnActiveTab(selected: PanelTab | null, buckets: Kovalar): PanelTab {
  return (
    selected ??
    resolveTab(buckets.varsayilanSekme) ??
    (buckets.gecikmis.length > 0 ? 'gec' : 'bugun')
  );
}

/**
 * ÇIKIŞLAR kartında açık sekme: seçim yoksa DAİMA "bugun" (Blazor `PanelSekme.CikisEtkin`). Gecikmiş çıkış
 * kapanmayan bayat no-show'dur; varsayılan olsaydı günün asıl işi gizlenirdi. Gecikmiş çipi yine "acil" görünür.
 */
export function pickupActiveTab(selected: PanelTab | null): PanelTab {
  return selected ?? 'bugun';
}

/** Kovadaki satırlar (sekmeye göre). */
export function bucketRows<T>(
  buckets: {
    readonly gecikmis: readonly T[];
    readonly bugun: readonly T[];
    readonly yarin: readonly T[];
  },
  tab: PanelTab,
): readonly T[] {
  return tab === 'gec' ? buckets.gecikmis : tab === 'yarin' ? buckets.yarin : buckets.bugun;
}

/**
 * OpenAPI tipleri sayıları `number | string` bildirir (sunucu sayı olarak yazar). Gösterim için sayıya çevirir;
 * çevrilemeyen değer `null` (ekranda boş/tire). PARA HESABI YAPILMAZ — yalnız biçimleme ve karşılaştırma.
 */
export function count(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || value === '') return null;
  const n = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(n) ? n : null;
}

/** Toplamın yüzdesi (tam sayı, Blazor `Yuzde`); toplam 0 → 0. */
export function percent(part: number | string, total: number | string): number {
  const p = count(part) ?? 0;
  const t = count(total) ?? 0;
  return t <= 0 ? 0 : Math.round((100 * p) / t);
}

/** Gelir trendi ay etiketi (`Eyl 26`), İstanbul ayına göre (ayBaşı UTC gece yarısına yakın olabilir). */
export function monthLabel(monthStart: string): string {
  try {
    return formatDate(monthStart, 'MMM yy', LOCALE, ISTANBUL_OFFSET);
  } catch {
    return '';
  }
}

/**
 * Band alt metnindeki gün başlığı (`Cuma, 25 Eylül 2026`). Girdi sunucunun İstanbul takvim günü
 * (`yyyy-MM-dd`); saat dilimi uygulanmaz (takvim günü yerel güne çevrilip kaydırılmaz).
 */
export function dayTitle(day: string): string {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) return '';
  try {
    return formatDate(day, 'EEEE, d MMMM y', LOCALE);
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
export const REFRESH_DURATION_MS = 120_000;
/** Koşul denetimi sıklığı: ertelenen tazeleme koşul kalkınca en geç bu kadar sonra yapılır. */
export const REFRESH_CHECK_MS = 15_000;

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
export function isRefreshDue(d: TazelemeDurumu): boolean {
  return (
    d.gecen >= REFRESH_DURATION_MS && d.belgeGorunur && d.sekmeAktif && !d.yaziyor && !d.mesgul
  );
}

/** Odaktaki eleman yazılabilir bir alan mı (girdi, metin alanı, seçim, contenteditable). */
export function isWritableField(element: Element | null): boolean {
  if (element === null) return false;
  if (element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement) return true;
  if (element instanceof HTMLInputElement) {
    return !['button', 'submit', 'reset', 'checkbox', 'radio', 'hidden', 'image'].includes(
      element.type,
    );
  }
  return element instanceof HTMLElement && element.isContentEditable === true;
}

// ------------------------------------------------------------------ tahsilat

export type AccountType = 'Kasa' | 'Banka';

/** `POST /api/ui/v1/finans/tahsilat` gövdesi (F4.4a `TahsilatIstegi`): panelin doldurduğu alanlar zorunlu. */
export type PanelCollectionBody = Required<
  Pick<
    CollectionRequest,
    'cariId' | 'kiraId' | 'hesapId' | 'doviz' | 'kanal' | 'aciklama' | 'tahsilatAnahtar'
  >
> & { readonly tutar: string; readonly hesap: AccountType };

/**
 * Tahsilat gövdesi. PARA KURALLARI:
 * - `tahsilatAnahtar` sunucunun panel yanıtında verdiği deterministik anahtardır; AYNEN geri gönderilir
 *   (istemci anahtar üretmez, değiştirmez). İki sekme/iki kullanıcı aynı satırı tahsil ederse ikincisi 409.
 * - `cariId`/`kiraId`/`doviz` de aynı yanıttan (satırın müşterisi ve kira dövizi); kur GÖNDERİLMEZ, sunucu çözer.
 * - `tutar` invariant ondalık metin (`rc-para-girdisi` değeri); kayan noktaya girmez.
 * - `kanal` "Masaüstü" (Blazor panosu FAZ-84: tek tık hızlı tahsilat).
 */
export function collectionBody(
  info: CollectionInfo,
  selection: {
    readonly tutar: string;
    readonly hesap: AccountType;
    readonly hesapId: string | null;
  },
  description: string,
): PanelCollectionBody {
  return {
    cariId: info.cariId,
    kiraId: info.rentalId,
    tutar: selection.tutar,
    hesap: selection.hesap,
    hesapId: selection.hesapId,
    doviz: info.doviz,
    kanal: 'Masaüstü',
    aciklama: description,
    tahsilatAnahtar: info.anahtar,
  };
}
