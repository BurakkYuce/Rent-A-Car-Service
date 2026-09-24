import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { toNumber } from '@features/vehicles/vehicle-model';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import type {
  ExpenseRow,
  IncomingInvoiceRow,
  InvoiceLineRow,
  InvoiceRow,
  PenaltyRow,
  VehicleSaleRow,
} from './document-model';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

const actions = <T>(t: Translate, width = 180): TabloSutunu<T> => ({
  kod: 'islemler',
  baslik: t('finansBelge.islemler'),
  deger: () => null,
  genislik: width,
});

function money<T>(
  kod: string,
  baslik: string,
  value: (r: T) => number | string | null | undefined,
  currency: (r: T) => string,
  sirala: boolean | string = false,
): TabloSutunu<T> {
  return {
    kod,
    baslik,
    deger: (r) => toNumber(value(r)),
    tur: 'para',
    paraBirimi: currency,
    sirala,
    genislik: 120,
  };
}

/** Blazor InvoiceList tablosu (KVKK: vergi no/ülke/cari kodu sunucudan gelmez — sütun yok). */
export function invoiceColumns(t: Translate): readonly TabloSutunu<InvoiceRow>[] {
  const h = (k: string) => t(`finansBelge.fatura.sutun.${k}` as CeviriAnahtari);
  const c = (r: InvoiceRow) => r.doviz;
  return [
    {
      kod: 'no',
      baslik: h('no'),
      deger: (r) => r.no,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 160,
    },
    {
      kod: 'tarih',
      baslik: h('tarih'),
      deger: (r) => r.tarih,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    { kod: 'cariAd', baslik: h('cari'), deger: (r) => r.cariAd, sirala: true, genislik: 180 },
    { kod: 'sozlesmeNo', baslik: h('sozlesme'), deger: (r) => r.sozlesmeNo ?? '—', genislik: 130 },
    { kod: 'plaka', baslik: h('plaka'), deger: (r) => r.plaka ?? '—', genislik: 100 },
    { kod: 'ofis', baslik: h('ofis'), deger: (r) => r.ofis ?? '—', genislik: 120 },
    money('netTutar', h('net'), (r) => r.netTutar, c),
    money('kdvTutar', h('kdv'), (r) => r.kdvTutar, c),
    money('genelToplam', h('toplam'), (r) => r.genelToplam, c, true),
    { kod: 'doviz', baslik: h('doviz'), deger: (r) => r.doviz, sirala: true, genislik: 70 },
    { kod: 'tur', baslik: h('tur'), deger: (r) => r.iadeMi, genislik: 90 },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 90 },
    actions(t, 200),
  ];
}

/** Blazor InvoiceLineList (satır bazında; işaretli TL toplamı iadede eksi). */
export function invoiceLineColumns(t: Translate): readonly TabloSutunu<InvoiceLineRow>[] {
  const h = (k: string) => t(`finansBelge.faturaSatir.sutun.${k}` as CeviriAnahtari);
  const c = (r: InvoiceLineRow) => r.doviz;
  return [
    {
      kod: 'faturaNo',
      baslik: h('faturaNo'),
      deger: (r) => r.faturaNo,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 160,
    },
    {
      kod: 'tarih',
      baslik: h('tarih'),
      deger: (r) => r.tarih,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    { kod: 'cariAd', baslik: h('cari'), deger: (r) => r.cariAd, sirala: true, genislik: 170 },
    { kod: 'aciklama', baslik: h('aciklama'), deger: (r) => r.aciklama, genislik: 200 },
    {
      kod: 'miktar',
      baslik: h('miktar'),
      deger: (r) => toNumber(r.miktar),
      tur: 'sayi',
      genislik: 70,
    },
    money('birimNetFiyat', h('birim'), (r) => r.birimNetFiyat, c),
    {
      kod: 'kdvOrani',
      baslik: h('kdvOrani'),
      deger: (r) => toNumber(r.kdvOrani),
      tur: 'sayi',
      haneler: '1.0-4',
      sirala: true,
      genislik: 80,
    },
    money('satirNet', h('net'), (r) => r.satirNet, c),
    money('satirKdv', h('kdv'), (r) => r.satirKdv, c),
    money('satirToplam', h('toplam'), (r) => r.satirToplam, c, true),
    money(
      'isaretliToplamTl',
      h('toplamTl'),
      (r) => r.isaretliToplamTl,
      () => 'TRY',
    ),
    { kod: 'sozlesmeNo', baslik: h('sozlesme'), deger: (r) => r.sozlesmeNo ?? '—', genislik: 130 },
    { kod: 'plaka', baslik: h('plaka'), deger: (r) => r.plaka ?? '—', genislik: 100 },
    { kod: 'cikisOfisi', baslik: h('ofis'), deger: (r) => r.cikisOfisi ?? '—', genislik: 120 },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, genislik: 90 },
  ];
}

/** Blazor PenaltyList (KVKK: e-posta ve ihbarname telefonu listede yok). Tutarlar TRY. */
export function penaltyColumns(t: Translate): readonly TabloSutunu<PenaltyRow>[] {
  const h = (k: string) => t(`finansBelge.ceza.sutun.${k}` as CeviriAnahtari);
  const c = () => 'TRY';
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
    { kod: 'cezaTuru', baslik: h('tur'), deger: (r) => r.cezaTuru, genislik: 150 },
    {
      kod: 'tebligTarihi',
      baslik: h('teblig'),
      deger: (r) => r.tebligTarihi,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    { kod: 'saat', baslik: h('saat'), deger: (r) => r.saat ?? '—', genislik: 70 },
    {
      kod: 'vadeTarihi',
      baslik: h('vade'),
      deger: (r) => r.vadeTarihi,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    { kod: 'plaka', baslik: h('arac'), deger: (r) => r.plaka ?? '—', sirala: true, genislik: 100 },
    {
      kod: 'cariAd',
      baslik: h('musteri'),
      deger: (r) => r.cariAd ?? '—',
      sirala: true,
      genislik: 170,
    },
    { kod: 'sozlesmeNo', baslik: h('sozlesme'), deger: (r) => r.sozlesmeNo ?? '—', genislik: 130 },
    { kod: 'faturaNo', baslik: h('fatura'), deger: (r) => r.faturaNo ?? '—', genislik: 150 },
    { kod: 'islemSube', baslik: h('sube'), deger: (r) => r.islemSube ?? '—', genislik: 110 },
    { kod: 'makbuzNo', baslik: h('makbuz'), deger: (r) => r.makbuzNo ?? '—', genislik: 100 },
    money('tutar', h('tutar'), (r) => r.tutar, c, true),
    money('odenenTutar', h('odenen'), (r) => r.odenenTutar, c),
    money('kalan', h('kalan'), (r) => r.kalan, c, true),
    {
      kod: 'odenmeTarihi',
      baslik: h('odemeTarihi'),
      deger: (r) => r.odenmeTarihi,
      tur: 'tarih',
      genislik: 100,
    },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 100 },
    actions(t, 220),
  ];
}

/** Blazor ExpenseList. Ödenen/kalan yalnız açık hesap (takip edilen) giderde anlamlı. */
export function expenseColumns(t: Translate): readonly TabloSutunu<ExpenseRow>[] {
  const h = (k: string) => t(`finansBelge.gider.sutun.${k}` as CeviriAnahtari);
  const c = (r: ExpenseRow) => r.doviz;
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
    { kod: 'tip', baslik: h('tur'), deger: (r) => r.tip, sirala: true, genislik: 90 },
    {
      kod: 'tarih',
      baslik: h('tarih'),
      deger: (r) => r.tarih,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    {
      kod: 'odemeTarihi',
      baslik: h('odemeTarihi'),
      deger: (r) => r.odemeTarihi,
      tur: 'tarih',
      genislik: 100,
    },
    { kod: 'plaka', baslik: h('arac'), deger: (r) => r.plaka ?? '—', sirala: true, genislik: 100 },
    {
      kod: 'cariAd',
      baslik: h('tedarikci'),
      deger: (r) => r.cariAd ?? '—',
      sirala: true,
      genislik: 160,
    },
    { kod: 'sube', baslik: h('sube'), deger: (r) => r.sube ?? '—', sirala: true, genislik: 110 },
    { kod: 'evrakNo', baslik: h('evrakNo'), deger: (r) => r.evrakNo ?? '—', genislik: 100 },
    { kod: 'aciklama', baslik: h('aciklama'), deger: (r) => r.aciklama ?? '—', genislik: 180 },
    money('netTutar', h('net'), (r) => r.netTutar, c),
    money('kdvTutar', h('kdv'), (r) => r.kdvTutar, c),
    money('genelToplam', h('toplam'), (r) => r.genelToplam, c, true),
    { kod: 'odemeYontemi', baslik: h('odeme'), deger: (r) => r.odemeYontemi, genislik: 100 },
    money('odenen', h('odenen'), (r) => (r.takipEdilir ? r.odenen : null), c),
    money('kalan', h('kalan'), (r) => (r.takipEdilir ? r.kalan : null), c, true),
    actions(t, 120),
  ];
}

/** Blazor GelenEFaturaList (KDV oran kırılımı dahil). */
export function incomingColumns(t: Translate): readonly TabloSutunu<IncomingInvoiceRow>[] {
  const h = (k: string) => t(`finansBelge.gelen.sutun.${k}` as CeviriAnahtari);
  const c = (r: IncomingInvoiceRow) => r.doviz;
  return [
    {
      kod: 'ettn',
      baslik: h('ettn'),
      deger: (r) => r.ettn,
      sirala: true,
      sabit: true,
      gizlenemez: true,
      genislik: 170,
    },
    {
      kod: 'gonderenUnvan',
      baslik: h('gonderen'),
      deger: (r) => r.gonderenUnvan,
      sirala: true,
      genislik: 180,
    },
    { kod: 'gonderenVkn', baslik: h('vkn'), deger: (r) => r.gonderenVkn, genislik: 110 },
    {
      kod: 'tarih',
      baslik: h('tarih'),
      deger: (r) => r.tarih,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    money('netTutar', h('net'), (r) => r.netTutar, c),
    money('kdvTutar', h('kdv'), (r) => r.kdvTutar, c),
    money('genelToplam', h('genel'), (r) => r.genelToplam, c, true),
    money('kdv20Matrah', h('kdv20Matrah'), (r) => r.kdv20Matrah, c),
    money('kdv20', h('kdv20'), (r) => r.kdv20, c),
    money('kdv10Matrah', h('kdv10Matrah'), (r) => r.kdv10Matrah, c),
    money('kdv10', h('kdv10'), (r) => r.kdv10, c),
    money('kdv1Matrah', h('kdv1Matrah'), (r) => r.kdv1Matrah, c),
    money('kdv1', h('kdv1'), (r) => r.kdv1, c),
    money('kdv0Matrah', h('kdv0Matrah'), (r) => r.kdv0Matrah, c),
    { kod: 'plaka', baslik: h('arac'), deger: (r) => r.plaka ?? '—', genislik: 100 },
    { kod: 'cariAd', baslik: h('tedarikci'), deger: (r) => r.cariAd ?? '—', genislik: 160 },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 110 },
    {
      kod: 'giderlestirilmeTarihi',
      baslik: h('defter'),
      deger: (r) => r.giderlestirilmeTarihi,
      tur: 'tarih',
      genislik: 100,
    },
    actions(t, 260),
  ];
}

/** Blazor VehicleSaleList. */
export function saleColumns(t: Translate): readonly TabloSutunu<VehicleSaleRow>[] {
  const h = (k: string) => t(`finansBelge.satis.sutun.${k}` as CeviriAnahtari);
  const c = (r: VehicleSaleRow) => r.doviz;
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
    {
      kod: 'tarih',
      baslik: h('tarih'),
      deger: (r) => r.tarih,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    { kod: 'plaka', baslik: h('arac'), deger: (r) => r.plaka, sirala: true, genislik: 100 },
    { kod: 'aliciAd', baslik: h('alici'), deger: (r) => r.aliciAd, sirala: true, genislik: 170 },
    money('satisNet', h('net'), (r) => r.satisNet, c),
    money('kdvTutar', h('kdv'), (r) => r.kdvTutar, c),
    money('genelToplam', h('toplam'), (r) => r.genelToplam, c, true),
    { kod: 'noterNo', baslik: h('noter'), deger: (r) => r.noterNo ?? '—', genislik: 100 },
    { kod: 'ihaleFirmasi', baslik: h('ihale'), deger: (r) => r.ihaleFirmasi ?? '—', genislik: 120 },
    { kod: 'satisKanali', baslik: h('kanal'), deger: (r) => r.satisKanali ?? '—', genislik: 110 },
    { kod: 'satisiVerildi', baslik: h('devir'), deger: (r) => r.satisiVerildi, genislik: 90 },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, genislik: 100 },
  ];
}
