import type { Sema } from '@core/api/ui-tipleri';

import { scorecardLink } from './catalog-sales';
import {
  VIEW_REPORTS,
  type RowOf,
  type SummaryOf,
  cardsFor,
  columnsFor,
  defineReport,
  defineView,
  section,
} from './report-model';

/** F10.2 — filo analiz, filo durumu, doluluk (uç `ReportApi.Fleet`). Firma geneli + ViewReports. */
const R = '/api/ui/v1/raporlar';

// ── Filo analiz ─────────────────────────────────────────────────────────────────────────────
const fa = cardsFor<SummaryOf<`${typeof R}/filo-analiz`>>();
const faSatir = columnsFor<RowOf<`${typeof R}/filo-analiz`>>();
const kohort = columnsFor<Sema<'FiloKohortRow'>>();
export const FLEET_ANALYSIS = defineReport({
  kod: 'filo-analiz',
  baslik: 'rapor.baslik.filoAnaliz',
  aciklama: 'rapor.aciklama.filoAnaliz',
  grup: 'filo',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/filo-analiz`, {
      filtreler: [
        { tur: 'donem' },
        {
          tur: 'secim',
          ad: 'siralama',
          baslik: 'rapor.alan.siralama',
          bosEtiket: 'rapor.secenek.varsayilan',
          secenekler: [
            { deger: 'net', etiket: 'rapor.secenek.enKarli' },
            { deger: 'zarar', etiket: 'rapor.secenek.enZararli' },
            { deger: 'doluluk', etiket: 'rapor.secenek.enDolu' },
            { deger: 'roi', etiket: 'rapor.secenek.enYuksekRoi' },
          ],
        },
      ],
      kartlar: [
        fa.field('toplamGelir', 'para'),
        fa.field('toplamGider', 'para'),
        fa.field('toplamNetKar', 'para', { isaretli: true }),
        fa.field('atanmamisGelir', 'para'),
        fa.field('atanmamisGider', 'para'),
        fa.computed('rapor.alan.dolulukYuzde', 'yuzde', (s) => s.havuzKpi?.dolulukYuzde),
        fa.computed('rapor.alan.revPacd', 'para', (s) => s.havuzKpi?.revPacd),
        fa.computed('rapor.alan.adr', 'para', (s) => s.havuzKpi?.adr),
        fa.computed('rapor.alan.omurGelir', 'para', (s) => s.havuzKpi?.omurGelir),
        fa.computed('rapor.alan.tutSatAday', 'tamsayi', (s) => s.tutSatAday?.aracSayisi),
        fa.computed(
          'rapor.alan.tahminiGeriKazanim',
          'para',
          (s) => s.tutSatAday?.tahminiGeriKazanim12Ay,
        ),
      ],
      bolumler: [
        section({
          kod: 'kohort',
          baslik: 'rapor.bolum.yasKohortu',
          satirlar: (s) => s.yasKohortu,
          sutunlar: [
            kohort.field('kova', 'metin'),
            kohort.field('aracAdet', 'tamsayi'),
            kohort.field('ortKmMaliyet', 'para'),
            kohort.field('ortDoluluk', 'yuzde'),
          ],
        }),
      ],
      satirlar: {
        siralanabilir: ['plaka', 'gelir', 'gider', 'netKar', 'dolulukYuzde', 'roiYuzde', 'yasAy'],
        satirKimligi: (r) => r.vehicleId,
        sutunlar: [
          faSatir.field('plaka', 'metin', {
            sirala: true,
            sabit: true,
            bag: (r) => scorecardLink(r.vehicleId),
          }),
          faSatir.field('grup', 'metin'),
          faSatir.field('segment', 'metin', { gizli: true }),
          faSatir.field('sube', 'metin'),
          faSatir.field('gelir', 'para', { sirala: true }),
          faSatir.field('gider', 'para', { sirala: true }),
          faSatir.field('netKar', 'para', { sirala: true }),
          faSatir.field('dolulukYuzde', 'yuzde', { sirala: true }),
          faSatir.field('roiYuzde', 'yuzde', { sirala: true }),
          faSatir.field('kmBasinaMaliyet', 'para'),
          faSatir.field('sahiplikGun', 'tamsayi', { gizli: true }),
          faSatir.field('kiralananGun', 'tamsayi', { gizli: true }),
          faSatir.field('yasAy', 'tamsayi', { sirala: true }),
          faSatir.field('tutSatSinyal', 'tamsayi'),
          faSatir.field('sinifEndeks', 'sayi', { gizli: true }),
        ],
      },
    }),
  ],
});

// ── Filo durumu (şube bazlı) ───────────────────────────────────────────────────────────────
const fs = cardsFor<SummaryOf<`${typeof R}/filo`>>();
const sube = columnsFor<Sema<'FiloSubeRow'>>();
export const FLEET_STATUS = defineReport({
  kod: 'filo',
  baslik: 'rapor.baslik.filo',
  aciklama: 'rapor.aciklama.filo',
  grup: 'filo',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/filo`, {
      kartlar: [
        fs.computed('rapor.alan.toplamArac', 'tamsayi', (s) => s.durum.toplam),
        fs.computed('rapor.alan.musait', 'tamsayi', (s) => s.durum.musait),
        fs.computed('rapor.alan.kirada', 'tamsayi', (s) => s.durum.kirada),
        fs.computed('rapor.alan.serviste', 'tamsayi', (s) => s.durum.serviste),
        fs.computed('rapor.alan.pasif', 'tamsayi', (s) => s.durum.pasif),
        fs.computed('rapor.alan.satildi', 'tamsayi', (s) => s.durum.satildi),
        fs.computed('rapor.alan.aktifKira', 'tamsayi', (s) => s.durum.aktifKira),
      ],
      bolumler: [
        section({
          kod: 'subeler',
          baslik: 'rapor.bolum.subeBazli',
          satirlar: (s) => s.subeler.satirlar,
          sutunlar: [
            sube.field('sube', 'metin'),
            sube.field('filo', 'tamsayi'),
            sube.field('bos', 'tamsayi'),
            sube.field('kirada', 'tamsayi'),
            sube.field('bakimda', 'tamsayi'),
            sube.field('pasif', 'tamsayi'),
            sube.field('satildi', 'tamsayi'),
            sube.field('satilik', 'tamsayi'),
            sube.field('baf', 'tamsayi'),
            sube.field('dolulukYuzde', 'yuzde'),
            sube.field('cikislar', 'tamsayi'),
            sube.field('donusler', 'tamsayi'),
            sube.field('cikacaklar', 'tamsayi'),
            sube.field('donecekler', 'tamsayi'),
            sube.field('gidenRez', 'tamsayi'),
          ],
        }),
      ],
    }),
  ],
});
