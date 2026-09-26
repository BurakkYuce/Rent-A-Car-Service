import type { Sayfa } from '@core/api/sayfa';
import type { Schema } from '@core/api/ui-tipleri';
import { invariantDecimal } from '@core/form/ondalik';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { listDefinition } from '@core/veri/liste-sorgusu';

export type CostInput = Schema<'CostInputDto'>;
export type CostResult = Schema<'CostResultDto'>;
export type CostOfferRow = Schema<'CostOfferRow'>;
/** Liste yanıtı: sayfa çekirdek `Sayfa<T>` biçiminde okunur (tablo motoru sözleşmesi). */
export interface CostOfferList {
  readonly kayitlar: Sayfa<CostOfferRow>;
  readonly ozet: Schema<'CostOfferList'>['ozet'];
}
export type CostOfferDetail = Schema<'CostOfferDetail'>;
export type CostOfferRequest = Schema<'CostOfferRequest'>;

export const COST_OFFERS = '/api/ui/v1/maliyet-teklifleri';

/** Kredi hesap şekli (sunucu enum adları). */
export const CREDIT_METHODS = ['EsitTaksitli', 'Rotatif'] as const;

type CostKey = Exclude<keyof CostInput, 'krediHesaplamaSekli'>;

/**
 * Filo maliyet girdisi alanları — Blazor `MaliyetHesaplama.razor` sırası. `money` = tutar (`rc-para-girdisi`,
 * 2 hane, sunucu `RecordAmount`); `ratio` = KESİR (0,30 = %30; 4 hane); `int` = ay / adet. Boş alan = sunucu
 * varsayılanı (servis doldurur, UI varsayılan UYDURMAZ — yalnız Blazor'un ilk açılış değerleri form başlangıcıdır).
 */
export interface CostField {
  readonly name: CostKey;
  readonly label: CeviriAnahtari;
  readonly kind: 'money' | 'ratio' | 'int';
  readonly initial: string | number | null;
  readonly group: 'finans' | 'gider';
}

const F = (
  name: CostKey,
  kind: CostField['kind'],
  initial: string | number | null,
  group: CostField['group'] = 'finans',
): CostField => ({
  name,
  label: `fiyatTarife.maliyet.alan.${name}` as CeviriAnahtari,
  kind,
  initial,
  group,
});

export const COST_FIELDS: readonly CostField[] = [
  F('alisBedeli', 'money', null),
  F('residualYuzde', 'ratio', 0.3),
  F('sureAy', 'int', 36),
  F('aracSayisi', 'int', 1),
  F('faizOran', 'ratio', 0),
  F('kkdfOran', 'ratio', 0.15),
  F('bsmvOran', 'ratio', 0.15),
  F('damgaOran', 'ratio', 0),
  F('karMarji', 'ratio', 0.2),
  F('kdvOran', 'ratio', 0.2),
  F('enflasyonOran', 'ratio', 0),
  F('kaskoYillik', 'money', null, 'gider'),
  F('trafikSigortasiYillik', 'money', null, 'gider'),
  F('mtvYillik', 'money', null, 'gider'),
  F('bakimYillik', 'money', null, 'gider'),
  F('lastikYillik', 'money', null, 'gider'),
  F('lastikKisYillik', 'money', null, 'gider'),
  F('aracTakipYillik', 'money', null, 'gider'),
  F('tescilPlakaYillik', 'money', null, 'gider'),
  F('muayeneEmisyonYillik', 'money', null, 'gider'),
  F('yedekAracYillik', 'money', null, 'gider'),
  F('yonetimGideriAylik', 'money', null, 'gider'),
  F('aylikGider', 'money', null, 'gider'),
  F('bankaDosyaDigerMasraf', 'money', null, 'gider'),
];

export type CostFormValue = Record<CostKey, string | number | null> & {
  readonly krediHesaplamaSekli: string | null;
};

const toNum = (v: unknown): number | null => {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
};

export function initialCostForm(): CostFormValue {
  const v: Record<string, string | number | null> = { krediHesaplamaSekli: 'EsitTaksitli' };
  for (const f of COST_FIELDS) v[f.name] = f.initial;
  return v as CostFormValue;
}

/** Kayıtlı teklifin girdisi → form (tutar invariant metin, oran/adet sayı). */
export function costInputToForm(i: CostInput): CostFormValue {
  const v: Record<string, string | number | null> = {
    krediHesaplamaSekli: i.krediHesaplamaSekli ?? 'EsitTaksitli',
  };
  for (const f of COST_FIELDS) {
    const raw = i[f.name] as number | string | null;
    v[f.name] =
      f.kind === 'money'
        ? raw === null || raw === ''
          ? null
          : invariantDecimal(raw, { kesir: 2 })
        : toNum(raw);
  }
  return v as CostFormValue;
}

/** Form → `CostInputDto` (boş alan `null` → sunucu varsayılanı). */
export function costFormToInput(v: CostFormValue): CostInput {
  const out: Record<string, string | number | null> = {
    krediHesaplamaSekli: v.krediHesaplamaSekli,
  };
  for (const f of COST_FIELDS) {
    const raw = v[f.name];
    out[f.name] = f.kind === 'money' ? (raw === '' ? null : raw) : toNum(raw);
  }
  return out as unknown as CostInput;
}

export const OFFER_LIST = listDefinition({
  filtreler: {
    metin: { tur: 'metin', enFazla: 100 },
    plaka: { tur: 'metin', enFazla: 32 },
    cariId: { tur: 'kimlik' },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
    fiyatMin: { tur: 'ondalik' },
    fiyatMax: { tur: 'ondalik' },
  },
  siralanabilir: ['kayitNo', 'baslik', 'tarih', 'plaka', 'teklifAylikNet', 'filoTeklifKdvli'],
  varsayilanSirala: '-tarih',
});
