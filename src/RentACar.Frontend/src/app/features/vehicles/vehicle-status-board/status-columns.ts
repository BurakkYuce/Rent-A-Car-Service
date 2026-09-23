import { tarihBicimle } from '@core/bicim/bicim';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { toNumber, type StatusRow } from '../vehicle-model';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

/** Kalan gün metni (Blazor `KalanMetin`): 0 = "bugün", negatif = gecikme. */
export function remainingText(t: Translate, days: number | string | null | undefined): string {
  const n = toNumber(days);
  if (n === null) return '—';
  if (n === 0) return t('arac.durum.bugun');
  if (n < 0) return t('arac.durum.gecikme', { gun: -n });
  return t('arac.durum.gunKaldi', { gun: n });
}

/** Rezervasyona kapalılık (Blazor `RezKapaliMetin`). */
export function closedText(t: Translate, r: StatusRow): string {
  if (r.webRezKapat && r.ofisRezKapat) return t('arac.durum.webOfis');
  if (r.webRezKapat) return t('arac.durum.web');
  if (r.ofisRezKapat) return t('arac.durum.ofis');
  return '—';
}

const joined = (...parts: (string | null | undefined)[]) =>
  parts
    .filter((p) => p && p.trim() !== '')
    .join(' ')
    .trim();

/**
 * Blazor FleetStatus tablosunun 24 sütunu, AYNI sırada. Müşteri adı/telefonu sunucuda `MusteriGorunumu`
 * kuralıyla (KVKK) çözülmüş gelir. Bakiye para (sağa yaslı), KM sayı.
 */
export function statusColumns(t: Translate): readonly TabloSutunu<StatusRow>[] {
  const h = (code: string) => t(`arac.durum.sutun.${code}` as CeviriAnahtari);
  const dash = (v: string | null | undefined) =>
    v === null || v === undefined || v.trim() === '' ? '—' : v;
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
    {
      kod: 'markaTip',
      baslik: h('markaTip'),
      deger: (r) => joined(r.marka, r.tip),
      sirala: 'marka',
      genislik: 150,
    },
    { kod: 'grup', baslik: h('grup'), deger: (r) => r.grup, sirala: true, genislik: 100 },
    {
      kod: 'filoDurum',
      baslik: h('filoDurum'),
      deger: (r) => dash(r.filoDurum),
      sirala: true,
      genislik: 100,
    },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 105 },
    {
      kod: 'kiraSozlesmeNo',
      baslik: h('kiraSozlesmeNo'),
      deger: (r) => dash(r.kiraSozlesmeNo),
      genislik: 140,
    },
    { kod: 'musteriAd', baslik: h('musteriAd'), deger: (r) => dash(r.musteriAd), genislik: 150 },
    { kod: 'musteriTel', baslik: h('musteriTel'), deger: (r) => dash(r.musteriTel), genislik: 120 },
    {
      kod: 'kiraBitTar',
      baslik: h('kiraBitTar'),
      deger: (r) => r.kiraBitTar,
      tur: 'tarih',
      sirala: true,
      genislik: 105,
    },
    {
      kod: 'kiraKalanGun',
      baslik: h('kiraKalanGun'),
      deger: (r) => remainingText(t, r.kiraKalanGun),
      hizala: 'son',
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'kiraBakiye',
      baslik: h('kiraBakiye'),
      deger: (r) => toNumber(r.kiraBakiye),
      tur: 'para',
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'rez',
      baslik: h('rez'),
      deger: (r) =>
        r.rezMusteriAd === null
          ? '—'
          : `${r.rezMusteriAd} (${tarihBicimle(r.rezBasTar).slice(0, 5)})`,
      sirala: 'rezBasTar',
      genislik: 170,
    },
    {
      kod: 'servis',
      baslik: h('servis'),
      deger: (r) => (r.acikServisNo === null ? '—' : joined(r.acikServisNo, r.servisAtolye)),
      genislik: 150,
    },
    {
      kod: 'baf',
      baslik: h('baf'),
      deger: (r) => (r.aktifBafNo === null ? '—' : joined(r.aktifBafNo, r.bafPersonelAd)),
      genislik: 150,
    },
    { kod: 'dosyaNo', baslik: h('dosyaNo'), deger: (r) => dash(r.dosyaNo), genislik: 110 },
    { kod: 'pasifSebep', baslik: h('pasifSebep'), deger: (r) => dash(r.pasifSebep), genislik: 130 },
    { kod: 'konum', baslik: h('konum'), deger: (r) => dash(r.konum), genislik: 110 },
    { kod: 'takipNo', baslik: h('takipNo'), deger: (r) => dash(r.takipNo), genislik: 110 },
    { kod: 'hgsNo', baslik: h('hgsNo'), deger: (r) => dash(r.hgsNo), genislik: 110 },
    {
      kod: 'karLastigi',
      baslik: h('karLastigi'),
      deger: (r) => (r.karLastigi ? t('arac.var') : '—'),
      genislik: 80,
    },
    { kod: 'rezKapali', baslik: h('rezKapali'), deger: (r) => closedText(t, r), genislik: 100 },
    {
      kod: 'km',
      baslik: h('km'),
      deger: (r) => toNumber(r.km),
      tur: 'sayi',
      haneler: '1.0-0',
      sirala: true,
      genislik: 90,
    },
    { kod: 'sube', baslik: h('sube'), deger: (r) => r.sube, sirala: true, genislik: 110 },
    { kod: 'islemler', baslik: h('islemler'), deger: () => null, genislik: 200 },
  ];
}
