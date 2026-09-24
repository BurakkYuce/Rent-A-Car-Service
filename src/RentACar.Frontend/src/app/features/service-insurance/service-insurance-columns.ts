import { tarihBicimle } from '@core/bicim/bicim';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import {
  type DueItem,
  type InspectionRow,
  type MtvRow,
  type PolicyRow,
  type ServiceRecordRow,
  num,
} from './service-insurance-model';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

const yesNo = (t: Translate, v: boolean) => t(v ? 'servisSigorta.evet' : 'servisSigorta.hayir');

/** Blazor "Plan" sütunu: "baş → bit", yalnız biri varsa o; ikisi de yoksa "—". */
export function planText(start: string | null, end: string | null): string {
  if (!start && !end) return '—';
  if (start && !end) return tarihBicimle(start);
  if (!start) return `→ ${tarihBicimle(end)}`;
  return `${tarihBicimle(start)} → ${tarihBicimle(end)}`;
}

/** Blazor ServiceRecordList tablosu (KDV / genel toplam kayıt detayında — satır DTO'su taşımaz). */
export function serviceColumns(
  t: Translate,
  status: (s: string) => string,
  type: (s: string) => string,
): readonly TabloSutunu<ServiceRecordRow>[] {
  const h = (k: string) => t(`servisSigorta.servis.sutun.${k}` as CeviriAnahtari);
  return [
    {
      kod: 'no',
      baslik: h('no'),
      deger: (r) => r.no,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 130,
    },
    { kod: 'plaka', baslik: h('arac'), deger: (r) => r.plaka, sirala: true, genislik: 110 },
    { kod: 'tip', baslik: h('tur'), deger: (r) => type(r.tip), sirala: true, genislik: 100 },
    {
      kod: 'girisTarihi',
      baslik: h('giris'),
      deger: (r) => r.girisTarihi,
      tur: 'tarih',
      sirala: true,
      genislik: 110,
    },
    {
      kod: 'plan',
      baslik: h('plan'),
      deger: (r) => planText(r.planBasTarihi, r.planBitTarihi),
      genislik: 170,
    },
    {
      kod: 'km',
      baslik: h('km'),
      deger: (r) => (r.cikisKm !== null ? `${r.girisKm} → ${r.cikisKm}` : `${r.girisKm}`),
      hizala: 'son',
      genislik: 120,
    },
    { kod: 'atolyeAdi', baslik: h('atolye'), deger: (r) => r.atolyeAdi ?? '—', genislik: 140 },
    {
      kod: 'toplamIscilik',
      baslik: h('iscilik'),
      deger: (r) => num(r.toplamIscilik),
      tur: 'para',
      sirala: true,
      genislik: 130,
    },
    {
      kod: 'durum',
      baslik: h('durum'),
      deger: (r) => status(r.durum),
      sirala: true,
      genislik: 110,
    },
    {
      kod: 'yansitildi',
      baslik: h('yansitildi'),
      deger: (r) => yesNo(t, r.yansitildi),
      genislik: 90,
    },
  ];
}

export function policyColumns(t: Translate): readonly TabloSutunu<PolicyRow>[] {
  const h = (k: string) => t(`servisSigorta.sigorta.sutun.${k}` as CeviriAnahtari);
  const money = (k: string, v: (r: PolicyRow) => number | string | null) =>
    ({
      kod: k,
      baslik: h(k),
      deger: (r: PolicyRow) => num(v(r)),
      tur: 'para',
      paraBirimi: (r: PolicyRow) => r.doviz,
      genislik: 120,
    }) as TabloSutunu<PolicyRow>;
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
    { kod: 'tip', baslik: h('tip'), deger: (r) => r.tip, sirala: true, genislik: 90 },
    { kod: 'policeNo', baslik: h('policeNo'), deger: (r) => r.policeNo ?? '—', genislik: 120 },
    { kod: 'firma', baslik: h('firma'), deger: (r) => r.firma ?? '—', genislik: 140 },
    {
      kod: 'bitis',
      baslik: h('bitis'),
      deger: (r) => r.bitis,
      tur: 'tarih',
      sirala: true,
      genislik: 110,
    },
    { ...money('prim', (r) => r.prim), sirala: true },
    money('aracDegeri', (r) => r.aracDegeri),
    money('immDegeri', (r) => r.immDegeri),
    money('aksesuarDegeri', (r) => r.aksesuarDegeri),
    { ...money('kalan', (r) => r.kalan), sirala: true },
    {
      kod: 'odendi',
      baslik: h('odendi'),
      deger: (r) => yesNo(t, r.odendi),
      sirala: true,
      genislik: 90,
    },
  ];
}

export function mtvColumns(t: Translate): readonly TabloSutunu<MtvRow>[] {
  const h = (k: string) => t(`servisSigorta.mtv.sutun.${k}` as CeviriAnahtari);
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
    { kod: 'donem', baslik: h('donem'), deger: (r) => r.donem, sirala: true, genislik: 100 },
    {
      kod: 'tutar',
      baslik: h('tutar'),
      deger: (r) => num(r.tutar),
      tur: 'para',
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'kalan',
      baslik: h('kalan'),
      deger: (r) => num(r.kalan),
      tur: 'para',
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'vade',
      baslik: h('vade'),
      deger: (r) => r.vade,
      tur: 'tarih',
      sirala: true,
      genislik: 110,
    },
    {
      kod: 'odendi',
      baslik: h('odendi'),
      deger: (r) => yesNo(t, r.odendi),
      sirala: true,
      genislik: 90,
    },
    { kod: 'aciklama', baslik: h('aciklama'), deger: (r) => r.aciklama ?? '—', genislik: 180 },
  ];
}

export function inspectionColumns(t: Translate): readonly TabloSutunu<InspectionRow>[] {
  const h = (k: string) => t(`servisSigorta.muayene.sutun.${k}` as CeviriAnahtari);
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
      kod: 'muayeneTarihi',
      baslik: h('muayene'),
      deger: (r) => r.muayeneTarihi,
      tur: 'tarih',
      sirala: true,
      genislik: 110,
    },
    {
      kod: 'bitis',
      baslik: h('bitis'),
      deger: (r) => r.bitis,
      tur: 'tarih',
      sirala: true,
      genislik: 110,
    },
    {
      kod: 'ucret',
      baslik: h('ucret'),
      deger: (r) => num(r.ucret),
      tur: 'para',
      sirala: true,
      genislik: 110,
    },
    { kod: 'ceza', baslik: h('ceza'), deger: (r) => num(r.ceza), tur: 'para', genislik: 100 },
    {
      kod: 'kalan',
      baslik: h('kalan'),
      deger: (r) => num(r.kalan),
      tur: 'para',
      sirala: true,
      genislik: 110,
    },
    {
      kod: 'islemKm',
      baslik: h('islemKm'),
      deger: (r) => num(r.islemKm),
      tur: 'sayi',
      genislik: 100,
    },
    {
      kod: 'odendi',
      baslik: h('odendi'),
      deger: (r) => yesNo(t, r.odendi),
      sirala: true,
      genislik: 90,
    },
    { kod: 'aciklama', baslik: h('aciklama'), deger: (r) => r.aciklama ?? '—', genislik: 180 },
  ];
}

export function dueColumns(
  t: Translate,
  bucket: (b: string) => string,
): readonly TabloSutunu<DueItem>[] {
  const h = (k: string) => t(`servisSigorta.vade.sutun.${k}` as CeviriAnahtari);
  return [
    {
      kod: 'plaka',
      baslik: h('arac'),
      deger: (r) => r.plaka,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 110,
    },
    { kod: 'tur', baslik: h('tur'), deger: (r) => r.tur, sirala: true, genislik: 110 },
    {
      kod: 'bitis',
      baslik: h('bitis'),
      deger: (r) => r.bitis,
      tur: 'tarih',
      sirala: true,
      genislik: 110,
    },
    {
      kod: 'kalanGun',
      baslik: h('kalanGun'),
      deger: (r) => num(r.kalanGun),
      tur: 'sayi',
      sirala: true,
      genislik: 100,
    },
    { kod: 'kova', baslik: h('durum'), deger: (r) => bucket(r.kova), genislik: 110 },
  ];
}
