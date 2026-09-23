import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { toNumber, type DetailedRow } from '../vehicle-model';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

/**
 * Blazor VehicleDetayList'in 49 sütunu, AYNI sırada. "Aktif kira" sütunları kira kayıtlarından CANLI; "(not)"
 * sütunları elle girilen anlık görüntüdür. Boş metin "—" (Blazor `G()`), plaka solda sabit.
 */
export function detailedColumns(t: Translate): readonly TabloSutunu<DetailedRow>[] {
  const h = (code: string) => t(`arac.detayli.sutun.${code}` as CeviriAnahtari);
  const dash = (v: string | null | undefined) =>
    v === null || v === undefined || v.trim() === '' ? '—' : v;
  const text = (code: keyof DetailedRow & string, width = 120): TabloSutunu<DetailedRow> => ({
    kod: code,
    baslik: h(code),
    deger: (r) => dash(r[code] as string | null),
    genislik: width,
  });
  const sortable = (c: TabloSutunu<DetailedRow>): TabloSutunu<DetailedRow> => ({
    ...c,
    sirala: true,
  });
  const num = (code: keyof DetailedRow & string): TabloSutunu<DetailedRow> => ({
    kod: code,
    baslik: h(code),
    deger: (r) => toNumber(r[code] as number | string | null),
    tur: 'sayi',
    haneler: '1.0-0',
    genislik: 100,
  });
  const money = (code: keyof DetailedRow & string, currency = 'TRY'): TabloSutunu<DetailedRow> => ({
    kod: code,
    baslik: h(code),
    deger: (r) => toNumber(r[code] as number | string | null),
    tur: 'para',
    paraBirimi: currency,
    genislik: 130,
  });
  const date = (code: keyof DetailedRow & string): TabloSutunu<DetailedRow> => ({
    kod: code,
    baslik: h(code),
    deger: (r) => r[code],
    tur: 'tarih',
    genislik: 105,
  });

  return [
    {
      kod: 'plaka',
      baslik: h('plaka'),
      deger: (r) => r.plaka,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 110,
    },
    sortable(text('marka', 110)),
    text('tip', 110),
    sortable(text('grup', 110)),
    sortable(text('sube', 110)),
    sortable(text('durum', 110)),
    text('belgeNo'),
    text('ruhsatSahibi', 140),
    text('sozNo'),
    text('araciAlan', 130),
    text('alimYapilanFirma', 140),
    sortable(date('alimTarihi')),
    sortable(money('alimBedeli')),
    { ...text('alisEuro', 80), deger: (r) => (r.alisEuro ? t('arac.evet') : '—') },
    money('alisEuroFiyat', 'EUR'),
    money('satisEuroFiyat', 'EUR'),
    text('krediBanka', 130),
    sortable(date('muayeneBitis')),
    sortable(date('kaskoBitis')),
    sortable(date('trafikBitis')),
    text('aktifKiraMusteri', 160),
    text('aktifKiraSozlesmeNo', 140),
    sortable(date('aktifKiraBitis')),
    text('kiralayan', 130),
    num('kiraGun'),
    money('kiraFiyat'),
    date('kiraBitTar'),
    date('kiraBekTar'),
    num('sonTeslimKm'),
    date('sonTeslimTarihi'),
    text('assistanFirma', 130),
    text('hgsFirma', 130),
    num('disKmLimit'),
    text('tsbKodu'),
    money('tsbKaskoDegeri'),
    text('odemeSekli'),
    money('satisHedefFiyat'),
    date('ihaleTarihi'),
    text('ihaleFirmasi', 130),
    date('noterSatisTarihi'),
    sortable(date('filoGirisTarih')),
    date('filoCikisTarih'),
    text('pasifSebep', 140),
    text('sonDurum', 140),
    text('ozelKod1', 100),
    text('ozelKod2', 100),
    text('ozelKod3', 100),
    text('ozelKod4', 100),
    text('ozelKod5', 100),
  ];
}
