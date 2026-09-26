import type { KabukSayaclariDegeri } from '@core/sayac/shell-counters';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';

import type { MenuKaydi, MenuModeli } from './menu-modeli';

/** Kenar çubuğu kısayol çifti: `+ Kira` (dolu) · `+ Rezervasyon` (çerçeveli). */
export interface KisayolCifti {
  readonly kira: MenuKaydi | null;
  readonly rezervasyon: MenuKaydi | null;
}

/**
 * Kısayol çifti sunucunun HIZLI BAĞLANTILARINDAN seçilir (izin süzmesi sunucuda — izni olmayan kullanıcıya
 * öğe gelmez, düğme de çizilmez; istemci izin adı yazmaz). "Yeni Kira" → kira formu; "Yeni Rezervasyon" SPA'da
 * liste yerine rezervasyon formuna (`/rezervasyonlar/yeni`) gider, Blazor hedefi olduğu gibi kalır.
 */
export function shortcutPair(model: MenuModeli): KisayolCifti {
  let rental: MenuKaydi | null = null;
  let reservation: MenuKaydi | null = null;
  for (const record of model.hizli) {
    const path = targetPath(record);
    if (!rental && /\/kiralar\/yeni$/.test(path)) rental = record;
    else if (!reservation && /\/rezervasyonlar(\/yeni)?$/.test(path)) {
      reservation =
        record.hedef.tur === 'spa' && record.hedef.yol === '/rezervasyonlar'
          ? { ...record, hedef: { tur: 'spa', yol: '/rezervasyonlar/yeni' } }
          : record;
    }
  }
  return { kira: rental, rezervasyon: reservation };
}

/** Kira listesinin ön ayarlı görünümleri (Yol v2 §9: `kiralar?gorunum=…`; ön ayarı liste ekranı uygular). */
export const VIEW_CODES = [
  'kirada',
  'geciken',
  'bugun-cikan',
  'bugun-donecek',
  'faturasiz',
  'kapali',
] as const;
export type ViewCode = (typeof VIEW_CODES)[number];

export interface KayitliGorunum {
  /** `null` = tüm sözleşmeler (ön ayarsız liste). */
  readonly kod: ViewCode | null;
  readonly etiket: CeviriAnahtari;
  readonly kayit: MenuKaydi;
  readonly sayac: keyof KabukSayaclariDegeri | null;
  /** Kırmızı rozet (eylem gerek). */
  readonly hata: boolean;
}

const VIEWS: readonly {
  readonly kod: ViewCode | null;
  readonly etiket: CeviriAnahtari;
  readonly sayac: keyof KabukSayaclariDegeri | null;
  readonly hata?: true;
}[] = [
  { kod: null, etiket: 'kabuk.gorunum.tum', sayac: null },
  { kod: 'kirada', etiket: 'kabuk.gorunum.kirada', sayac: 'kirada' },
  { kod: 'geciken', etiket: 'kabuk.gorunum.geciken', sayac: 'geciken', hata: true },
  { kod: 'bugun-cikan', etiket: 'kabuk.gorunum.bugunCikan', sayac: 'bugunCikan' },
  { kod: 'bugun-donecek', etiket: 'kabuk.gorunum.bugunDonecek', sayac: 'bugunDonecek' },
  { kod: 'faturasiz', etiket: 'kabuk.gorunum.faturasiz', sayac: null },
  { kod: 'kapali', etiket: 'kabuk.gorunum.kapali', sayac: null },
];

/** SPA kira listesi öğesi mi (kayıtlı görünümler yalnız SPA listesinde — Blazor listesi ön ayarı bilmez). */
export function isRentalList(record: MenuKaydi): boolean {
  return record.hedef.tur === 'spa' && record.hedef.yol === '/kiralar';
}

/** Kira listesi öğesinden kayıtlı görünüm bağlantıları; ilki ("Tüm sözleşmeler") öğenin kendisi. */
export function rentalViews(rentals: MenuKaydi): KayitliGorunum[] {
  return VIEWS.map((g) => ({
    kod: g.kod,
    etiket: g.etiket,
    sayac: g.sayac,
    hata: g.hata === true,
    kayit:
      g.kod === null
        ? rentals
        : {
            ...rentals,
            kimlik: `${rentals.kimlik}-${g.kod}`,
            hedef: { tur: 'spa', yol: `/kiralar?gorunum=${g.kod}` },
          },
  }));
}

function targetPath(record: MenuKaydi): string {
  return record.hedef.tur === 'spa' ? record.hedef.yol : record.hedef.adres;
}
