import { sayiBicimle } from '@core/bicim/bicim';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { type MusaitlikSatiri, sayi } from './musaitlik-modeli';

type Ceviri = (anahtar: CeviriAnahtari, parametreler?: Record<string, unknown>) => string;

export type SutunKodu =
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
export function provizyonMetni(satir: MusaitlikSatiri): string | null {
  const p = sayi(satir.provizyon);
  if (p === null) return null;
  const tutar = sayiBicimle(p, '1.2-2');
  return satir.provizyonDoviz ? `${tutar} ${satir.provizyonDoviz}` : tutar;
}

/**
 * Blazor MusaitlikArama tablosunun 25 sütunu (FAZ-48/73). Km limiti araç kaydı → grup varsayılanı yönü,
 * yaş, boşta gün ve fiyat SUNUCUDAN (UI formül taşımaz). Fiyat motorun verdiği dövizde; kur çevirimi yok.
 */
export function musaitlikSutunlari(t: Ceviri): readonly TabloSutunu<MusaitlikSatiri>[] {
  const s = (kod: SutunKodu) => t(`musaitlikSayfasi.sutun.${kod}`);
  const tam = { tur: 'sayi', haneler: '1.0-0' } as const;
  const doviz = (r: MusaitlikSatiri) => r.fiyat?.paraBirimi ?? 'TRY';
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
    { kod: 'yas', baslik: s('yas'), deger: (r) => sayi(r.yas), ...tam, genislik: 56 },
    { kod: 'yakit', baslik: s('yakit'), deger: (r) => r.yakit, genislik: 90 },
    { kod: 'vites', baslik: s('vites'), deger: (r) => r.vites, genislik: 90 },
    { kod: 'renk', baslik: s('renk'), deger: (r) => r.renk, genislik: 80 },
    { kod: 'sipp', baslik: s('sipp'), deger: (r) => r.sipp, genislik: 70 },
    { kod: 'grup', baslik: s('grup'), deger: (r) => r.grup, genislik: 80 },
    { kod: 'sube', baslik: s('sube'), deger: (r) => r.sube, genislik: 110 },
    {
      kod: 'kmLimiti',
      baslik: s('kmLimiti'),
      deger: (r) => sayi(r.kmLimiti),
      ...tam,
      genislik: 80,
    },
    {
      kod: 'minSurucuYas',
      baslik: s('minSurucuYas'),
      deger: (r) => sayi(r.minSurucuYas),
      ...tam,
      genislik: 90,
    },
    {
      kod: 'minEhliyetYil',
      baslik: s('minEhliyetYil'),
      deger: (r) => sayi(r.minEhliyetYil),
      ...tam,
      genislik: 90,
    },
    {
      kod: 'provizyon',
      baslik: s('provizyon'),
      deger: provizyonMetni,
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
    { kod: 'km', baslik: s('km'), deger: (r) => sayi(r.km), ...tam, genislik: 80 },
    // Hiç kiralanmamış araçta boş ("—"): 0 yazmak "dün döndü" ile karışırdı.
    {
      kod: 'bostaGun',
      baslik: s('bostaGun'),
      deger: (r) => sayi(r.bostaGun),
      ...tam,
      genislik: 80,
    },
    { kod: 'sonMusteri', baslik: s('sonMusteri'), deger: (r) => r.sonMusteri, genislik: 140 },
    {
      kod: 'gunluk',
      baslik: s('gunluk'),
      deger: (r) => sayi(r.fiyat?.gunluk),
      tur: 'para',
      paraBirimi: doviz,
      genislik: 110,
    },
    {
      kod: 'toplam',
      baslik: s('toplam'),
      deger: (r) => sayi(r.fiyat?.toplam),
      tur: 'para',
      paraBirimi: doviz,
      genislik: 120,
    },
    { kod: 'doviz', baslik: s('doviz'), deger: (r) => r.fiyat?.paraBirimi ?? null, genislik: 64 },
    { kod: 'kirala', baslik: s('kirala'), deger: () => null, gizlenemez: true, genislik: 110 },
  ];
}
