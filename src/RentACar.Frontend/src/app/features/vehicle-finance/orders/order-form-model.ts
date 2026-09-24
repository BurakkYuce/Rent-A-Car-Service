import { invariantOndalik } from '@core/form/ondalik';
import type { GunMetni } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

import { anDegeri, gunDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { toNumber } from '@features/vehicles/vehicle-model';

import type { OrderDetail, OrderRequest } from '../finance-model';

/** Blazor sipariş formunun TÜM alanları (döviz/kur görünür — wire-in bütünlüğü). */
export interface OrderFormValue {
  readonly tedarikci: string | null;
  readonly tedarikciCari: SecimSecenegi | null;
  readonly dosyaNo: string | null;
  readonly siparisTarihi: GunMetni | null;
  readonly imzaTarih: GunMetni | null;
  readonly beklenenTeslim: GunMetni | null;
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

const money = (v: number | string | null) => invariantOndalik(v, { kesir: 2 });

export function orderToForm(d: OrderDetail): OrderFormValue {
  return {
    tedarikci: d.tedarikci,
    tedarikciCari: d.tedarikciCariId
      ? { id: d.tedarikciCariId, etiket: d.tedarikciCariAd ?? d.tedarikciCariId }
      : null,
    dosyaNo: d.dosyaNo,
    siparisTarihi: gunDegeri(d.siparisTarihi),
    imzaTarih: gunDegeri(d.imzaTarih),
    beklenenTeslim: gunDegeri(d.beklenenTeslim),
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
 * Form → `POST` / tam değiştirme `PUT` gövdesi (PUT'ta `surum`). Dokunulmayan tarih sunucunun anıyla gider. Birim
 * fiyat boşsa 0 (Blazor: resmi tutar Adet × Birim; bilgi fiyatları toplama girmez).
 */
export function orderRequest(v: OrderFormValue, base: OrderDetail | null): OrderRequest {
  return {
    tedarikci: metinDegeri(v.tedarikci),
    tedarikciCariId: v.tedarikciCari?.id ?? null,
    siparisTarihi: anDegeri(v.siparisTarihi, base?.siparisTarihi),
    imzaTarih: anDegeri(v.imzaTarih, base?.imzaTarih),
    beklenenTeslim: anDegeri(v.beklenenTeslim, base?.beklenenTeslim),
    dosyaNo: metinDegeri(v.dosyaNo),
    satisTemsilci: metinDegeri(v.satisTemsilci),
    ozelTemsilci: metinDegeri(v.ozelTemsilci),
    marka: metinDegeri(v.marka),
    tip: metinDegeri(v.tip),
    grup: metinDegeri(v.grup),
    versiyon: metinDegeri(v.versiyon),
    opsiyon: metinDegeri(v.opsiyon),
    renk: metinDegeri(v.renk),
    icRenk: metinDegeri(v.icRenk),
    kaynakTip: metinDegeri(v.kaynakTip),
    satisTipi: metinDegeri(v.satisTipi),
    tsbKayitNo: metinDegeri(v.tsbKayitNo),
    krediId: v.krediId,
    adet: v.adet ?? 1,
    birimFiyat: v.birimFiyat ?? '0',
    piyasaFiyat: v.piyasaFiyat,
    opsFiyat: v.opsFiyat,
    filoFiyat: v.filoFiyat,
    doviz: v.doviz,
    kur: v.kur,
    aciklama: metinDegeri(v.aciklama),
    ...(base === null ? {} : { surum: base.surum ?? null }),
  };
}
