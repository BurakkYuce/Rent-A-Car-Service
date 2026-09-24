import type { Sema } from '@core/api/ui-tipleri';

import {
  VIEW_REPORTS,
  type SummaryOf,
  cardsFor,
  columnsFor,
  defineReport,
  defineView,
  fractionToPercent,
  section,
} from './report-model';

/** F10.2 — filo raporları (uç `ReportApi.Fleet`): araç karnesi, filo analiz, filo durumu, doluluk. Firma geneli. */
const R = '/api/ui/v1/raporlar';

// ── Araç karnesi ────────────────────────────────────────────────────────────────────────────
const kr = cardsFor<SummaryOf<`${typeof R}/arac-karne/{id}`>>();
const yil = columnsFor<Sema<'AracYilPnlRow'>>();
const kir = columnsFor<Sema<'AracKirilimRow'>>();
const olay = columnsFor<Sema<'AracOlayRow'>>();
const vade = columnsFor<Sema<'ScorecardDue'>>();
const kalem = columnsFor<Sema<'ScorecardCostItem'>>();
const kirilimSutunlari = [
  kir.field('kategori', 'metin'),
  kir.field('tutar', 'para'),
  kir.field('yuzdeGelir', 'yuzde'),
];
export const SCORECARD = defineReport({
  kod: 'arac-karne',
  baslik: 'rapor.baslik.aracKarne',
  aciklama: 'rapor.aciklama.aracKarne',
  grup: 'filo',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  kimlikli: true,
  gorunumler: [
    defineView(`${R}/arac-karne/{id}`, {
      filtreler: [{ tur: 'donem', ipucu: 'rapor.ipucu.karneDonem' }],
      kartlar: [
        kr.computed('rapor.alan.plaka', 'metin', (s) => s.baslik.plaka),
        kr.computed(
          'rapor.alan.aracTipi',
          'metin',
          (s) => [s.baslik.marka, s.baslik.tip].filter(Boolean).join(' ') || null,
        ),
        kr.computed('rapor.alan.grup', 'metin', (s) => s.baslik.grup),
        kr.computed('rapor.alan.sube', 'metin', (s) => s.baslik.sube),
        kr.computed('rapor.alan.durum', 'metin', (s) => s.baslik.durum),
        kr.computed('rapor.alan.km', 'tamsayi', (s) => s.baslik.km),
        kr.computed('rapor.alan.alimBedeli', 'para', (s) => s.baslik.alimBedeli),
        kr.field('toplamGelir', 'para'),
        kr.field('toplamGider', 'para'),
        kr.field('toplamNetKar', 'para', { isaretli: true }),
        kr.computed('rapor.alan.dolulukYuzde', 'yuzde', (s) => s.kpi.dolulukYuzde),
        kr.computed('rapor.alan.revPacd', 'para', (s) => s.kpi.revPacd),
        kr.computed('rapor.alan.adr', 'para', (s) => s.kpi.adr),
        kr.computed('rapor.alan.kmBasinaMaliyet', 'para', (s) => s.kpi.kmBasinaMaliyet),
        kr.computed('rapor.alan.netMarjYuzde', 'yuzde', (s) => s.kpi.netMarjYuzde),
        kr.computed('rapor.alan.roiYuzde', 'yuzde', (s) => s.kpi.roiYuzde),
        kr.computed('rapor.alan.geriOdemeAy', 'tamsayi', (s) => s.kpi.geriOdemeAy),
        kr.computed('rapor.alan.tco', 'para', (s) => s.kpi.tco),
        kr.computed('rapor.alan.gerceklesenAmortisman', 'para', (s) => s.kpi.gerceklesenAmortisman),
        kr.computed('rapor.alan.ekonomikKar', 'para', (s) => s.kpi.ekonomikKar, true),
        kr.computed('rapor.alan.kiraAdet', 'tamsayi', (s) => s.kpi.kiraSayisi),
        kr.computed('rapor.alan.toplamKatedilenKm', 'tamsayi', (s) => s.kpi.toplamKatedilenKm),
        kr.field('basaBasGunluk', 'para'),
        kr.field('donemKm', 'tamsayi'),
        kr.field('donemKmMaliyet', 'para'),
        kr.computed('rapor.alan.tutSatSinyal', 'tamsayi', (s) => s.tutSat.sinyal),
        kr.computed('rapor.alan.kalintiOran', 'yuzde', (s) =>
          s.kalinti ? fractionToPercent(s.kalinti.yillikOran) : null,
        ),
        kr.computed('rapor.alan.deger12Ay', 'para', (s) => s.kalinti?.deger12Ay),
        kr.computed('rapor.alan.deger24Ay', 'para', (s) => s.kalinti?.deger24Ay),
        kr.computed('rapor.alan.kalanKm', 'tamsayi', (s) => s.bakimKm?.kalanKm),
      ],
      bolumler: [
        section({
          kod: 'yillik',
          baslik: 'rapor.bolum.yillikPnl',
          satirlar: (s) => s.yillikPnl,
          sutunlar: [
            yil.field('yil', 'metin'),
            yil.field('gelir', 'para'),
            yil.field('gider', 'para'),
            yil.field('netKar', 'para'),
          ],
        }),
        section({
          kod: 'gelir',
          baslik: 'rapor.bolum.gelirKirilim',
          satirlar: (s) => s.gelirKaynak,
          sutunlar: kirilimSutunlari,
        }),
        section({
          kod: 'gider',
          baslik: 'rapor.bolum.giderKirilim',
          satirlar: (s) => s.giderKategori,
          sutunlar: kirilimSutunlari,
        }),
        section({
          kod: 'olaylar',
          baslik: 'rapor.bolum.olaylar',
          satirlar: (s) => s.olaylar,
          sutunlar: [
            olay.field('tarih', 'tarih'),
            olay.field('tur', 'metin'),
            olay.field('aciklama', 'metin'),
            olay.field('tutar', 'para'),
            olay.field('deftereYansir', 'bayrak'),
          ],
        }),
        section({
          kod: 'vadeler',
          baslik: 'rapor.bolum.vadeler',
          satirlar: (s) => s.vadeler,
          sutunlar: [
            vade.field('tur', 'metin'),
            vade.field('bitis', 'tarih'),
            vade.field('kalanGun', 'tamsayi'),
            vade.field('kova', 'metin'),
          ],
        }),
        section({
          kod: 'maliyet',
          baslik: 'rapor.bolum.maliyetModeli',
          satirlar: (s) => s.maliyetModel?.kalemler,
          sutunlar: [
            kalem.field('ad', 'metin', { baslik: 'rapor.alan.kalem' }),
            kalem.field('periyot', 'metin'),
            kalem.field('birim', 'para'),
            kalem.field('donemTutar', 'para'),
          ],
        }),
        section({
          kod: 'tutsat',
          baslik: 'rapor.bolum.tutSat',
          satirlar: (s) => s.tutSat.gerekceler.map((gerekce) => ({ gerekce })),
          sutunlar: [columnsFor<{ gerekce: string }>().field('gerekce', 'metin')],
        }),
      ],
    }),
  ],
});
