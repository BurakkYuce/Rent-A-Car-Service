import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { toNumber } from '@features/vehicles/vehicle-model';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import type {
  Allocation,
  CustomerInstallment,
  DamageFile,
  FleetPlan,
  LoanRow,
  OrderRow,
} from './finance-model';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

const actions = <T>(t: Translate, width = 200): TabloSutunu<T> => ({
  kod: 'islemler',
  baslik: t('aracFinans.islemler'),
  deger: () => null,
  genislik: width,
});

/** Blazor AracKrediList tablosu, AYNI sırada. Para sağa yaslı; kalan bakiye kredinin dövizinde. */
export function loanColumns(t: Translate): readonly TabloSutunu<LoanRow>[] {
  const h = (k: string) => t(`aracFinans.kredi.sutun.${k}` as CeviriAnahtari);
  return [
    {
      kod: 'no',
      baslik: h('no'),
      deger: (r) => r.no,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 120,
    },
    { kod: 'dosyaNo', baslik: h('dosyaNo'), deger: (r) => r.dosyaNo ?? '—', genislik: 110 },
    { kod: 'bankaAdi', baslik: h('banka'), deger: (r) => r.bankaAdi, sirala: true, genislik: 140 },
    { kod: 'cari', baslik: h('cari'), deger: (r) => r.cariAd ?? '—', sirala: true, genislik: 160 },
    { kod: 'plaka', baslik: h('arac'), deger: (r) => r.plaka ?? '—', sirala: true, genislik: 110 },
    {
      kod: 'krediTutari',
      baslik: h('kredi'),
      deger: (r) => toNumber(r.krediTutari),
      tur: 'para',
      paraBirimi: (r) => r.doviz,
      sirala: true,
      genislik: 130,
    },
    {
      kod: 'aylikTaksit',
      baslik: h('aylikTaksit'),
      deger: (r) => toNumber(r.aylikTaksit),
      tur: 'para',
      paraBirimi: (r) => r.doviz,
      genislik: 120,
    },
    {
      kod: 'odenen',
      baslik: h('odenen'),
      deger: (r) => `${r.odenenTaksit} / ${r.taksitSayisi}`,
      hizala: 'son',
      genislik: 110,
    },
    {
      kod: 'kalanBakiye',
      baslik: h('kalanBakiye'),
      deger: (r) => toNumber(r.kalanBakiye),
      tur: 'para',
      paraBirimi: (r) => r.doviz,
      sirala: true,
      genislik: 140,
    },
    {
      kod: 'sonVadeGunu',
      baslik: h('sonVade'),
      deger: (r) => r.sonVadeGunu,
      tur: 'tarih',
      genislik: 110,
    },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 100 },
    actions<LoanRow>(t, 190),
  ];
}

/** Blazor MusteriTaksitList tablosu. Baz ₺ = tutar × kur (sunucu). */
export function customerInstallmentColumns(
  t: Translate,
): readonly TabloSutunu<CustomerInstallment>[] {
  const h = (k: string) => t(`aracFinans.taksit.sutun.${k}` as CeviriAnahtari);
  return [
    {
      kod: 'sira',
      baslik: h('sira'),
      deger: (r) => toNumber(r.sira),
      tur: 'sayi',
      haneler: '1.0-0',
      sirala: true,
      genislik: 60,
    },
    {
      kod: 'cari',
      baslik: h('musteri'),
      deger: (r) => r.cariAd,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 170,
    },
    { kod: 'plaka', baslik: h('arac'), deger: (r) => r.plaka ?? '—', sirala: true, genislik: 100 },
    {
      kod: 'vade',
      baslik: h('vade'),
      deger: (r) => r.vade,
      tur: 'tarih',
      sirala: true,
      genislik: 105,
    },
    {
      kod: 'taksitTutari',
      baslik: h('tutar'),
      deger: (r) => toNumber(r.taksitTutari),
      tur: 'sayi',
      haneler: '1.2-2',
      sirala: true,
      genislik: 120,
    },
    { kod: 'doviz', baslik: h('doviz'), deger: (r) => r.doviz, genislik: 70 },
    {
      kod: 'kur',
      baslik: h('kur'),
      deger: (r) => toNumber(r.kur),
      tur: 'sayi',
      haneler: '1.4-4',
      genislik: 90,
    },
    {
      kod: 'tutarBaz',
      baslik: h('baz'),
      deger: (r) => toNumber(r.tutarBaz),
      tur: 'para',
      sirala: true,
      genislik: 130,
    },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 100 },
    {
      kod: 'odemeTarihi',
      baslik: h('odemeTarihi'),
      deger: (r) => r.odemeTarihi,
      tur: 'tarih',
      genislik: 110,
    },
    { kod: 'aciklama', baslik: h('aciklama'), deger: (r) => r.aciklama ?? '—', genislik: 180 },
    actions<CustomerInstallment>(t, 230),
  ];
}

/** Blazor AracSiparisList tablosu (20 sütun). Piyasa/Ops/Filo BİLGİ — toplama girmez. */
export function orderColumns(
  t: Translate,
  loanLabel: (id: string | null) => string,
): readonly TabloSutunu<OrderRow>[] {
  const h = (k: string) => t(`aracFinans.siparis.sutun.${k}` as CeviriAnahtari);
  const info = (v: number | string | null) => toNumber(v);
  return [
    {
      kod: 'no',
      baslik: h('no'),
      deger: (r) => r.no,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 120,
    },
    { kod: 'dosyaNo', baslik: h('dosyaNo'), deger: (r) => r.dosyaNo ?? '—', genislik: 100 },
    {
      kod: 'tedarikci',
      baslik: h('tedarikci'),
      deger: (r) => r.tedarikci,
      sirala: true,
      genislik: 150,
    },
    { kod: 'cari', baslik: h('cari'), deger: (r) => r.tedarikciCariAd ?? '—', genislik: 150 },
    { kod: 'arac', baslik: h('arac'), deger: (r) => vehicleText(r), genislik: 200 },
    {
      kod: 'renk',
      baslik: h('renk'),
      deger: (r) => (r.renk ?? '—') + (r.icRenk ? ` / ${r.icRenk}` : ''),
      genislik: 110,
    },
    {
      kod: 'tipler',
      baslik: h('tipler'),
      deger: (r) => `${r.kaynakTip ?? '—'} / ${r.satisTipi ?? '—'}`,
      genislik: 130,
    },
    {
      kod: 'adet',
      baslik: h('adet'),
      deger: (r) => toNumber(r.adet),
      tur: 'sayi',
      haneler: '1.0-0',
      genislik: 70,
    },
    {
      kod: 'birimFiyat',
      baslik: h('birim'),
      deger: (r) => toNumber(r.birimFiyat),
      tur: 'sayi',
      haneler: '1.2-2',
      genislik: 120,
    },
    {
      kod: 'toplam',
      baslik: h('toplam'),
      deger: (r) => toNumber(r.toplam),
      tur: 'para',
      paraBirimi: (r) => r.doviz,
      sirala: true,
      genislik: 140,
    },
    {
      kod: 'piyasaFiyat',
      baslik: h('piyasa'),
      deger: (r) => info(r.piyasaFiyat),
      tur: 'sayi',
      haneler: '1.2-2',
      genislik: 110,
    },
    {
      kod: 'opsFiyat',
      baslik: h('ops'),
      deger: (r) => info(r.opsFiyat),
      tur: 'sayi',
      haneler: '1.2-2',
      genislik: 110,
    },
    {
      kod: 'filoFiyat',
      baslik: h('filo'),
      deger: (r) => info(r.filoFiyat),
      tur: 'sayi',
      haneler: '1.2-2',
      genislik: 110,
    },
    {
      kod: 'siparisTarihi',
      baslik: h('siparis'),
      deger: (r) => r.siparisTarihi,
      tur: 'tarih',
      sirala: true,
      genislik: 105,
    },
    { kod: 'imzaTarih', baslik: h('imza'), deger: (r) => r.imzaTarih, tur: 'tarih', genislik: 105 },
    {
      kod: 'beklenenTeslim',
      baslik: h('beklenen'),
      deger: (r) => r.beklenenTeslim,
      tur: 'tarih',
      sirala: true,
      genislik: 105,
    },
    { kod: 'tsbKayitNo', baslik: h('tsb'), deger: (r) => r.tsbKayitNo ?? '—', genislik: 100 },
    { kod: 'kredi', baslik: h('kredi'), deger: (r) => loanLabel(r.krediId), genislik: 160 },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 110 },
    actions<OrderRow>(t, 230),
  ];
}

/** Sipariş "Araç" hücresi: marka tip grup versiyon (boşluklar tekilleştirilir; Blazor ile aynı). */
export function vehicleText(r: Pick<OrderRow, 'marka' | 'tip' | 'grup' | 'versiyon'>): string {
  return [r.marka, r.tip, r.grup, r.versiyon]
    .map((x) => x?.trim() ?? '')
    .filter((x) => x !== '')
    .join(' ');
}

/** Blazor BafList tablosu. */
export function allocationColumns(
  t: Translate,
  purpose: (p: string | null) => string,
): readonly TabloSutunu<Allocation>[] {
  const h = (k: string) => t(`aracFinans.baf.sutun.${k}` as CeviriAnahtari);
  const withTime = (office: string | null, time: string | null) =>
    (office ?? '—') + (time ? ` · ${time.slice(0, 5)}` : '');
  return [
    {
      kod: 'no',
      baslik: h('no'),
      deger: (r) => r.no,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 110,
    },
    {
      kod: 'personel',
      baslik: h('personel'),
      deger: (r) => r.personelAd,
      sirala: true,
      genislik: 150,
    },
    { kod: 'plaka', baslik: h('arac'), deger: (r) => r.plaka, sirala: true, genislik: 100 },
    {
      kod: 'amac',
      baslik: h('amac'),
      deger: (r) =>
        purpose(r.kullanimAmaci) +
        (r.kirayaVer ? ` ${t('aracFinans.baf.kirayaVerilebilirEk')}` : ''),
      genislik: 190,
    },
    {
      kod: 'cikisOfisi',
      baslik: h('cikisOfisi'),
      deger: (r) => withTime(r.sube, r.cikisSaat),
      genislik: 140,
    },
    {
      kod: 'donusOfisi',
      baslik: h('donusOfisi'),
      deger: (r) => withTime(r.donusSube, r.donusSaat),
      genislik: 140,
    },
    {
      kod: 'cikisKm',
      baslik: h('cikisKm'),
      deger: (r) => toNumber(r.cikisKm),
      tur: 'sayi',
      haneler: '1.0-0',
      genislik: 100,
    },
    {
      kod: 'donusKm',
      baslik: h('donusKm'),
      deger: (r) => toNumber(r.donusKm),
      tur: 'sayi',
      haneler: '1.0-0',
      genislik: 100,
    },
    { kod: 'onaylayan', baslik: h('onaylayan'), deger: (r) => r.onaylayanAd ?? '—', genislik: 140 },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 90 },
    actions<Allocation>(t, 120),
  ];
}

/** Blazor DamageFileList tablosu. Tahmini tutar BİLGİ (deftere yazmaz). */
export function damageColumns(t: Translate): readonly TabloSutunu<DamageFile>[] {
  const h = (k: string) => t(`aracFinans.hasar.sutun.${k}` as CeviriAnahtari);
  return [
    {
      kod: 'no',
      baslik: h('no'),
      deger: (r) => r.no,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 110,
    },
    {
      kod: 'acilisTarihi',
      baslik: h('acilis'),
      deger: (r) => r.acilisTarihi,
      tur: 'tarih',
      sirala: true,
      genislik: 105,
    },
    { kod: 'plaka', baslik: h('arac'), deger: (r) => r.plaka, sirala: true, genislik: 100 },
    { kod: 'cari', baslik: h('sorumlu'), deger: (r) => r.cariAd ?? '—', genislik: 170 },
    {
      kod: 'tahminiTutar',
      baslik: h('tahmini'),
      deger: (r) => toNumber(r.tahminiTutar),
      tur: 'para',
      sirala: true,
      genislik: 130,
    },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 100 },
    actions<DamageFile>(t, 200),
  ];
}

/** Blazor FiloPlanList tablosu. */
export function fleetPlanColumns(t: Translate): readonly TabloSutunu<FleetPlan>[] {
  const h = (k: string) => t(`aracFinans.plan.sutun.${k}` as CeviriAnahtari);
  const all = t('aracFinans.plan.tumu');
  return [
    {
      kod: 'aracGrupAdi',
      baslik: h('grup'),
      deger: (r) => r.aracGrupAdi ?? all,
      sirala: true,
      genislik: 140,
    },
    { kod: 'sipp', baslik: h('sipp'), deger: (r) => r.sipp ?? all, sirala: true, genislik: 90 },
    { kod: 'donem', baslik: h('donem'), deger: (r) => r.donem ?? '—', sirala: true, genislik: 100 },
    {
      kod: 'hedefAdet',
      baslik: h('hedef'),
      deger: (r) => toNumber(r.hedefAdet),
      tur: 'sayi',
      haneler: '1.0-0',
      sirala: true,
      genislik: 80,
    },
    {
      kod: 'gerceklesen',
      baslik: h('gerceklesen'),
      deger: (r) => toNumber(r.gerceklesen),
      tur: 'sayi',
      haneler: '1.0-0',
      sirala: true,
      genislik: 100,
    },
    {
      kod: 'toplamKayitli',
      baslik: h('kayitli'),
      deger: (r) => toNumber(r.toplamKayitli),
      tur: 'sayi',
      haneler: '1.0-0',
      genislik: 80,
    },
    {
      kod: 'fark',
      baslik: h('fark'),
      deger: (r) => signed(toNumber(r.fark)),
      hizala: 'son',
      sirala: true,
      genislik: 70,
    },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, genislik: 120 },
    { kod: 'aciklama', baslik: h('aciklama'), deger: (r) => r.aciklama ?? '—', genislik: 180 },
    actions<FleetPlan>(t, 220),
  ];
}

/** Fark gösterimi: pozitif "+3" (Blazor), sıfır ve negatif olduğu gibi. */
export function signed(n: number | null): string {
  if (n === null) return '—';
  return n > 0 ? `+${n}` : String(n);
}
