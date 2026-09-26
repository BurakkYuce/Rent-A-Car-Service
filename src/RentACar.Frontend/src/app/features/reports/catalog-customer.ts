import {
  VIEW_REPORTS,
  type ReportDefinition,
  type RowOf,
  type SummaryOf,
  cardsFor,
  columnsFor,
  defineReport,
  defineView,
} from './report-model';

/**
 * F10.2 — cari ve fatura raporları (uç `ReportApi.Customer`/`Invoice`). Müşteri adı sunucudan KVKK kuralıyla
 * (`CustomerMask`) gelir; SPA maskelemez, saklamaz. Hepsi firma geneli + ViewReports.
 */

const R = '/api/ui/v1/raporlar';

const RENTAL_STATUSES = [
  { deger: 'Kirada', etiket: 'rapor.secenek.kirada' },
  { deger: 'Tamamlandi', etiket: 'rapor.secenek.tamamlandi' },
  { deger: 'Iptal', etiket: 'rapor.secenek.iptal' },
] as const;

const rentalLink = (id: string) => ['/kiralar', id];

// ── Cari bakiye (+ yaşlandırma) ─────────────────────────────────────────────────────────────
const cb = cardsFor<SummaryOf<`${typeof R}/cari-bakiye`>>();
const cbRow = columnsFor<RowOf<`${typeof R}/cari-bakiye`>>();
const ya = cardsFor<SummaryOf<`${typeof R}/cari-bakiye/yaslandirma`>>();
const yaRow = columnsFor<RowOf<`${typeof R}/cari-bakiye/yaslandirma`>>();
export const CUSTOMER_BALANCE = defineReport({
  kod: 'cari-bakiye',
  baslik: 'rapor.baslik.cariBakiye',
  aciklama: 'rapor.aciklama.cariBakiye',
  grup: 'cari',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/cari-bakiye`, {
      kod: 'bakiye',
      baslik: 'rapor.gorunum.bakiye',
      filtreler: [
        { tur: 'metin', ad: 'ara', baslik: 'rapor.alan.ara' },
        {
          tur: 'metin',
          ad: 'ozelKod',
          baslik: 'rapor.alan.ozelKod',
          oneriler: (s) => s.ozelKodlar,
        },
        { tur: 'metin', ad: 'sinif', baslik: 'rapor.alan.sinif', oneriler: (s) => s.siniflar },
        {
          tur: 'metin',
          ad: 'doviz',
          baslik: 'rapor.alan.doviz',
          enFazla: 3,
          oneriler: (s) => s.dovizler,
        },
        {
          tur: 'secim',
          ad: 'tip',
          baslik: 'rapor.alan.tip',
          secenekler: [
            { deger: 'kurumsal', etiket: 'rapor.secenek.kurumsal' },
            { deger: 'bireysel', etiket: 'rapor.secenek.bireysel' },
          ],
        },
        {
          tur: 'secim',
          ad: 'bakiye',
          baslik: 'rapor.alan.bakiyeDurumu',
          secenekler: [
            { deger: 'borclu', etiket: 'rapor.secenek.borclu' },
            { deger: 'alacakli', etiket: 'rapor.secenek.alacakli' },
          ],
        },
        { tur: 'sayi', ad: 'min', baslik: 'rapor.alan.enAzBakiye', enAz: 0 },
      ],
      kartlar: [cb.field('borcluToplam', 'para'), cb.field('alacakliToplam', 'para')],
      satirlar: {
        siralanabilir: ['ad', 'bakiye', 'toplamBorc', 'toplamAlacak', 'doviz', 'sinif'],
        satirKimligi: (r) => r.cariId,
        sutunlar: [
          cbRow.field('ad', 'metin', { sirala: true, sabit: true, baslik: 'rapor.alan.cari' }),
          cbRow.field('bakiye', 'para', { sirala: true }),
          cbRow.field('toplamBorc', 'para', { sirala: true }),
          cbRow.field('toplamAlacak', 'para', { sirala: true }),
          cbRow.field('doviz', 'metin', { sirala: true }),
          cbRow.field('telefon', 'metin'),
          cbRow.field('email', 'metin'),
          cbRow.field('banka', 'metin', { gizli: true }),
          cbRow.field('ozelKod', 'metin'),
          cbRow.field('sinif', 'metin', { sirala: true }),
          cbRow.field('kurumsal', 'bayrak'),
          cbRow.field('pasif', 'bayrak', { gizli: true }),
        ],
      },
    }),
    defineView(`${R}/cari-bakiye/yaslandirma`, {
      kod: 'yaslandirma',
      baslik: 'rapor.gorunum.yaslandirma',
      filtreler: [
        {
          tur: 'gun',
          ad: 'tarih',
          baslik: 'rapor.alan.yaslandirmaTarihi',
          ipucu: 'rapor.ipucu.bugun',
        },
      ],
      kartlar: [
        ya.field('tarih', 'tarih', { baslik: 'rapor.alan.yaslandirmaTarihi' }),
        ya.computed('rapor.alan.b0_30', 'para', (s) => s.kovalar.b0_30),
        ya.computed('rapor.alan.b31_60', 'para', (s) => s.kovalar.b31_60),
        ya.computed('rapor.alan.b61_90', 'para', (s) => s.kovalar.b61_90),
        ya.computed('rapor.alan.b90Plus', 'para', (s) => s.kovalar.b90Plus),
        ya.field('toplam', 'para'),
      ],
      satirlar: {
        siralanabilir: ['ad', 'toplam', 'b0_30', 'b31_60', 'b61_90', 'b90Plus'],
        satirKimligi: (r) => r.cariId,
        sutunlar: [
          yaRow.field('ad', 'metin', { sirala: true, sabit: true, baslik: 'rapor.alan.cari' }),
          yaRow.field('b0_30', 'para', { sirala: true }),
          yaRow.field('b31_60', 'para', { sirala: true }),
          yaRow.field('b61_90', 'para', { sirala: true }),
          yaRow.field('b90Plus', 'para', { sirala: true }),
          yaRow.field('toplam', 'para', { sirala: true }),
        ],
      },
    }),
  ],
});

// ── Ekstre özeti ────────────────────────────────────────────────────────────────────────────
const ex = cardsFor<SummaryOf<`${typeof R}/extre-ozeti`>>();
const exRow = columnsFor<RowOf<`${typeof R}/extre-ozeti`>>();
export const STATEMENT = defineReport({
  kod: 'extre-ozeti',
  baslik: 'rapor.baslik.extreOzeti',
  aciklama: 'rapor.aciklama.extreOzeti',
  grup: 'cari',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/extre-ozeti`, {
      filtreler: [
        { tur: 'donem' },
        { tur: 'arama', ad: 'cariId', baslik: 'rapor.alan.cari', kaynak: 'musteri' },
        { tur: 'metin', ad: 'plaka', baslik: 'rapor.alan.plaka', enFazla: 20 },
        { tur: 'metin', ad: 'ofis', baslik: 'rapor.alan.ofis' },
        { tur: 'bayrak', ad: 'gecikmis', baslik: 'rapor.alan.yalnizGecikmis' },
      ],
      kartlar: [ex.field('toplamTl', 'para'), ex.field('gecikmisAdet', 'tamsayi')],
      satirlar: {
        siralanabilir: ['tarih', 'vadeTarihi', 'faturaNo', 'cariAd', 'tutar'],
        satirKimligi: (r) => r.faturaId,
        sutunlar: [
          exRow.field('faturaNo', 'metin', { sirala: true, sabit: true }),
          exRow.field('tarih', 'tarih', { sirala: true }),
          exRow.field('vadeTarihi', 'tarih', { sirala: true }),
          exRow.field('cariAd', 'metin', { sirala: true, baslik: 'rapor.alan.cari' }),
          exRow.field('plaka', 'metin'),
          exRow.field('sozlesmeNo', 'metin'),
          exRow.field('cikisOfisi', 'metin'),
          exRow.field('tutar', 'para', { sirala: true, paraBirimi: (r) => r.doviz }),
          exRow.field('kur', 'sayi', { gizli: true }),
          exRow.field('iadeMi', 'bayrak'),
          exRow.field('isaretliTutarTl', 'para'),
        ],
      },
    }),
  ],
});

// ── Tahsilat–fatura (+ kira bazlı mutabakat) ────────────────────────────────────────────────
const tf = cardsFor<SummaryOf<`${typeof R}/tahsilat-fatura`>>();
const mu = cardsFor<SummaryOf<`${typeof R}/tahsilat-fatura/mutabakat`>>();
const muRow = columnsFor<RowOf<`${typeof R}/tahsilat-fatura/mutabakat`>>();
export const COLLECTION_INVOICE = defineReport({
  kod: 'tahsilat-fatura',
  baslik: 'rapor.baslik.tahsilatFatura',
  aciklama: 'rapor.aciklama.tahsilatFatura',
  grup: 'cari',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/tahsilat-fatura`, {
      kod: 'ozet',
      baslik: 'rapor.gorunum.ozet',
      filtreler: [{ tur: 'donem' }],
      kartlar: [
        tf.field('faturaAdet', 'tamsayi'),
        tf.field('faturaToplam', 'para'),
        tf.field('tahsilatAdet', 'tamsayi'),
        tf.field('tahsilatToplam', 'para'),
        tf.field('fark', 'para', { isaretli: true }),
      ],
    }),
    defineView(`${R}/tahsilat-fatura/mutabakat`, {
      kod: 'mutabakat',
      baslik: 'rapor.gorunum.mutabakat',
      filtreler: [
        { tur: 'donem', ipucu: 'rapor.ipucu.kiraBaslangici' },
        { tur: 'metin', ad: 'ara', baslik: 'rapor.alan.ara' },
        { tur: 'secim', ad: 'durum', baslik: 'rapor.alan.durum', secenekler: RENTAL_STATUSES },
        {
          tur: 'secim',
          ad: 'bakiye',
          baslik: 'rapor.alan.bakiyeDurumu',
          secenekler: [
            { deger: 'acik', etiket: 'rapor.secenek.acik' },
            { deger: 'kapali', etiket: 'rapor.secenek.kapali' },
          ],
        },
        { tur: 'bayrak', ad: 'tutarsiz', baslik: 'rapor.alan.yalnizTutarsiz' },
      ],
      kartlar: [
        mu.field('genelToplam', 'para'),
        mu.field('tahsilat', 'para'),
        mu.field('faturalanan', 'para'),
        mu.field('tutarsizAdet', 'tamsayi'),
      ],
      satirlar: {
        siralanabilir: [
          'basTar',
          'sozlesmeNo',
          'musteriAd',
          'genelToplam',
          'bakiye',
          'faturaFarki',
        ],
        satirKimligi: (r) => r.rentalId,
        sutunlar: [
          muRow.field('sozlesmeNo', 'metin', {
            sirala: true,
            sabit: true,
            bag: (r) => rentalLink(r.rentalId),
          }),
          muRow.field('plaka', 'metin'),
          muRow.field('musteriAd', 'metin', { sirala: true, baslik: 'rapor.alan.musteri' }),
          muRow.field('basTar', 'tarih', { sirala: true }),
          muRow.field('durum', 'metin'),
          muRow.field('matrah', 'para', { paraBirimi: (r) => r.doviz, gizli: true }),
          muRow.field('damgaVergisi', 'para', { paraBirimi: (r) => r.doviz, gizli: true }),
          muRow.field('genelToplam', 'para', { sirala: true, paraBirimi: (r) => r.doviz }),
          muRow.field('tahsilat', 'para', { paraBirimi: (r) => r.doviz }),
          muRow.field('defterTahsilat', 'para', { gizli: true }),
          muRow.field('faturalanan', 'para', { paraBirimi: (r) => r.doviz }),
          muRow.field('musteriBakiye', 'para', { gizli: true }),
          muRow.field('bakiye', 'para', { sirala: true, paraBirimi: (r) => r.doviz }),
          muRow.field('faturaFarki', 'para', { sirala: true, paraBirimi: (r) => r.doviz }),
          muRow.field('tahsilatAyrimi', 'para', { gizli: true }),
          muRow.field('tutarsiz', 'bayrak'),
        ],
      },
    }),
  ],
});

// ── Fatura dönem (+ kira fatura durumu) ─────────────────────────────────────────────────────
const fd = cardsFor<SummaryOf<`${typeof R}/fatura-donem`>>();
const fdRow = columnsFor<RowOf<`${typeof R}/fatura-donem`>>();
const kd = cardsFor<SummaryOf<`${typeof R}/fatura-donem/kira-durum`>>();
const kdRow = columnsFor<RowOf<`${typeof R}/fatura-donem/kira-durum`>>();
export const INVOICE_PERIOD = defineReport({
  kod: 'fatura-donem',
  baslik: 'rapor.baslik.faturaDonem',
  aciklama: 'rapor.aciklama.faturaDonem',
  grup: 'cari',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/fatura-donem`, {
      kod: 'faturalar',
      baslik: 'rapor.gorunum.faturalar',
      filtreler: [{ tur: 'donem' }],
      kartlar: [fd.field('adet', 'tamsayi'), fd.field('toplamTl', 'para')],
      satirlar: {
        siralanabilir: ['tarih', 'vadeTarihi', 'no', 'cari', 'genelToplam'],
        satirKimligi: (r) => r.invoiceId,
        sutunlar: [
          fdRow.field('no', 'metin', { sirala: true, sabit: true }),
          fdRow.field('tarih', 'tarih', { sirala: true }),
          fdRow.field('vadeTarihi', 'tarih', { sirala: true }),
          fdRow.field('cari', 'metin', { sirala: true }),
          fdRow.field('genelToplam', 'para', { sirala: true, paraBirimi: (r) => r.currency }),
          fdRow.field('kur', 'sayi', { gizli: true }),
          fdRow.field('durum', 'metin'),
          fdRow.field('iadeMi', 'bayrak'),
        ],
      },
    }),
    defineView(`${R}/fatura-donem/kira-durum`, {
      kod: 'kira-durum',
      baslik: 'rapor.gorunum.kiraDurum',
      filtreler: [
        { tur: 'donem' },
        { tur: 'metin', ad: 'ara', baslik: 'rapor.alan.ara' },
        {
          tur: 'secim',
          ad: 'faturaDurum',
          baslik: 'rapor.alan.faturaDurum',
          secenekler: [
            { deger: 'yok', etiket: 'rapor.secenek.faturalanmamis' },
            { deger: 'var', etiket: 'rapor.secenek.faturalanmis' },
          ],
        },
        { tur: 'liste', ad: 'subeId', baslik: 'rapor.alan.sube', kaynak: 'sube' },
      ],
      kartlar: [
        kd.field('adet', 'tamsayi'),
        kd.field('faturalanmamisAdet', 'tamsayi'),
        kd.field('faturalananTutar', 'para'),
      ],
      satirlar: {
        siralanabilir: ['basTar', 'sozlesmeNo', 'cari', 'plaka', 'faturalananTutar'],
        satirKimligi: (r) => r.rentalId,
        sutunlar: [
          kdRow.field('sozlesmeNo', 'metin', {
            sirala: true,
            sabit: true,
            bag: (r) => rentalLink(r.rentalId),
          }),
          kdRow.field('plaka', 'metin', { sirala: true }),
          kdRow.field('cari', 'metin', { sirala: true }),
          kdRow.field('basTar', 'tarih', { sirala: true }),
          kdRow.field('bitTar', 'tarih'),
          kdRow.field('durum', 'metin'),
          kdRow.field('faturalanan', 'bayrak'),
          kdRow.field('faturaAdet', 'tamsayi'),
          kdRow.field('faturalananTutar', 'para', { sirala: true }),
          kdRow.field('ofis', 'metin'),
        ],
      },
    }),
  ],
});

export const CUSTOMER_REPORTS: readonly ReportDefinition[] = [
  CUSTOMER_BALANCE,
  STATEMENT,
  COLLECTION_INVOICE,
  INVOICE_PERIOD,
];
