import { formatMoney } from '@core/bicim/bicim';
import type { DayText } from '@core/form/tarih-girdisi';
import { momentValue, textValue } from '@features/planlama-ortak/form-yardimcilari';
import { toNumber } from '@features/vehicles/vehicle-model';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

import type {
  AccountKind,
  ExpenseCreateRequest,
  ExpensePaymentRequest,
  ExpenseType,
  IncomingInvoiceCreateRequest,
  IncomingInvoiceExpenseRequest,
  IncomingInvoiceLinkRequest,
  IncomingInvoiceRow,
  ManualInvoiceRequest,
  PaymentMethod,
  PenaltyCreateRequest,
  PenaltyPaymentRequest,
  VatRate,
  VehicleSaleRequest,
} from './document-model';

/**
 * Form değeri → istek gövdesi (saf; Vitest'te bağımsız oracle ile). HİÇBİR tutar burada hesaplanmaz: KDV, genel
 * toplam, kalan ve baz tutar SUNUCUDA. Tutarlar `rc-para-girdisi`'nin invariant metniyle (`"1500.50"`) AYNEN gider;
 * boş metin `null` olur (sunucu "boş = otomatik/kalanın tamamı" kuralını uygular). Günler İstanbul gece yarısı anı.
 */

const amount = (v: string | null | undefined): string | null => {
  const t = v?.trim() ?? '';
  return t === '' ? null : t;
};
const day = (g: DayText | null | undefined): string | null => momentValue(g ?? null, null);

export interface ManualInvoiceForm {
  readonly cari: SecimSecenegi | null;
  readonly netTutar: string | null;
  readonly kdvOrani: VatRate | null;
  readonly tarih: DayText | null;
  readonly vadeTarihi: DayText | null;
  readonly aciklama: string | null;
  readonly islemSube: string | null;
  readonly evrakNo: string | null;
  readonly faturaOzelKod: string | null;
  readonly odemeTuru: string | null;
  readonly gonderimSekli: string | null;
  readonly kdvSifirSebep: string | null;
  readonly otv: string | null;
  readonly tevkifatOran: number | null;
  readonly tevkifatTutar: string | null;
  readonly damgaVergisi: string | null;
}

export function manualInvoiceRequest(v: ManualInvoiceForm): ManualInvoiceRequest {
  return {
    cariId: v.cari?.id ?? '',
    netTutar: amount(v.netTutar) ?? '',
    kdvOrani: v.kdvOrani,
    aciklama: textValue(v.aciklama),
    tarih: day(v.tarih),
    vadeTarihi: day(v.vadeTarihi),
    islemSube: textValue(v.islemSube),
    evrakNo: textValue(v.evrakNo),
    faturaOzelKod: textValue(v.faturaOzelKod),
    odemeTuru: textValue(v.odemeTuru),
    gonderimSekli: textValue(v.gonderimSekli),
    kdvSifirSebep: textValue(v.kdvSifirSebep),
    otv: amount(v.otv),
    tevkifatOran: v.tevkifatOran,
    tevkifatTutar: amount(v.tevkifatTutar),
    damgaVergisi: amount(v.damgaVergisi),
  };
}

export interface PenaltyForm {
  readonly cezaTuru: string | null;
  readonly tebligTarihi: DayText | null;
  readonly saat: string | null;
  readonly vadeGun: number | null;
  readonly yer: string | null;
  readonly cepTel: string | null;
  readonly makbuzNo: string | null;
  readonly islemSube: string | null;
  readonly arac: SecimSecenegi | null;
  readonly cari: SecimSecenegi | null;
  readonly sebep: string | null;
  readonly kalemler: readonly { readonly tutar: string | null; readonly sebep: string | null }[];
}

/** Boş tutarlı kalem atlanır (Blazor: "Boş kalem atlanır"); sıra korunur. */
export function penaltyRequest(v: PenaltyForm): PenaltyCreateRequest {
  return {
    cezaTuru: textValue(v.cezaTuru),
    kalemler: v.kalemler
      .filter((k) => amount(k.tutar) !== null)
      .map((k) => ({ tutar: amount(k.tutar) ?? '', sebep: textValue(k.sebep) })),
    tebligTarihi: day(v.tebligTarihi),
    vadeGun: v.vadeGun,
    aracId: v.arac?.id ?? null,
    cariId: v.cari?.id ?? null,
    kiraId: null,
    sebep: textValue(v.sebep),
    saat: textValue(v.saat),
    yer: textValue(v.yer),
    cepTel: textValue(v.cepTel),
    makbuzNo: textValue(v.makbuzNo),
    islemSube: textValue(v.islemSube),
  };
}

export interface PenaltyPaymentForm {
  readonly satirId: string | null;
  readonly tutar: string | null;
  readonly hesap: AccountKind | null;
  readonly tarih: DayText | null;
  readonly makbuzNo: string | null;
  readonly islemYapan: string | null;
  readonly aciklama: string | null;
}

/** Tutar boş → kalemin kalanının TAMAMI (sunucu kuralı; istemci kalanı kopyalamaz). */
export function penaltyPaymentRequest(v: PenaltyPaymentForm): PenaltyPaymentRequest {
  return {
    satirId: v.satirId ?? '',
    hesap: v.hesap,
    tutar: amount(v.tutar),
    tarih: day(v.tarih),
    makbuzNo: textValue(v.makbuzNo),
    islemYapan: textValue(v.islemYapan),
    aciklama: textValue(v.aciklama),
  };
}

export interface ExpenseForm {
  readonly tip: ExpenseType | null;
  readonly arac: SecimSecenegi | null;
  readonly cari: SecimSecenegi | null;
  /** Bağlı kira sözleşmesi (isteğe bağlı; sunucu varlık + şube kapsamını denetler). */
  readonly kira: SecimSecenegi | null;
  readonly netTutar: string | null;
  readonly kdvOrani: VatRate | null;
  readonly odemeYontemi: PaymentMethod | null;
  readonly doviz: string | null;
  /** `rc-para-girdisi` (4 hane) invariant metni; boş → sunucu çözer (TRY = 1; dövizde firma kuru → TCMB). */
  readonly kur: string | null;
  readonly hesapId: string | null;
  readonly tarih: DayText | null;
  readonly odemeTarihi: DayText | null;
  readonly vade: DayText | null;
  readonly hazirAciklama: string | null;
  readonly sube: string | null;
  readonly evrakNo: string | null;
  readonly aciklama: string | null;
}

export function expenseRequest(v: ExpenseForm): ExpenseCreateRequest {
  return {
    tip: v.tip,
    netTutar: amount(v.netTutar) ?? '',
    kdvOrani: v.kdvOrani ?? '',
    odemeYontemi: v.odemeYontemi,
    tarih: day(v.tarih),
    aracId: v.arac?.id ?? null,
    cariId: v.cari?.id ?? null,
    sube: textValue(v.sube),
    evrakNo: textValue(v.evrakNo),
    doviz: textValue(v.doviz),
    kur: amount(v.kur),
    aciklama: textValue(v.aciklama),
    hesapId: v.hesapId,
    odemeTarihi: day(v.odemeTarihi),
    hazirAciklama: textValue(v.hazirAciklama),
    kiraId: v.kira?.id ?? null,
    vade: day(v.vade),
  };
}

export interface ExpensePaymentForm {
  readonly tutar: string | null;
  readonly tarih: DayText | null;
  readonly makbuzNo: string | null;
  readonly aciklama: string | null;
}

/** Tutar boş → kalanın tamamı (sunucu). */
export function expensePaymentRequest(v: ExpensePaymentForm): ExpensePaymentRequest {
  return {
    tutar: amount(v.tutar),
    tarih: day(v.tarih),
    makbuzNo: textValue(v.makbuzNo),
    aciklama: textValue(v.aciklama),
  };
}

export interface SaleForm {
  readonly arac: SecimSecenegi | null;
  readonly alici: SecimSecenegi | null;
  readonly satisNet: string | null;
  readonly kdvOrani: VatRate | null;
  readonly tarih: DayText | null;
  readonly doviz: string | null;
  readonly kur: string | null;
  readonly noterNo: string | null;
  readonly hedefFiyat: string | null;
  readonly satisKm: number | null;
  readonly satisKanali: string | null;
  readonly devir: string | null;
  readonly aciklama: string | null;
  readonly ilanKm: number | null;
  readonly listeDoviz: string | null;
  readonly satisNoktasi: string | null;
  readonly uygulananKampanya: string | null;
  readonly ihaleFirmasi: string | null;
  readonly ihaleTarihi: DayText | null;
  readonly ihaleSayisi: string | null;
  readonly noterSatisTarihi: DayText | null;
  readonly yevmiyeNumarasi: string | null;
  readonly aciklama2: string | null;
  readonly kirayaVerme: boolean | null;
  readonly satisiVerildi: boolean | null;
}

export function saleRequest(v: SaleForm): VehicleSaleRequest {
  return {
    aracId: v.arac?.id ?? '',
    aliciCariId: v.alici?.id ?? '',
    satisNet: amount(v.satisNet) ?? '',
    kdvOrani: v.kdvOrani ?? '',
    tarih: day(v.tarih),
    doviz: textValue(v.doviz),
    kur: amount(v.kur),
    noterNo: textValue(v.noterNo),
    aciklama: textValue(v.aciklama),
    hedefFiyat: amount(v.hedefFiyat),
    satisKm: v.satisKm,
    satisKanali: textValue(v.satisKanali),
    devir: textValue(v.devir),
    ihaleTarihi: day(v.ihaleTarihi),
    ihaleFirmasi: textValue(v.ihaleFirmasi),
    noterSatisTarihi: day(v.noterSatisTarihi),
    kirayaVerme: v.kirayaVerme === true,
    ilanKm: v.ilanKm,
    listeDoviz: textValue(v.listeDoviz),
    satisNoktasi: textValue(v.satisNoktasi),
    uygulananKampanya: textValue(v.uygulananKampanya),
    ihaleSayisi: textValue(v.ihaleSayisi),
    satisiVerildi: v.satisiVerildi === true,
    yevmiyeNumarasi: textValue(v.yevmiyeNumarasi),
    aciklama2: textValue(v.aciklama2),
  };
}

export interface IncomingCreateForm {
  readonly ettn: string | null;
  readonly gonderenVkn: string | null;
  readonly gonderenUnvan: string | null;
  readonly tarih: DayText | null;
  readonly netTutar: string | null;
  readonly kdvTutar: string | null;
  readonly genelToplam: string | null;
  readonly doviz: string | null;
  readonly aciklama: string | null;
}

/** Elle giriş: belge tutarları kullanıcının belgeden OKUDUĞU değerlerdir; tutarlılığı (net + KDV = genel) sunucu denetler. */
export function incomingCreateRequest(v: IncomingCreateForm): IncomingInvoiceCreateRequest {
  return {
    ettn: textValue(v.ettn),
    gonderenVkn: textValue(v.gonderenVkn),
    gonderenUnvan: textValue(v.gonderenUnvan),
    netTutar: amount(v.netTutar) ?? '0',
    kdvTutar: amount(v.kdvTutar) ?? '0',
    genelToplam: amount(v.genelToplam) ?? '',
    tarih: day(v.tarih),
    doviz: textValue(v.doviz),
    aciklama: textValue(v.aciklama),
  };
}

export interface IncomingLinkForm {
  readonly kdv20Matrah: string | null;
  readonly kdv20: string | null;
  readonly kdv10Matrah: string | null;
  readonly kdv10: string | null;
  readonly kdv1Matrah: string | null;
  readonly kdv1: string | null;
  readonly kdv0Matrah: string | null;
  readonly arac: SecimSecenegi | null;
  /** Gider kategorisi (`/secim/gider-kategorisi` aranabilir seçimi; etiket kaydın `giderKategoriAd`'ından). */
  readonly kategori: SecimSecenegi | null;
  readonly cari: SecimSecenegi | null;
  readonly giderTipi: ExpenseType | null;
}

const kept = (n: number | string | null | undefined): string | null => {
  const v = toNumber(n);
  return v === null ? null : String(n);
};

/** Kayıt → bağlama formu (tutarlar sunucunun değeri; `rc-para-girdisi` programatik değeri yuvarlar). */
export function incomingToLinkForm(r: IncomingInvoiceRow): IncomingLinkForm {
  const tip = r.giderTipi as ExpenseType | null;
  return {
    kdv20Matrah: kept(r.kdv20Matrah),
    kdv20: kept(r.kdv20),
    kdv10Matrah: kept(r.kdv10Matrah),
    kdv10: kept(r.kdv10),
    kdv1Matrah: kept(r.kdv1Matrah),
    kdv1: kept(r.kdv1),
    kdv0Matrah: kept(r.kdv0Matrah),
    arac: r.aracId ? { id: r.aracId, etiket: r.plaka ?? r.aracId } : null,
    kategori: r.giderKategoriId
      ? { id: r.giderKategoriId, etiket: r.giderKategoriAd ?? r.giderKategoriId }
      : null,
    cari: r.cariId ? { id: r.cariId, etiket: r.cariAd ?? r.cariId } : null,
    giderTipi: tip,
  };
}

/** TAM DEĞİŞTİRME (boş alan temizler); `surum` ZORUNLU — detaydan, bayatsa sunucu 409 `cakisma`. */
export function incomingLinkRequest(
  v: IncomingLinkForm,
  version: string,
): IncomingInvoiceLinkRequest {
  return {
    surum: version,
    kdv20Matrah: amount(v.kdv20Matrah),
    kdv20: amount(v.kdv20),
    kdv10Matrah: amount(v.kdv10Matrah),
    kdv10: amount(v.kdv10),
    kdv1Matrah: amount(v.kdv1Matrah),
    kdv1: amount(v.kdv1),
    kdv0Matrah: amount(v.kdv0Matrah),
    aracId: v.arac?.id ?? null,
    giderKategoriId: v.kategori?.id ?? null,
    cariId: v.cari?.id ?? null,
    giderTipi: v.giderTipi,
  };
}

export interface IncomingExpenseForm {
  readonly odemeYontemi: PaymentMethod | null;
  readonly cari: SecimSecenegi | null;
  readonly sube: string | null;
}

export function incomingExpenseRequest(v: IncomingExpenseForm): IncomingInvoiceExpenseRequest {
  return {
    odemeYontemi: v.odemeYontemi ?? 'AcikHesap',
    cariId: v.cari?.id ?? null,
    sube: textValue(v.sube),
  };
}

/**
 * Kayıtlı KDV kırılımının okunur özeti (giderleştirme onayı; r300 M4): yalnız dolu kademeler, tutarlar sunucunun
 * değeri (istemci toplamaz). Hiç kademe yoksa `empty`.
 */
export function vatBreakdownText(r: IncomingInvoiceRow, empty: string): string {
  const c = r.doviz || 'TRY';
  const m = (v: number | string | null | undefined) => formatMoney(toNumber(v), c);
  const parts: string[] = [];
  const tiers: readonly [string, number | string | null, number | string | null][] = [
    ['%20', r.kdv20Matrah, r.kdv20],
    ['%10', r.kdv10Matrah, r.kdv10],
    ['%1', r.kdv1Matrah, r.kdv1],
    ['%0', r.kdv0Matrah, null],
  ];
  for (const [label, base, vat] of tiers) {
    if (toNumber(base) === null && toNumber(vat) === null) continue;
    parts.push(vat === null ? `${label}: ${m(base)}` : `${label}: ${m(base)} + KDV ${m(vat)}`);
  }
  return parts.length === 0 ? empty : parts.join('; ');
}
