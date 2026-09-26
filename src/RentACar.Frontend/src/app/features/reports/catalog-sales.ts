import type { Schema } from '@core/api/ui-tipleri';

import {
  VIEW_REPORTS,
  type ReportColumn,
  type ReportDefinition,
  type RowOf,
  type SummaryOf,
  cardsFor,
  columnsFor,
  defineReport,
  defineView,
  fractionToPercent,
  section,
} from './report-model';

/**
 * F10.2 — satış/kârlılık raporları (uç `ReportApi.Sales`): araç kârlılığı (+ boyut özeti), ek hizmet (+ araç pivotu,
 * satır detayı), günlük faaliyet. Hepsi firma geneli + ViewReports.
 */

const R = '/api/ui/v1/raporlar';

/** Araç karnesi bağlantısı (kimliksiz satır — silinmiş araç — bağlantısız). */
export const scorecardLink = (id: string | null | undefined) =>
  id ? ['/raporlar/arac-karne', id] : null;

// ── Kârlılık (+ boyut özeti) ────────────────────────────────────────────────────────────────
const ka = cardsFor<SummaryOf<`${typeof R}/karlilik`>>();
const kaRow = columnsFor<RowOf<`${typeof R}/karlilik`>>();
const ko = cardsFor<SummaryOf<`${typeof R}/karlilik/ozet`>>();
const koRow = columnsFor<Schema<'KarlilikOzetSatirDto'>>();
export const PROFITABILITY = defineReport({
  kod: 'karlilik',
  baslik: 'rapor.baslik.karlilik',
  aciklama: 'rapor.aciklama.karlilik',
  grup: 'satis',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/karlilik`, {
      kod: 'arac',
      baslik: 'rapor.gorunum.aracBazli',
      filtreler: [
        { tur: 'donem' },
        { tur: 'sube', ad: 'sube', baslik: 'rapor.alan.sube' },
        { tur: 'metin', ad: 'grup', baslik: 'rapor.alan.grup' },
        { tur: 'metin', ad: 'plaka', baslik: 'rapor.alan.plaka', enFazla: 20 },
        { tur: 'metin', ad: 'kaynak', baslik: 'rapor.alan.rezKaynagi' },
        { tur: 'metin', ad: 'sipp', baslik: 'rapor.alan.sipp', enFazla: 10 },
        { tur: 'bayrak', ad: 'kdvDahil', baslik: 'rapor.alan.kdvDahil' },
      ],
      kartlar: [
        ka.field('toplamGelir', 'para'),
        ka.field('toplamGider', 'para'),
        ka.field('toplamNetKar', 'para', { isaretli: true }),
        ka.field('toplamPotansiyelGelir', 'para'),
        ka.field('toplamReferansMaliyet', 'para'),
        ka.field('toplamHesaplananKdv', 'para'),
      ],
      satirlar: {
        siralanabilir: ['plaka', 'gelir', 'gider', 'netKar', 'sube', 'grup', 'dolulukYuzde'],
        satirKimligi: (r) => r.vehicleId ?? `plaka:${r.plaka}`,
        sutunlar: [
          kaRow.field('plaka', 'metin', {
            sirala: true,
            sabit: true,
            bag: (r) => scorecardLink(r.vehicleId),
          }),
          kaRow.field('sube', 'metin', { sirala: true }),
          kaRow.field('grup', 'metin', { sirala: true }),
          kaRow.field('segment', 'metin', { gizli: true }),
          kaRow.field('sipp', 'metin', { gizli: true }),
          kaRow.field('gelir', 'para', { sirala: true }),
          kaRow.field('gider', 'para', { sirala: true }),
          kaRow.field('netKar', 'para', { sirala: true }),
          kaRow.field('dolulukYuzde', 'yuzde', { sirala: true }),
          kaRow.field('revPacd', 'para'),
          kaRow.field('adr', 'para'),
          kaRow.field('kiraAdet', 'tamsayi'),
          kaRow.field('kiralananGun', 'tamsayi'),
          kaRow.field('sahiplikGun', 'tamsayi', { gizli: true }),
          kaRow.field('potansiyelGelir', 'para', { gizli: true }),
          kaRow.field('referansAylikMaliyet', 'para', { gizli: true }),
          kaRow.field('referansFiloYonetimMaliyeti', 'para', { gizli: true }),
          kaRow.field('referansToplamMaliyet', 'para', { gizli: true }),
          kaRow.field('hesaplananKdv', 'para', { gizli: true }),
          kaRow.field('gelirKdvDahil', 'para', { gizli: true }),
          kaRow.field('otopark', 'metin', { gizli: true }),
          kaRow.field('rezKaynagi', 'metin', { gizli: true }),
          kaRow.field('cariAd', 'metin', { gizli: true, baslik: 'rapor.alan.cari' }),
          kaRow.field('cariBakiye', 'para', { gizli: true }),
        ],
      },
    }),
    defineView(`${R}/karlilik/ozet`, {
      kod: 'boyut',
      baslik: 'rapor.gorunum.boyutOzeti',
      filtreler: [
        { tur: 'donem' },
        {
          tur: 'secim',
          ad: 'kirilim',
          param: 'boyut',
          baslik: 'rapor.alan.kirilim',
          bosEtiket: 'rapor.secenek.grupVarsayilan',
          secenekler: [
            { deger: 'sube', etiket: 'rapor.alan.sube' },
            { deger: 'segment', etiket: 'rapor.alan.segment' },
            { deger: 'otopark', etiket: 'rapor.alan.otopark' },
            { deger: 'sipp', etiket: 'rapor.alan.sipp' },
          ],
        },
      ],
      kartlar: [
        ko.field('toplamGelir', 'para'),
        ko.field('toplamGider', 'para'),
        ko.field('toplamNetKar', 'para', { isaretli: true }),
      ],
      bolumler: [
        section({
          kod: 'boyut',
          baslik: 'rapor.bolum.boyutKirilimi',
          satirlar: (s) => s.satirlar,
          sutunlar: [
            koRow.field('boyut', 'metin', { baslik: 'rapor.alan.kirilim' }),
            koRow.field('aracAdet', 'tamsayi'),
            koRow.field('gelir', 'para'),
            koRow.field('gider', 'para'),
            koRow.field('netKar', 'para'),
            koRow.field('aracBasiGelir', 'para'),
            koRow.field('dolulukYuzde', 'yuzde'),
            koRow.field('potansiyelGelir', 'para'),
            koRow.field('referansToplamMaliyet', 'para'),
          ],
        }),
      ],
    }),
  ],
});

// ── Ek hizmet (özet + araç pivotu + satır detayı) ───────────────────────────────────────────
const eh = cardsFor<SummaryOf<`${typeof R}/ek-hizmet`>>();
const ehRow = columnsFor<Schema<'EkHizmetRaporRowDto'>>();
const pv = cardsFor<SummaryOf<`${typeof R}/ek-hizmet/arac-pivot`>>();
type PivotRow = Schema<'EkHizmetAracPivotSatir'>;
const pvRow = columnsFor<PivotRow>();
const dt = cardsFor<SummaryOf<`${typeof R}/ek-hizmet/detay`>>();
const dtRow = columnsFor<RowOf<`${typeof R}/ek-hizmet/detay`>>();

/** Pivot sütunları sunucunun `kolonlar` sırasıyla (hizmet adı başlık; metin sözlükte değil — veri). */
function pivotColumns(columns: readonly string[]): readonly ReportColumn<PivotRow>[] {
  return [
    pvRow.field('plaka', 'metin', { sabit: true }),
    pvRow.field('grup', 'metin'),
    pvRow.field('sipp', 'metin'),
    ...columns.map((name, i) =>
      pvRow.computed(`k${i}`, 'para', (r) => r.hucreler[i], {
        baslik: 'rapor.alan.hizmet',
        baslikMetni: name,
      }),
    ),
    pvRow.field('toplam', 'para'),
    pvRow.field('kalemAdet', 'tamsayi'),
  ];
}

export const ADDON_SALES = defineReport({
  kod: 'ek-hizmet',
  baslik: 'rapor.baslik.ekHizmet',
  aciklama: 'rapor.aciklama.ekHizmet',
  grup: 'satis',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/ek-hizmet`, {
      kod: 'ozet',
      baslik: 'rapor.gorunum.ozet',
      filtreler: [{ tur: 'donem' }],
      kartlar: [
        eh.field('toplamNet', 'para'),
        eh.field('toplamKdv', 'para'),
        eh.field('toplamBrut', 'para'),
        eh.field('kiraAdet', 'tamsayi'),
      ],
      bolumler: [
        section({
          kod: 'hizmetler',
          baslik: 'rapor.bolum.hizmetler',
          satirlar: (s) => s.satirlar,
          sutunlar: [
            ehRow.field('ad', 'metin', { baslik: 'rapor.alan.hizmet' }),
            ehRow.field('toplamMiktar', 'sayi'),
            ehRow.field('net', 'para'),
            ehRow.field('kdv', 'para'),
            ehRow.field('brut', 'para'),
            ehRow.field('kiraAdet', 'tamsayi'),
          ],
        }),
      ],
    }),
    defineView(`${R}/ek-hizmet/arac-pivot`, {
      kod: 'arac',
      baslik: 'rapor.gorunum.aracPivot',
      filtreler: [{ tur: 'donem' }],
      kartlar: [pv.field('genelToplam', 'para')],
      bolumler: [
        section({
          kod: 'pivot',
          baslik: 'rapor.bolum.aracPivot',
          satirlar: (s) => s.satirlar,
          sutunlar: (s) => pivotColumns(s.kolonlar),
          toplam: (s) => ({
            plaka: null,
            ...Object.fromEntries(s.kolonToplam.map((v, i) => [`k${i}`, v])),
            toplam: s.genelToplam,
          }),
        }),
      ],
    }),
    defineView(`${R}/ek-hizmet/detay`, {
      kod: 'detay',
      baslik: 'rapor.gorunum.detay',
      filtreler: [
        { tur: 'donem' },
        { tur: 'metin', ad: 'ara', baslik: 'rapor.alan.ara' },
        { tur: 'arama', ad: 'personelId', baslik: 'rapor.alan.satanPersonel', kaynak: 'personel' },
        { tur: 'metin', ad: 'ofis', baslik: 'rapor.alan.ofis' },
        { tur: 'metin', ad: 'kaynak', baslik: 'rapor.alan.rezKaynagi' },
        { tur: 'bayrak', ad: 'sistemGizle', baslik: 'rapor.alan.sistemGizle' },
      ],
      kartlar: [
        dt.field('adet', 'tamsayi'),
        dt.field('net', 'para'),
        dt.field('kdv', 'para'),
        dt.field('brut', 'para'),
      ],
      satirlar: {
        siralanabilir: ['eklenmeTarihi', 'sozlesmeNo', 'ad', 'plaka', 'brut'],
        satirKimligi: (r) => r.addOnId,
        sutunlar: [
          dtRow.field('eklenmeTarihi', 'tarihSaat', { sirala: true }),
          dtRow.field('sozlesmeNo', 'metin', {
            sirala: true,
            sabit: true,
            bag: (r) => ['/kiralar', r.rentalId],
          }),
          dtRow.field('basTar', 'tarih'),
          dtRow.field('bitTar', 'tarih'),
          dtRow.field('plaka', 'metin', { sirala: true }),
          dtRow.field('musteriAd', 'metin', { baslik: 'rapor.alan.musteri' }),
          dtRow.field('rezKaynagi', 'metin', { gizli: true }),
          dtRow.field('cikisOfisi', 'metin', { gizli: true }),
          dtRow.field('ad', 'metin', { sirala: true, baslik: 'rapor.alan.hizmet' }),
          dtRow.field('miktar', 'sayi'),
          dtRow.field('birimNetFiyat', 'para'),
          dtRow.computed('kdvOrani', 'yuzde', (r) => fractionToPercent(r.kdvOrani), {
            baslik: 'rapor.alan.kdvOrani',
          }),
          dtRow.field('net', 'para'),
          dtRow.field('kdv', 'para'),
          dtRow.field('brut', 'para', { sirala: true }),
          dtRow.field('satanPersonel', 'metin'),
          dtRow.field('ilkTahsilat', 'para', { gizli: true }),
          dtRow.field('sistemKalemi', 'bayrak', { gizli: true }),
        ],
      },
    }),
  ],
});

// ── Günlük faaliyet ─────────────────────────────────────────────────────────────────────────
const gf = cardsFor<SummaryOf<`${typeof R}/gunluk`>>();
export const DAILY_ACTIVITY = defineReport({
  kod: 'gunluk',
  baslik: 'rapor.baslik.gunluk',
  aciklama: 'rapor.aciklama.gunluk',
  grup: 'satis',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/gunluk`, {
      filtreler: [
        { tur: 'gun', ad: 'gun', baslik: 'rapor.alan.gun', ipucu: 'rapor.ipucu.bugun' },
        { tur: 'sube', ad: 'sube', baslik: 'rapor.alan.sube' },
      ],
      kartlar: [
        gf.field('yeniRezervasyon', 'tamsayi'),
        gf.field('yeniKira', 'tamsayi'),
        gf.field('cikis', 'tamsayi'),
        gf.field('donus', 'tamsayi'),
        gf.field('tahsilatAdet', 'tamsayi'),
        gf.field('tahsilatTutar', 'para'),
        gf.field('faturaAdet', 'tamsayi'),
        gf.field('faturaTutar', 'para'),
      ],
    }),
  ],
});

export const SALES_REPORTS: readonly ReportDefinition[] = [
  PROFITABILITY,
  ADDON_SALES,
  DAILY_ACTIVITY,
];
