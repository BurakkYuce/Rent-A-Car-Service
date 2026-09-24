import { invariantOndalik } from '@core/form/ondalik';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { anDegeri, gunDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';

import type {
  PaymentMethod,
  ServiceInfo,
  ServiceInfoRequest,
  ServiceLineRequest,
} from '../service-insurance-model';

/**
 * Servis kaydının FAZ-16 bilgi blokları (kaza / fatura / ödeme / yakıt / plan) — `PUT /servisler/{id}/bilgi` tam
 * değiştirmedir: form her alanı taşır, dokunulmayan tarih sunucunun anıyla geri gider (gün yuvarlaması yok), tutarlar
 * invariant metin (`rc-para-girdisi`). BİLGİDİR: deftere yazmaz (gerçek maliyet Giderler ekranından).
 */
export interface ServiceInfoForm {
  readonly atolyeAdi: string | null;
  readonly aciklama: string | null;
  readonly beyanTuru: string | null;
  readonly karsiPlaka: string | null;
  readonly karsiTrafikSigortasi: string | null;
  readonly kazaTarihi: GunMetni | null;
  readonly kazaSorumlusu: string | null;
  readonly hasarDosyaNo: string | null;
  readonly degerKaybi: string | null;
  readonly faturaTarihi: GunMetni | null;
  readonly faturaNo: string | null;
  readonly faturaTutar: string | null;
  readonly faturaKdv: string | null;
  readonly odemeTarihi: GunMetni | null;
  readonly odeme: string | null;
  readonly odemeDoviz: string | null;
  readonly odemeKur: string | null;
  readonly odemeTuru: PaymentMethod | null;
  readonly kasaKodu: string | null;
  readonly hesapNo: string | null;
  readonly cikisYakit: number | null;
  readonly donusYakit: number | null;
  readonly planBasTarihi: GunMetni | null;
  readonly planBitTarihi: GunMetni | null;
}

export const INFO_TEXT_LIMITS: Readonly<Partial<Record<keyof ServiceInfoForm, number>>> = {
  atolyeAdi: 128,
  aciklama: 1024,
  beyanTuru: 64,
  karsiPlaka: 32,
  karsiTrafikSigortasi: 128,
  kazaSorumlusu: 128,
  hasarDosyaNo: 64,
  faturaNo: 64,
  odemeDoviz: 3,
  kasaKodu: 32,
  hesapNo: 64,
};

const money = (v: number | string | null | undefined, kesir = 2): string | null =>
  v === null || v === undefined || v === '' ? null : invariantOndalik(v, { kesir });

const int = (v: number | string | null | undefined): number | null => {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
};

export function emptyInfoForm(): ServiceInfoForm {
  return {
    atolyeAdi: null,
    aciklama: null,
    beyanTuru: null,
    karsiPlaka: null,
    karsiTrafikSigortasi: null,
    kazaTarihi: null,
    kazaSorumlusu: null,
    hasarDosyaNo: null,
    degerKaybi: null,
    faturaTarihi: null,
    faturaNo: null,
    faturaTutar: null,
    faturaKdv: null,
    odemeTarihi: null,
    odeme: null,
    odemeDoviz: null,
    odemeKur: null,
    odemeTuru: null,
    kasaKodu: null,
    hesapNo: null,
    cikisYakit: null,
    donusYakit: null,
    planBasTarihi: null,
    planBitTarihi: null,
  };
}

/** Sunucu bilgisi → form (tarih İstanbul günü, tutar invariant metin, kur 6 hane). */
export function infoToForm(i: ServiceInfo): ServiceInfoForm {
  return {
    atolyeAdi: i.atolyeAdi,
    aciklama: i.aciklama,
    beyanTuru: i.beyanTuru,
    karsiPlaka: i.karsiPlaka,
    karsiTrafikSigortasi: i.karsiTrafikSigortasi,
    kazaTarihi: gunDegeri(i.kazaTarihi),
    kazaSorumlusu: i.kazaSorumlusu,
    hasarDosyaNo: i.hasarDosyaNo,
    degerKaybi: money(i.degerKaybi),
    faturaTarihi: gunDegeri(i.faturaTarihi),
    faturaNo: i.faturaNo,
    faturaTutar: money(i.faturaTutar),
    faturaKdv: money(i.faturaKdv),
    odemeTarihi: gunDegeri(i.odemeTarihi),
    odeme: money(i.odeme),
    odemeDoviz: i.odemeDoviz,
    odemeKur: money(i.odemeKur, 6),
    odemeTuru: (i.odemeTuru as PaymentMethod | null) ?? null,
    kasaKodu: i.kasaKodu,
    hesapNo: i.hesapNo,
    cikisYakit: int(i.cikisYakit),
    donusYakit: int(i.donusYakit),
    planBasTarihi: gunDegeri(i.planBasTarihi),
    planBitTarihi: gunDegeri(i.planBitTarihi),
  };
}

/**
 * Form → `PUT /bilgi` gövdesi. `base` = düzenlenen kaydın sunucu bilgisi (dokunulmayan tarih orijinal anıyla gider);
 * `surum` zorunlu (bayatsa sunucu 409 `cakisma`).
 */
export function infoRequest(
  v: ServiceInfoForm,
  base: ServiceInfo | null,
  surum: string | null,
): ServiceInfoRequest {
  return {
    atolyeAdi: metinDegeri(v.atolyeAdi),
    aciklama: metinDegeri(v.aciklama),
    beyanTuru: metinDegeri(v.beyanTuru),
    karsiPlaka: metinDegeri(v.karsiPlaka),
    karsiTrafikSigortasi: metinDegeri(v.karsiTrafikSigortasi),
    kazaTarihi: anDegeri(v.kazaTarihi, base?.kazaTarihi),
    kazaSorumlusu: metinDegeri(v.kazaSorumlusu),
    hasarDosyaNo: metinDegeri(v.hasarDosyaNo),
    degerKaybi: v.degerKaybi,
    faturaTarihi: anDegeri(v.faturaTarihi, base?.faturaTarihi),
    faturaNo: metinDegeri(v.faturaNo),
    faturaTutar: v.faturaTutar,
    faturaKdv: v.faturaKdv,
    odemeTarihi: anDegeri(v.odemeTarihi, base?.odemeTarihi),
    odeme: v.odeme,
    odemeDoviz: metinDegeri(v.odemeDoviz),
    odemeKur: v.odemeKur,
    odemeTuru: v.odemeTuru,
    kasaKodu: metinDegeri(v.kasaKodu),
    hesapNo: metinDegeri(v.hesapNo),
    cikisYakit: v.cikisYakit,
    donusYakit: v.donusYakit,
    planBasTarihi: anDegeri(v.planBasTarihi, base?.planBasTarihi),
    planBitTarihi: anDegeri(v.planBitTarihi, base?.planBitTarihi),
    surum,
  };
}

/** Kalem formu: tutar boş → sunucu `birim × miktar − indirim`'den hesaplar (UI formül taşımaz). */
export interface ServiceLineForm {
  readonly aciklama: string | null;
  readonly birimFiyat: string | null;
  readonly miktar: number | null;
  readonly indirim: string | null;
  readonly kdvOran: number | null;
  readonly tutar: string | null;
}

export function lineRequest(v: ServiceLineForm): ServiceLineRequest {
  return {
    aciklama: metinDegeri(v.aciklama),
    birimFiyat: v.birimFiyat,
    miktar: v.miktar,
    indirim: v.indirim,
    kdvOran: v.kdvOran,
    tutar: v.tutar,
  };
}
