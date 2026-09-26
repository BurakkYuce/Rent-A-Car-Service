import type { Schema } from '@core/api/ui-tipleri';
import type { DayText } from '@core/form/tarih-girdisi';
import { listDefinition } from '@core/veri/liste-sorgusu';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

import { momentValue, dayValue, textValue } from '@features/planlama-ortak/form-yardimcilari';

export type FleetListRow = Schema<'FiloListeSatiri'>;
export type FleetRental = Schema<'FiloKiralamaDto'>;
export type FleetRentalRequest = Schema<'FiloKiralamaIstegi'>;
export type FleetIdentityRequest = Schema<'FiloKunyeIstegi'>;
export type CreateFleetResponse = Schema<'FiloOlusturYaniti'>;
export type FleetInstallment = Schema<'FiloTaksitDto'>;

/** Sunucu enum ADLARI (`FiloKiraDurum`); tanımsız ad 400. */
export const FLEET_STATUSES = ['Aktif', 'Tamamlandi', 'Iptal'] as const;
export type FleetStatus = (typeof FLEET_STATUSES)[number];

/** Blazor datalist önerileri (serbest metin de kabul). */
export const INVOICE_TYPE_SUGGESTIONS = ['Dönem', 'Kırık'] as const;
export const PRICE_TYPE_SUGGESTIONS = [
  'Aylık',
  '30 Gün Aylık',
  'KDV Dahil',
  '30 Gün Dahil',
] as const;

/**
 * Filo kiralama listesi URL ↔ API sözleşmesi (`GET /api/ui/v1/filo-kiralama`): Blazor FAZ-21 arama
 * paneli (müşteri, plaka, serbest arama, durum, başlangıç günü aralığı) + sunucu sayfalama/sıralama.
 */
export const FLEET_LIST = listDefinition({
  filtreler: {
    musteriId: { tur: 'kimlik' },
    plaka: { tur: 'metin', enFazla: 32 },
    ara: { tur: 'metin', enFazla: 100 },
    durum: { tur: 'secim', degerler: FLEET_STATUSES },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: [
    'no',
    'musteri',
    'plaka',
    'basTar',
    'sureAy',
    'aylikUcret',
    'genelToplam',
    'durum',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

export function fleetStatus(value: string): FleetStatus | null {
  return (FLEET_STATUSES as readonly string[]).includes(value) ? (value as FleetStatus) : null;
}

/** KÜNYE alanları (para/süre YOK — taksit planı bu yoldan değişemez). */
export interface FiloKunyeDegeri {
  readonly sozlesmeNo: string | null;
  readonly makbuzNo: string | null;
  readonly dosyaNo: string | null;
  readonly sozlesmeTarihi: DayText | null;
  readonly imzaTarih: DayText | null;
  readonly satisTemsilcisi: string | null;
  readonly faturaTuru: string | null;
  readonly fiyatTuru: string | null;
  readonly kaynak: string | null;
  readonly vadeGun: number | null;
  readonly toplamKmLimiti: number | null;
  readonly cikisKm: number | null;
  readonly toplamKm: number | null;
  readonly aciklama: string | null;
}

/** Yeni sözleşme formu: künye + müşteri/araç/başlangıç/süre/ücret/KDV/damga. */
export interface FiloYeniDegeri extends FiloKunyeDegeri {
  readonly musteri: SecimSecenegi | null;
  readonly arac: SecimSecenegi | null;
  readonly basTar: DayText | null;
  readonly sureAy: number | null;
  readonly aylikUcret: string | null;
  /** Kesir (0,20 = %20) — Blazor formuyla aynı. */
  readonly kdvOrani: number | null;
  readonly damgaVergisi: string | null;
}

const numberValue = (d: number | string | null | undefined): number | null =>
  d === null || d === undefined || d === '' ? null : Number(d);

/** Kayıt → künye form değeri. */
export function profileValues(k: FleetRental): FiloKunyeDegeri {
  return {
    sozlesmeNo: k.sozlesmeNo,
    makbuzNo: k.makbuzNo,
    dosyaNo: k.dosyaNo,
    sozlesmeTarihi: dayValue(k.sozlesmeTarihi),
    imzaTarih: dayValue(k.imzaTarih),
    satisTemsilcisi: k.satisTemsilcisi,
    faturaTuru: k.faturaTuru,
    fiyatTuru: k.fiyatTuru,
    kaynak: k.kaynak,
    vadeGun: numberValue(k.vadeGun),
    toplamKmLimiti: numberValue(k.toplamKmLimiti),
    cikisKm: numberValue(k.cikisKm),
    toplamKm: numberValue(k.toplamKm),
    aciklama: k.aciklama,
  };
}

export const EMPTY_IDENTITY: FiloKunyeDegeri = {
  sozlesmeNo: null,
  makbuzNo: null,
  dosyaNo: null,
  sozlesmeTarihi: null,
  imzaTarih: null,
  satisTemsilcisi: null,
  faturaTuru: null,
  fiyatTuru: null,
  kaynak: null,
  vadeGun: null,
  toplamKmLimiti: null,
  cikisKm: null,
  toplamKm: null,
  aciklama: null,
};

export function newValues(): FiloYeniDegeri {
  return {
    ...EMPTY_IDENTITY,
    musteri: null,
    arac: null,
    basTar: null,
    sureAy: null,
    aylikUcret: null,
    kdvOrani: 0.2,
    damgaVergisi: null,
  };
}

/**
 * Künye → `PUT /filo-kiralama/{id}/kunye` tam değiştirme gövdesi (`surum` zorunlu). Dokunulmayan tarih
 * sunucunun anıyla AYNEN gider: belge tarihi sınırı yalnız DEĞİŞEN tarihe uygulanır (#271 Low-1), gün
 * yuvarlaması eski sözleşmenin tarihini kaydırıp sınıra takmaz.
 */
export function profileBody(v: FiloKunyeDegeri, floor: FleetRental): FleetIdentityRequest {
  return {
    surum: floor.surum ?? null,
    sozlesmeNo: textValue(v.sozlesmeNo),
    makbuzNo: textValue(v.makbuzNo),
    dosyaNo: textValue(v.dosyaNo),
    sozlesmeTarihi: momentValue(v.sozlesmeTarihi, floor.sozlesmeTarihi),
    imzaTarih: momentValue(v.imzaTarih, floor.imzaTarih),
    satisTemsilcisi: textValue(v.satisTemsilcisi),
    faturaTuru: textValue(v.faturaTuru),
    fiyatTuru: textValue(v.fiyatTuru),
    kaynak: textValue(v.kaynak),
    vadeGun: v.vadeGun,
    toplamKmLimiti: v.toplamKmLimiti,
    cikisKm: v.cikisKm,
    toplamKm: v.toplamKm,
    aciklama: textValue(v.aciklama),
  };
}

/** Yeni sözleşme → `POST /filo-kiralama` gövdesi. Döviz/kur formda yok (Blazor gibi TRY, kur 1). */
export function createBody(v: FiloYeniDegeri): FleetRentalRequest {
  return {
    musteriId: v.musteri?.id ?? '',
    vehicleId: v.arac?.id ?? '',
    basTar: momentValue(v.basTar, null),
    sureAy: v.sureAy,
    aylikUcret: v.aylikUcret,
    kdvOrani: v.kdvOrani,
    damgaVergisi: v.damgaVergisi,
    toplamKmLimiti: v.toplamKmLimiti,
    aciklama: textValue(v.aciklama),
    sozlesmeNo: textValue(v.sozlesmeNo),
    makbuzNo: textValue(v.makbuzNo),
    dosyaNo: textValue(v.dosyaNo),
    sozlesmeTarihi: momentValue(v.sozlesmeTarihi, null),
    imzaTarih: momentValue(v.imzaTarih, null),
    satisTemsilcisi: textValue(v.satisTemsilcisi),
    faturaTuru: textValue(v.faturaTuru),
    fiyatTuru: textValue(v.fiyatTuru),
    kaynak: textValue(v.kaynak),
    vadeGun: v.vadeGun,
    cikisKm: v.cikisKm,
    toplamKm: v.toplamKm,
  };
}

/** JSON sayısı (`number | string`) → gösterim sayısı. YALNIZ gösterim. */
export function count(value: number | string | null | undefined): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'string' && value.trim() !== '') {
    const n = Number(value);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}
