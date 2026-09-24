import type { Sema } from '@core/api/ui-tipleri';

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
const kaSatir = columnsFor<RowOf<`${typeof R}/karlilik`>>();
const ko = cardsFor<SummaryOf<`${typeof R}/karlilik/ozet`>>();
const koSatir = columnsFor<Sema<'KarlilikOzetSatirDto'>>();
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
          kaSatir.field('plaka', 'metin', {
            sirala: true,
            sabit: true,
            bag: (r) => scorecardLink(r.vehicleId),
          }),
          kaSatir.field('sube', 'metin', { sirala: true }),
          kaSatir.field('grup', 'metin', { sirala: true }),
          kaSatir.field('segment', 'metin', { gizli: true }),
          kaSatir.field('sipp', 'metin', { gizli: true }),
          kaSatir.field('gelir', 'para', { sirala: true }),
          kaSatir.field('gider', 'para', { sirala: true }),
          kaSatir.field('netKar', 'para', { sirala: true }),
          kaSatir.field('dolulukYuzde', 'yuzde', { sirala: true }),
          kaSatir.field('revPacd', 'para'),
          kaSatir.field('adr', 'para'),
          kaSatir.field('kiraAdet', 'tamsayi'),
          kaSatir.field('kiralananGun', 'tamsayi'),
          kaSatir.field('sahiplikGun', 'tamsayi', { gizli: true }),
          kaSatir.field('potansiyelGelir', 'para', { gizli: true }),
          kaSatir.field('referansAylikMaliyet', 'para', { gizli: true }),
          kaSatir.field('referansFiloYonetimMaliyeti', 'para', { gizli: true }),
          kaSatir.field('referansToplamMaliyet', 'para', { gizli: true }),
          kaSatir.field('hesaplananKdv', 'para', { gizli: true }),
          kaSatir.field('gelirKdvDahil', 'para', { gizli: true }),
          kaSatir.field('otopark', 'metin', { gizli: true }),
          kaSatir.field('rezKaynagi', 'metin', { gizli: true }),
          kaSatir.field('cariAd', 'metin', { gizli: true, baslik: 'rapor.alan.cari' }),
          kaSatir.field('cariBakiye', 'para', { gizli: true }),
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
            koSatir.field('boyut', 'metin', { baslik: 'rapor.alan.kirilim' }),
            koSatir.field('aracAdet', 'tamsayi'),
            koSatir.field('gelir', 'para'),
            koSatir.field('gider', 'para'),
            koSatir.field('netKar', 'para'),
            koSatir.field('aracBasiGelir', 'para'),
            koSatir.field('dolulukYuzde', 'yuzde'),
            koSatir.field('potansiyelGelir', 'para'),
            koSatir.field('referansToplamMaliyet', 'para'),
          ],
        }),
      ],
    }),
  ],
});

// ── Ek hizmet (özet + araç pivotu + satır detayı) ───────────────────────────────────────────
const eh = cardsFor<SummaryOf<`${typeof R}/ek-hizmet`>>();
const ehSatir = columnsFor<Sema<'EkHizmetRaporRowDto'>>();
const pv = cardsFor<SummaryOf<`${typeof R}/ek-hizmet/arac-pivot`>>();
type PivotRow = Sema<'EkHizmetAracPivotSatir'>;
const pvSatir = columnsFor<PivotRow>();
const dt = cardsFor<SummaryOf<`${typeof R}/ek-hizmet/detay`>>();
const dtSatir = columnsFor<RowOf<`${typeof R}/ek-hizmet/detay`>>();

/** Pivot sütunları sunucunun `kolonlar` sırasıyla (hizmet adı başlık; metin sözlükte değil — veri). */
function pivotColumns(kolonlar: readonly string[]): readonly ReportColumn<PivotRow>[] {
  return [
    pvSatir.field('plaka', 'metin', { sabit: true }),
    pvSatir.field('grup', 'metin'),
    pvSatir.field('sipp', 'metin'),
    ...kolonlar.map((ad, i) =>
      pvSatir.computed(`k${i}`, 'para', (r) => r.hucreler[i], {
        baslik: 'rapor.alan.hizmet',
        baslikMetni: ad,
      }),
    ),
    pvSatir.field('toplam', 'para'),
    pvSatir.field('kalemAdet', 'tamsayi'),
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
            ehSatir.field('ad', 'metin', { baslik: 'rapor.alan.hizmet' }),
            ehSatir.field('toplamMiktar', 'sayi'),
            ehSatir.field('net', 'para'),
            ehSatir.field('kdv', 'para'),
            ehSatir.field('brut', 'para'),
            ehSatir.field('kiraAdet', 'tamsayi'),
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
          dtSatir.field('eklenmeTarihi', 'tarihSaat', { sirala: true }),
          dtSatir.field('sozlesmeNo', 'metin', {
            sirala: true,
            sabit: true,
            bag: (r) => ['/kiralar', r.rentalId],
          }),
          dtSatir.field('basTar', 'tarih'),
          dtSatir.field('bitTar', 'tarih'),
          dtSatir.field('plaka', 'metin', { sirala: true }),
          dtSatir.field('musteriAd', 'metin', { baslik: 'rapor.alan.musteri' }),
          dtSatir.field('rezKaynagi', 'metin', { gizli: true }),
          dtSatir.field('cikisOfisi', 'metin', { gizli: true }),
          dtSatir.field('ad', 'metin', { sirala: true, baslik: 'rapor.alan.hizmet' }),
          dtSatir.field('miktar', 'sayi'),
          dtSatir.field('birimNetFiyat', 'para'),
          dtSatir.computed('kdvOrani', 'yuzde', (r) => fractionToPercent(r.kdvOrani), {
            baslik: 'rapor.alan.kdvOrani',
          }),
          dtSatir.field('net', 'para'),
          dtSatir.field('kdv', 'para'),
          dtSatir.field('brut', 'para', { sirala: true }),
          dtSatir.field('satanPersonel', 'metin'),
          dtSatir.field('ilkTahsilat', 'para', { gizli: true }),
          dtSatir.field('sistemKalemi', 'bayrak', { gizli: true }),
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
