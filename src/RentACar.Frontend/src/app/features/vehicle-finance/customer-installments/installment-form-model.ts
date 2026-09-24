import type { GunMetni } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

import { anDegeri, gunDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { invariantOndalik } from '@core/form/ondalik';
import { toNumber } from '@features/vehicles/vehicle-model';

import {
  type CustomerInstallment,
  type CustomerInstallmentRequest,
  type CustomerInstallmentStatus,
  type InstallmentPlanRequest,
  rateToSend,
} from '../finance-model';

/** Blazor "Tek Taksit Ekle / Taksiti Düzenle" formu. */
export interface InstallmentFormValue {
  readonly cari: SecimSecenegi | null;
  readonly arac: SecimSecenegi | null;
  readonly vade: GunMetni | null;
  /** `rc-para-girdisi` invariant metni. */
  readonly taksitTutari: string | null;
  readonly doviz: string | null;
  /** Boş → sunucu çözer (TRY = 1; dövizde firma kuru → TCMB). */
  readonly kur: number | null;
  readonly durum: CustomerInstallmentStatus | null;
  readonly odemeTarihi: GunMetni | null;
  readonly aciklama: string | null;
}

/** Blazor "Taksit Planı Üret" formu (taksit sayısı varsayılanı 12). */
export interface PlanFormValue {
  readonly cari: SecimSecenegi | null;
  readonly arac: SecimSecenegi | null;
  readonly toplamTutar: string | null;
  readonly taksitSayisi: number | null;
  readonly ilkVade: GunMetni | null;
  readonly doviz: string | null;
  readonly kur: number | null;
  readonly aciklama: string | null;
}

export function emptyInstallment(): InstallmentFormValue {
  return {
    cari: null,
    arac: null,
    vade: null,
    taksitTutari: null,
    doviz: 'TRY',
    kur: null,
    durum: 'Bekliyor',
    odemeTarihi: null,
    aciklama: null,
  };
}

export function emptyPlan(): PlanFormValue {
  return {
    cari: null,
    arac: null,
    toplamTutar: null,
    taksitSayisi: 12,
    ilkVade: null,
    doviz: 'TRY',
    kur: null,
    aciklama: null,
  };
}

/** Kayıt → düzenleme formu (tutar invariant metin; kur kaydın kuru). */
export function installmentToForm(r: CustomerInstallment): InstallmentFormValue {
  return {
    cari: { id: r.cariId, etiket: r.cariAd },
    arac: r.vehicleId ? { id: r.vehicleId, etiket: r.plaka ?? r.vehicleId } : null,
    vade: gunDegeri(r.vade),
    taksitTutari: invariantOndalik(r.taksitTutari, { kesir: 2 }),
    doviz: r.doviz,
    kur: toNumber(r.kur),
    durum: r.durum === 'Odendi' ? 'Odendi' : 'Bekliyor',
    odemeTarihi: gunDegeri(r.odemeTarihi),
    aciklama: r.aciklama,
  };
}

/** Döviz kodu kırpılır; büyük harfe çevirme ve ISO doğrulaması sunucuda (Türkçe yerel "i" → "İ" tuzağı yok). */
const currency = (d: string | null) => metinDegeri(d);

/**
 * Form → `POST` (yeni) ya da tam değiştirme `PUT` gövdesi (düzenleme: `surum` zorunlu; ekranda olmayan araç-satış bağı
 * tabandan AYNEN geri gider). Dokunulmayan tarih sunucunun anıyla gider (gün yuvarlaması yok). Ödeme tarihi yalnız
 * "Ödendi"de anlamlı (sunucu Bekliyor'da yok sayar).
 */
export function installmentRequest(
  v: InstallmentFormValue,
  base: CustomerInstallment | null,
): CustomerInstallmentRequest {
  return {
    cariId: v.cari?.id ?? '',
    vehicleId: v.arac?.id ?? null,
    vehicleSaleId: base?.vehicleSaleId ?? null,
    vade: anDegeri(v.vade, base?.vade),
    taksitTutari: v.taksitTutari ?? '',
    doviz: currency(v.doviz),
    kur: rateToSend(currency(v.doviz), v.kur, base),
    durum: v.durum ?? 'Bekliyor',
    odemeTarihi: v.durum === 'Odendi' ? anDegeri(v.odemeTarihi, base?.odemeTarihi) : null,
    aciklama: metinDegeri(v.aciklama),
    ...(base === null ? {} : { surum: base.surum ?? null }),
  };
}

export function planRequest(v: PlanFormValue): InstallmentPlanRequest {
  return {
    cariId: v.cari?.id ?? '',
    vehicleId: v.arac?.id ?? null,
    toplamTutar: v.toplamTutar ?? '',
    taksitSayisi: v.taksitSayisi ?? 0,
    ilkVade: anDegeri(v.ilkVade, null),
    doviz: currency(v.doviz),
    kur: v.kur,
    aciklama: metinDegeri(v.aciklama),
  };
}
