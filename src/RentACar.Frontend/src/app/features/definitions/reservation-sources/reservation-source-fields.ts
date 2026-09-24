import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TanimAlani } from '@shared/form/tanim-crud/tanim-kaynagi';

import { commonFields } from '../definition-catalog';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

/** `RezKaynakGrubu` enum adları (sunucu adla alır; boş = belirtilmemiş). */
export const SOURCE_GROUPS = [
  'OfisSatis',
  'Broker',
  'Acente',
  'RentACar',
  'Otel',
  'Diger',
] as const;

/** Davranış DEĞİŞTİREN kural bayrakları (FAZ-49 `RezKaynakKural`); listede özetlenir. */
export const RULE_FLAGS = [
  'uzatamaz',
  'rezTarihleriDegisemez',
  'provizyonYok',
  'kmSinirsiz',
  'ayniYonDrop',
] as const;

/** Yalnız BİLGİ olan işaretler (hiçbir fiyat/fatura/defter hesabına girmez). */
const INFO_FLAGS = [
  'scdwDahil',
  'cdwDahil',
  'lcfDahil',
  'paiDahil',
  'maliyetYansitma',
  'matrisErken',
  'matrisGecikme',
  'matrisIptal',
  'matrisNoShow',
  'matrisUzatma',
  'otomatikMailGitme',
  'riskAnalizYapma',
  'subeGor',
  'acenteFiyatDegistir',
  'gizle',
  'sadeceMusteriOdeme',
] as const;

/**
 * Rezervasyon kaynağı (Blazor `ReservationSourceList` formunun tüm alanları). Oranlar YÜZDE (12,5 = %12,5; 0–100,
 * 2 hane), ek hizmet tutarları para; sınırlar uç `ReservationSourceLimits` + servis `Ek` ile aynı. Tam PUT: formda
 * her alan var, gönderilmeyen alan kalmaz.
 */
export function reservationSourceFields(t: Translate): readonly TanimAlani[] {
  const f = commonFields(t);
  const r = (k: string) => t(`tanimlar.reservationSource.alan.${k}` as CeviriAnahtari);
  const rate = (ad: string, inList = false): TanimAlani => ({
    ad,
    etiket: r(ad),
    tur: 'sayi',
    fraction: 2,
    inList,
  });
  const amount = (ad: string): TanimAlani => ({ ad, etiket: r(ad), tur: 'para', inList: false });
  const text = (ad: string, max: number, inList = false): TanimAlani =>
    f.text(ad, r(ad), max, { inList });
  const flag = (ad: string): TanimAlani => ({ ad, etiket: r(ad), tur: 'onay', inList: false });
  return [
    f.code(),
    f.name(),
    {
      ad: 'kaynakGrubu',
      etiket: r('kaynakGrubu'),
      tur: 'secim',
      secenekler: [
        { deger: null, etiket: r('grupYok') },
        ...SOURCE_GROUPS.map((g) => ({ deger: g, etiket: r(`grup${g}`) })),
      ],
    },
    text('tedarikci', 128, true),
    rate('kiraOrani', true),
    rate('hizmetOrani', true),
    rate('dropOrani', true),
    rate('komisyonOrani', true),
    ...RULE_FLAGS.map(flag),
    { ad: 'maxGun', etiket: r('maxGun'), tur: 'sayi', inList: false, placeholder: r('sinirsiz') },
    rate('onOdemeOrani'),
    rate('indirimOrani'),
    rate('puanOrani'),
    amount('bebekKoltugu'),
    amount('navigasyon'),
    amount('ekSurucu'),
    amount('wifi'),
    text('sigortaKaynakNo', 64),
    text('dropKaynakNo', 64),
    text('provizyonSecenek', 64),
    text('muafiyatSecenek', 64),
    text('mailAdres', 256),
    ...INFO_FLAGS.map(flag),
    f.active(),
  ];
}
