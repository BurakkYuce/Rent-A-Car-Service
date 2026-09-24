import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { toNumber } from '@features/vehicles/vehicle-model';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import type { CustomerRow } from './customer-model';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

/**
 * Blazor `CustomerList` tablosu, AYNI sırada. Fark (KVKK): "TC / Vergi No" sütunu yalnız VERGİ NO gösterir — TC hiçbir
 * yanıtta yok; bireysel caride vergi no da dönmez (şahıs vergi no = TC olabilir). İl, e-posta, ülke ek (gizli) sütun.
 */
export function customerColumns(t: Translate): readonly TabloSutunu<CustomerRow>[] {
  const h = (k: string) => t(`cari.sutun.${k}` as CeviriAnahtari);
  const dash = (v: string | null) => (v === null || v.trim() === '' ? '—' : v);
  return [
    {
      kod: 'tip',
      baslik: h('tip'),
      deger: (r) => t(`cari.tipler.${r.tip}` as CeviriAnahtari),
      sirala: true,
      genislik: 90,
    },
    {
      kod: 'ad',
      baslik: h('ad'),
      deger: (r) => r.ad,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 200,
    },
    { kod: 'vergiNo', baslik: h('vergiNo'), deger: (r) => dash(r.vergiNo), genislik: 110 },
    { kod: 'cepTel', baslik: h('cepTel'), deger: (r) => dash(r.cepTel), genislik: 120 },
    { kod: 'gsm2', baslik: h('gsm2'), deger: (r) => dash(r.gsm2), genislik: 120 },
    {
      kod: 'il',
      baslik: h('il'),
      deger: (r) => dash(r.il),
      sirala: true,
      gizli: true,
      genislik: 100,
    },
    { kod: 'ilce', baslik: h('ilce'), deger: (r) => dash(r.ilce), sirala: true, genislik: 100 },
    { kod: 'email', baslik: h('email'), deger: (r) => dash(r.email), gizli: true, genislik: 180 },
    {
      kod: 'musteriTemsilcisi',
      baslik: h('temsilci'),
      deger: (r) => dash(r.musteriTemsilcisi),
      sirala: true,
      genislik: 130,
    },
    {
      kod: 'entegrasyonKodu',
      baslik: h('entegrasyon'),
      deger: (r) => dash(r.entegrasyonKodu),
      genislik: 110,
    },
    { kod: 'ozelKod', baslik: h('ozelKod'), deger: (r) => dash(r.ozelKod), genislik: 100 },
    { kod: 'sinif', baslik: h('sinif'), deger: (r) => dash(r.sinif), sirala: true, genislik: 90 },
    { kod: 'ulke', baslik: h('ulke'), deger: (r) => dash(r.ulke), gizli: true, genislik: 100 },
    {
      kod: 'vadeGun',
      baslik: h('vade'),
      deger: (r) => toNumber(r.vadeGun),
      tur: 'sayi',
      sirala: true,
      genislik: 90,
    },
    {
      kod: 'kaynak',
      baslik: h('kaynak'),
      deger: (r) => dash(r.kaynak),
      sirala: true,
      genislik: 110,
    },
    {
      kod: 'kiraAdet',
      baslik: h('kira'),
      deger: (r) => toNumber(r.kiraAdet),
      tur: 'sayi',
      genislik: 70,
    },
    { kod: 'ciro', baslik: h('ciro'), deger: (r) => toNumber(r.ciro), tur: 'para', genislik: 130 },
    { kod: 'sonKira', baslik: h('sonKira'), deger: (r) => r.sonKira, tur: 'tarih', genislik: 105 },
    { kod: 'durum', baslik: h('durum'), deger: () => null, genislik: 200 },
    { kod: 'islemler', baslik: t('cari.islemler'), deger: () => null, genislik: 220 },
  ];
}
