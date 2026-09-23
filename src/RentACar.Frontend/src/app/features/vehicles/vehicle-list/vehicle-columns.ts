import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { toNumber, type VehicleListRow } from '../vehicle-model';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

/**
 * Blazor VehicleList tablosunun 49 sütunu (48 veri + işlem), AYNI sırada. Plaka solda sabit ve gizlenemez;
 * para sütunları sağa yaslı (`tur: 'para'`). Son sekiz veri sütunu başka tablolardan canlı çözülür (kira,
 * servis, BAF, satış, kasko, kredi). Sıralanabilirler sunucunun `SiralamaHaritasi` ile aynı.
 */
export function vehicleColumns(t: Translate): readonly TabloSutunu<VehicleListRow>[] {
  const h = (code: string) => t(`arac.sutun.${code}` as CeviriAnahtari);
  const flag = (value: boolean, yes: CeviriAnahtari) => (value ? t(yes) : '—');
  const text = (
    code: keyof VehicleListRow & string,
    width = 120,
    sortable = false,
  ): TabloSutunu<VehicleListRow> => ({
    kod: code,
    baslik: h(code),
    deger: (r) => r[code],
    genislik: width,
    ...(sortable ? { sirala: true } : {}),
  });
  const num = (
    code: keyof VehicleListRow & string,
    sortable = false,
  ): TabloSutunu<VehicleListRow> => ({
    kod: code,
    baslik: h(code),
    deger: (r) => toNumber(r[code] as number | string | null),
    tur: 'sayi',
    haneler: '1.0-0',
    genislik: 100,
    ...(sortable ? { sirala: true } : {}),
  });
  const money = (
    code: keyof VehicleListRow & string,
    sortable = false,
  ): TabloSutunu<VehicleListRow> => ({
    kod: code,
    baslik: h(code),
    deger: (r) => toNumber(r[code] as number | string | null),
    tur: 'para',
    genislik: 130,
    ...(sortable ? { sirala: true } : {}),
  });
  const date = (
    code: keyof VehicleListRow & string,
    sortable = false,
  ): TabloSutunu<VehicleListRow> => ({
    kod: code,
    baslik: h(code),
    deger: (r) => r[code],
    tur: 'tarih',
    genislik: 105,
    ...(sortable ? { sirala: true } : {}),
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
    text('marka', 110, true),
    text('tip', 110, true),
    text('detayTipi', 110, true),
    text('grup', 110, true),
    text('segment', 100, true),
    text('sipp', 70, true),
    num('modelYili', true),
    text('renk', 90, true),
    text('vites', 90),
    { ...text('yakit', 90), deger: (r) => r.yakit ?? '—' },
    num('km', true),
    text('sube', 110, true),
    { ...text('durum', 110, true) },
    text('filoDurum', 110, true),
    text('sasiNo', 150),
    text('motorNo', 130),
    num('motorGucu'),
    text('aracSahibi', 130, true),
    text('hgsNo'),
    text('ogsNo'),
    text('kasaTipi'),
    num('sonBakimKm', true),
    money('alimBedeli', true),
    date('filoGirisTarih', true),
    date('filoCikisTarih', true),
    date('tescilTarihi', true),
    text('alimYapilanFirma', 140),
    text('ruhsatNo'),
    money('ikinciElDeger', true),
    money('tsbKaskoDegeri', true),
    { ...text('yedekAnahtar', 90), deger: (r) => flag(r.yedekAnahtar, 'arac.var') },
    { ...text('karLastigi', 90), deger: (r) => flag(r.karLastigi, 'arac.var') },
    text('lastikDurumu'),
    { ...text('zIzni', 80), deger: (r) => flag(r.zIzni, 'arac.var') },
    text('sonDurum', 140),
    text('konum'),
    text('takipNo'),
    text('teypKodu'),
    text('aciklama', 200),
    { ...text('aktifKiraSozlesmeNo', 140), deger: (r) => r.aktifKiraSozlesmeNo ?? '—' },
    { ...text('acikServis', 80), deger: (r) => flag(r.acikServis, 'arac.acik') },
    { ...text('acikBaf', 80), deger: (r) => flag(r.acikBaf, 'arac.tahsisli') },
    { ...text('satisVar', 80), deger: (r) => flag(r.satisVar, 'arac.var') },
    date('kaskoBitis'),
    money('kaskoPrim'),
    text('krediKurulusu', 140),
    date('krediSonTarih'),
    { kod: 'islemler', baslik: h('islemler'), deger: () => null, genislik: 170 },
  ];
}
