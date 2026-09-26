import { sayiBicimle } from '@core/bicim/bicim';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { type AvailabilityRow, count } from './musaitlik-modeli';

type Translation = (key: CeviriAnahtari, parameters?: Record<string, unknown>) => string;

export type ColumnCode =
  | 'plaka'
  | 'marka'
  | 'tip'
  | 'modelYili'
  | 'yas'
  | 'yakit'
  | 'vites'
  | 'renk'
  | 'sipp'
  | 'grup'
  | 'sube'
  | 'kmLimiti'
  | 'minSurucuYas'
  | 'minEhliyetYil'
  | 'provizyon'
  | 'karLastigi'
  | 'temizlik'
  | 'ozelKod1'
  | 'km'
  | 'bostaGun'
  | 'sonMusteri'
  | 'gunluk'
  | 'toplam'
  | 'doviz'
  | 'kirala';

/**
 * Provizyon (bloke) BİLGİDİR: teklif toplamına girmez; grup kendi dövizini taşımıyorsa yalnız tutar
 * (uydurma para birimi basılmaz) — Blazor `Provizyon(grup)` ile aynı.
 */
export function preAuthText(row: AvailabilityRow): string | null {
  const p = count(row.provizyon);
  if (p === null) return null;
  const amount = sayiBicimle(p, '1.2-2');
  return row.provizyonDoviz ? `${amount} ${row.provizyonDoviz}` : amount;
}

/**
 * Blazor MusaitlikArama tablosunun 25 sütunu (FAZ-48/73). Km limiti araç kaydı → grup varsayılanı yönü,
 * yaş, boşta gün ve fiyat SUNUCUDAN (UI formül taşımaz). Fiyat motorun verdiği dövizde; kur çevirimi yok.
 */
export function availabilityColumns(t: Translation): readonly TabloSutunu<AvailabilityRow>[] {
  const s = (code: ColumnCode) => t(`musaitlikSayfasi.sutun.${code}`);
  const full = { tur: 'sayi', haneler: '1.0-0' } as const;
  const currency = (r: AvailabilityRow) => r.fiyat?.paraBirimi ?? 'TRY';
  return [
    { kod: 'plaka', baslik: s('plaka'), deger: (r) => r.plaka, sabit: true, genislik: 124 },
    { kod: 'marka', baslik: s('marka'), deger: (r) => r.marka, genislik: 100 },
    { kod: 'tip', baslik: s('tip'), deger: (r) => r.tip, genislik: 100 },
    // Yıl binlik ayraçsız ("2.024" değil): metin olarak, sağa yaslı.
    {
      kod: 'modelYili',
      baslik: s('modelYili'),
      deger: (r) => (r.modelYili === null ? null : String(r.modelYili)),
      hizala: 'son',
      genislik: 64,
    },
    { kod: 'yas', baslik: s('yas'), deger: (r) => count(r.yas), ...full, genislik: 56 },
    { kod: 'yakit', baslik: s('yakit'), deger: (r) => r.yakit, genislik: 90 },
    { kod: 'vites', baslik: s('vites'), deger: (r) => r.vites, genislik: 90 },
    { kod: 'renk', baslik: s('renk'), deger: (r) => r.renk, genislik: 80 },
    { kod: 'sipp', baslik: s('sipp'), deger: (r) => r.sipp, genislik: 70 },
    { kod: 'grup', baslik: s('grup'), deger: (r) => r.grup, genislik: 80 },
    { kod: 'sube', baslik: s('sube'), deger: (r) => r.sube, genislik: 110 },
    {
      kod: 'kmLimiti',
      baslik: s('kmLimiti'),
      deger: (r) => count(r.kmLimiti),
      ...full,
      genislik: 80,
    },
    {
      kod: 'minSurucuYas',
      baslik: s('minSurucuYas'),
      deger: (r) => count(r.minSurucuYas),
      ...full,
      genislik: 90,
    },
    {
      kod: 'minEhliyetYil',
      baslik: s('minEhliyetYil'),
      deger: (r) => count(r.minEhliyetYil),
      ...full,
      genislik: 90,
    },
    {
      kod: 'provizyon',
      baslik: s('provizyon'),
      deger: preAuthText,
      hizala: 'son',
      genislik: 120,
    },
    {
      kod: 'karLastigi',
      baslik: s('karLastigi'),
      deger: (r) => (r.karLastigi ? t('musaitlikSayfasi.var') : null),
      genislik: 80,
    },
    {
      kod: 'temizlik',
      baslik: s('temizlik'),
      deger: (r) => (r.temizlik ? t('musaitlikSayfasi.temiz') : null),
      genislik: 80,
    },
    { kod: 'ozelKod1', baslik: s('ozelKod1'), deger: (r) => r.ozelKod1, genislik: 90 },
    { kod: 'km', baslik: s('km'), deger: (r) => count(r.km), ...full, genislik: 80 },
    // Hiç kiralanmamış araçta boş ("—"): 0 yazmak "dün döndü" ile karışırdı.
    {
      kod: 'bostaGun',
      baslik: s('bostaGun'),
      deger: (r) => count(r.bostaGun),
      ...full,
      genislik: 80,
    },
    { kod: 'sonMusteri', baslik: s('sonMusteri'), deger: (r) => r.sonMusteri, genislik: 140 },
    {
      kod: 'gunluk',
      baslik: s('gunluk'),
      deger: (r) => count(r.fiyat?.gunluk),
      tur: 'para',
      paraBirimi: currency,
      genislik: 110,
    },
    {
      kod: 'toplam',
      baslik: s('toplam'),
      deger: (r) => count(r.fiyat?.toplam),
      tur: 'para',
      paraBirimi: currency,
      genislik: 120,
    },
    { kod: 'doviz', baslik: s('doviz'), deger: (r) => r.fiyat?.paraBirimi ?? null, genislik: 64 },
    { kod: 'kirala', baslik: s('kirala'), deger: () => null, gizlenemez: true, genislik: 110 },
  ];
}
