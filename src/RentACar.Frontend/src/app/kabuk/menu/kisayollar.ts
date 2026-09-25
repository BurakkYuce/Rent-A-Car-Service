import type { KabukSayaclariDegeri } from '@core/sayac/kabuk-sayaclari';
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
export function kisayolCifti(model: MenuModeli): KisayolCifti {
  let kira: MenuKaydi | null = null;
  let rezervasyon: MenuKaydi | null = null;
  for (const kayit of model.hizli) {
    const yol = hedefYolu(kayit);
    if (!kira && /\/kiralar\/yeni$/.test(yol)) kira = kayit;
    else if (!rezervasyon && /\/rezervasyonlar(\/yeni)?$/.test(yol)) {
      rezervasyon =
        kayit.hedef.tur === 'spa' && kayit.hedef.yol === '/rezervasyonlar'
          ? { ...kayit, hedef: { tur: 'spa', yol: '/rezervasyonlar/yeni' } }
          : kayit;
    }
  }
  return { kira, rezervasyon };
}

/** Kira listesinin ön ayarlı görünümleri (Yol v2 §9: `kiralar?gorunum=…`; ön ayarı liste ekranı uygular). */
export const GORUNUM_KODLARI = [
  'kirada',
  'geciken',
  'bugun-cikan',
  'bugun-donecek',
  'faturasiz',
  'kapali',
] as const;
export type GorunumKodu = (typeof GORUNUM_KODLARI)[number];

export interface KayitliGorunum {
  /** `null` = tüm sözleşmeler (ön ayarsız liste). */
  readonly kod: GorunumKodu | null;
  readonly etiket: CeviriAnahtari;
  readonly kayit: MenuKaydi;
  readonly sayac: keyof KabukSayaclariDegeri | null;
  /** Kırmızı rozet (eylem gerek). */
  readonly hata: boolean;
}

const GORUNUMLER: readonly {
  readonly kod: GorunumKodu | null;
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
export function kiraListesiMi(kayit: MenuKaydi): boolean {
  return kayit.hedef.tur === 'spa' && kayit.hedef.yol === '/kiralar';
}

/** Kira listesi öğesinden kayıtlı görünüm bağlantıları; ilki ("Tüm sözleşmeler") öğenin kendisi. */
export function kiraGorunumleri(kiralar: MenuKaydi): KayitliGorunum[] {
  return GORUNUMLER.map((g) => ({
    kod: g.kod,
    etiket: g.etiket,
    sayac: g.sayac,
    hata: g.hata === true,
    kayit:
      g.kod === null
        ? kiralar
        : {
            ...kiralar,
            kimlik: `${kiralar.kimlik}-${g.kod}`,
            hedef: { tur: 'spa', yol: `/kiralar?gorunum=${g.kod}` },
          },
  }));
}

function hedefYolu(kayit: MenuKaydi): string {
  return kayit.hedef.tur === 'spa' ? kayit.hedef.yol : kayit.hedef.adres;
}
