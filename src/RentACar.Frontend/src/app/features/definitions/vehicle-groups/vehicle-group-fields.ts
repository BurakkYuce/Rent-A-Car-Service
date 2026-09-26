import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TanimAlani } from '@shared/form/tanim-crud/definition-source';

import { type SuggestionSource, commonFields } from '../definition-catalog';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

/** Sunucu enum adları (`FuelType`, `Vites`); boş = belirtilmemiş. */
const FUEL_TYPES = ['Benzin', 'Dizel', 'Lpg', 'Elektrik', 'Hibrit'] as const;
const GEARS = ['Manuel', 'Otomatik'] as const;

/**
 * Araç grubu (Blazor `VehicleGroupList` formunun 38 alanı + Durum). Tutarlar para (numeric(19,4) uçta), sayılar tam
 * sayı, "Sonra öde" yüzde (2 hane). Sınırlar uç `GroupInput` ile aynı (SIPP 8, segment/marka/tipi 64, kasa 32,
 * döviz 3, kimlikler 64). `aracSayisi` salt okunur liste sütunu (sunucu hesaplar; gövdede yok sayılır).
 */
export function vehicleGroupFields(
  t: Translate,
  suggest: { readonly brand: SuggestionSource },
): readonly TanimAlani[] {
  const f = commonFields(t);
  const g = (k: string) => t(`tanimlar.vehicleGroup.alan.${k}` as CeviriAnahtari);
  const text = (name: string, max: number, inList = false): TanimAlani =>
    f.text(name, g(name), max, { inList });
  const int = (name: string, inList = false): TanimAlani => ({
    ad: name,
    etiket: g(name),
    tur: 'sayi',
    inList,
  });
  const amount = (name: string, inList = false): TanimAlani => ({
    ad: name,
    etiket: g(name),
    tur: 'para',
    inList,
  });
  const enumChoice = (name: string, values: readonly string[]): TanimAlani => ({
    ad: name,
    etiket: g(name),
    tur: 'secim',
    inList: false,
    secenekler: [
      { deger: null, etiket: '—' },
      ...values.map((v) => ({ deger: v, etiket: g(`${name}${v}`) })),
    ],
  });
  return [
    f.code(),
    f.name(),
    text('aciklama', 512),
    text('sipp', 8, true),
    text('segment', 64, true),
    text('kasaTuru', 32),
    { ...text('marka', 64), tur: 'datalist', suggestions: suggest.brand },
    text('tipi', 64),
    int('koltukSayisi'),
    int('kapiSayisi'),
    int('bagajSayisi'),
    int('kucukBagaj'),
    int('buyukBagaj'),
    int('surucuMinYas', true),
    int('gencSurucuYas'),
    amount('gencSurucuUcretGunluk'),
    amount('ekSurucuUcretGunluk'),
    int('ehliyetMinYil', true),
    int('gencEhliyetMinYil'),
    amount('provizyon', true),
    text('provizyonDoviz', 3),
    amount('provizyon2'),
    text('provizyon2Doviz', 3),
    amount('muafiyetTutari'),
    amount('muafiyet2'),
    int('gunlukKmLimiti', true),
    int('aylikMaxKm'),
    amount('asimKmUcreti'),
    amount('yakitFiyati'),
    { ad: 'sonraOdeOran', etiket: g('sonraOdeOran'), tur: 'sayi', fraction: 2, inList: false },
    {
      ad: 'krediKartiSart',
      etiket: g('krediKartiSart'),
      tur: 'secim',
      inList: false,
      secenekler: [
        { deger: null, etiket: '—' },
        { deger: true, etiket: f.l('evet') },
        { deger: false, etiket: f.l('hayir') },
      ],
    },
    int('webSira'),
    int('upgradeSira'),
    enumChoice('yakitTuru', FUEL_TYPES),
    enumChoice('vites', GEARS),
    text('entegrasyonKod1', 64),
    text('webId', 64, true),
    text('servisId', 64, true),
    f.active(),
  ];
}
