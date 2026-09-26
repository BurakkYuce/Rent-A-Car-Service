import { invariantDecimal } from '@core/form/ondalik';
import type { DayText } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

import { momentValue, dayValue, textValue } from '@features/planlama-ortak/form-yardimcilari';
import { toNumber } from '@features/vehicles/vehicle-model';

import { type OrderDetail, type OrderRequest, rateToSend } from '../finance-model';

/** Blazor sipariş formunun TÜM alanları (döviz/kur görünür — wire-in bütünlüğü). */
export interface OrderFormValue {
  readonly tedarikci: string | null;
  readonly tedarikciCari: SecimSecenegi | null;
  readonly dosyaNo: string | null;
  readonly siparisTarihi: DayText | null;
  readonly imzaTarih: DayText | null;
  readonly beklenenTeslim: DayText | null;
  readonly satisTemsilci: string | null;
  readonly ozelTemsilci: string | null;
  readonly marka: string | null;
  readonly tip: string | null;
  readonly grup: string | null;
  readonly versiyon: string | null;
  readonly renk: string | null;
  readonly icRenk: string | null;
  readonly kaynakTip: string | null;
  readonly satisTipi: string | null;
  readonly opsiyon: string | null;
  readonly tsbKayitNo: string | null;
  readonly krediId: string | null;
  readonly adet: number | null;
  /** Para girdileri: invariant metin (yuvarlanmaz). */
  readonly birimFiyat: string | null;
  readonly piyasaFiyat: string | null;
  readonly opsFiyat: string | null;
  readonly filoFiyat: string | null;
  readonly doviz: string | null;
  /** Boş → sunucu çözer (TRY = 1; dövizde firma kuru → TCMB). */
  readonly kur: number | null;
  readonly aciklama: string | null;
}

export function emptyOrder(): OrderFormValue {
  return {
    tedarikci: null,
    tedarikciCari: null,
    dosyaNo: null,
    siparisTarihi: null,
    imzaTarih: null,
    beklenenTeslim: null,
    satisTemsilci: null,
    ozelTemsilci: null,
    marka: null,
    tip: null,
    grup: null,
    versiyon: null,
    renk: null,
    icRenk: null,
    kaynakTip: null,
    satisTipi: null,
    opsiyon: null,
    tsbKayitNo: null,
    krediId: null,
    adet: 1,
    birimFiyat: null,
    piyasaFiyat: null,
    opsFiyat: null,
    filoFiyat: null,
    doviz: 'TRY',
    kur: null,
    aciklama: null,
  };
}

const money = (v: number | string | null) => invariantDecimal(v, { kesir: 2 });

export function orderToForm(d: OrderDetail): OrderFormValue {
  return {
    tedarikci: d.tedarikci,
    tedarikciCari: d.tedarikciCariId
      ? { id: d.tedarikciCariId, etiket: d.tedarikciCariAd ?? d.tedarikciCariId }
      : null,
    dosyaNo: d.dosyaNo,
    siparisTarihi: dayValue(d.siparisTarihi),
    imzaTarih: dayValue(d.imzaTarih),
    beklenenTeslim: dayValue(d.beklenenTeslim),
    satisTemsilci: d.satisTemsilci,
    ozelTemsilci: d.ozelTemsilci,
    marka: d.marka,
    tip: d.tip,
    grup: d.grup,
    versiyon: d.versiyon,
    renk: d.renk,
    icRenk: d.icRenk,
    kaynakTip: d.kaynakTip,
    satisTipi: d.satisTipi,
    opsiyon: d.opsiyon,
    tsbKayitNo: d.tsbKayitNo,
    krediId: d.krediId,
    adet: toNumber(d.adet),
    birimFiyat: money(d.birimFiyat),
    piyasaFiyat: money(d.piyasaFiyat),
    opsFiyat: money(d.opsFiyat),
    filoFiyat: money(d.filoFiyat),
    doviz: d.doviz,
    kur: toNumber(d.kur),
    aciklama: d.aciklama,
  };
}

/**
 * Form → `POST` / tam değiştirme `PUT` gövdesi (PUT'ta `surum`). Dokunulmayan tarih sunucunun anıyla gider. Resmi
 * tutar Adet × Birim; bilgi fiyatları toplama girmez. Döviz değiştiyse eski kur gönderilmez (`rateToSend`).
 */
export function orderRequest(v: OrderFormValue, base: OrderDetail | null): OrderRequest {
  return {
    tedarikci: textValue(v.tedarikci),
    tedarikciCariId: v.tedarikciCari?.id ?? null,
    siparisTarihi: momentValue(v.siparisTarihi, base?.siparisTarihi),
    imzaTarih: momentValue(v.imzaTarih, base?.imzaTarih),
    beklenenTeslim: momentValue(v.beklenenTeslim, base?.beklenenTeslim),
    dosyaNo: textValue(v.dosyaNo),
    satisTemsilci: textValue(v.satisTemsilci),
    ozelTemsilci: textValue(v.ozelTemsilci),
    marka: textValue(v.marka),
    tip: textValue(v.tip),
    grup: textValue(v.grup),
    versiyon: textValue(v.versiyon),
    opsiyon: textValue(v.opsiyon),
    renk: textValue(v.renk),
    icRenk: textValue(v.icRenk),
    kaynakTip: textValue(v.kaynakTip),
    satisTipi: textValue(v.satisTipi),
    tsbKayitNo: textValue(v.tsbKayitNo),
    krediId: v.krediId,
    adet: v.adet ?? 1,
    // Boş birim fiyat 0'a sessizce düşmez (inceleme L3): form alanı zorunludur; buraya boş gelirse sunucu reddeder.
    birimFiyat: v.birimFiyat ?? '',
    piyasaFiyat: v.piyasaFiyat,
    opsFiyat: v.opsFiyat,
    filoFiyat: v.filoFiyat,
    doviz: v.doviz,
    kur: rateToSend(v.doviz, v.kur, base),
    aciklama: textValue(v.aciklama),
    ...(base === null ? {} : { surum: base.surum ?? null }),
  };
}
