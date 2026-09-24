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
  objectKey,
  section,
} from './report-model';

/**
 * F10.2 — defter/finans raporları (Blazor `Reports/*` paritesi; uç `ReportApi.Ledger`/`Finance`). Tutarlar yalnız
 * DEFTERDEN (sunucu); SPA toplamaz, formül taşımaz. Hepsi firma geneli + ViewReports.
 */

const R = '/api/ui/v1/raporlar';

type Kalem = Sema<'GelirGiderKalemDto'>;
const kalem = columnsFor<Kalem>();
const kalemSutunlari: readonly ReportColumn<Kalem>[] = [
  kalem.field('sourceType', 'metin', { baslik: 'rapor.alan.kaynak' }),
  kalem.field('tutar', 'para'),
];

// ── Gelir-Gider ─────────────────────────────────────────────────────────────────────────────
const gg = cardsFor<SummaryOf<`${typeof R}/gelir-gider`>>();
export const INCOME_EXPENSE = defineReport({
  kod: 'gelir-gider',
  baslik: 'rapor.baslik.gelirGider',
  aciklama: 'rapor.aciklama.gelirGider',
  grup: 'finans',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/gelir-gider`, {
      filtreler: [{ tur: 'donem' }],
      kartlar: [
        gg.field('gelirToplam', 'para'),
        gg.field('giderToplam', 'para'),
        gg.field('kdvTahsil', 'para'),
        gg.field('kdvIndirilecek', 'para'),
        gg.field('netKar', 'para', { isaretli: true }),
      ],
      bolumler: [
        section({
          kod: 'gelir',
          baslik: 'rapor.bolum.gelirKirilim',
          satirlar: (s) => s.gelirKirilim,
          sutunlar: kalemSutunlari,
        }),
        section({
          kod: 'gider',
          baslik: 'rapor.bolum.giderKirilim',
          satirlar: (s) => s.giderKirilim,
          sutunlar: kalemSutunlari,
        }),
      ],
    }),
  ],
});

// ── Kasa / Banka defteri ────────────────────────────────────────────────────────────────────
const kb = cardsFor<SummaryOf<`${typeof R}/kasa-banka`>>();
const kbSatir = columnsFor<RowOf<`${typeof R}/kasa-banka`>>();
const hesapOzet = columnsFor<Sema<'ReportAccountSummaryRow'>>();
export const CASH_BANK = defineReport({
  kod: 'kasa-banka',
  baslik: 'rapor.baslik.kasaBanka',
  aciklama: 'rapor.aciklama.kasaBanka',
  grup: 'finans',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/kasa-banka`, {
      filtreler: [
        { tur: 'donem' },
        {
          tur: 'secim',
          ad: 'hesap',
          baslik: 'rapor.alan.hesapTuru',
          bosEtiket: 'rapor.secenek.kasaVarsayilan',
          secenekler: [
            { deger: 'Kasa', etiket: 'rapor.secenek.kasa' },
            { deger: 'Banka', etiket: 'rapor.secenek.banka' },
          ],
        },
        { tur: 'liste', ad: 'hesapId', baslik: 'rapor.alan.hesap', kaynak: 'finansHesap' },
        {
          tur: 'metin',
          ad: 'doviz',
          baslik: 'rapor.alan.doviz',
          enFazla: 3,
          oneriler: (s) => s.secenekler.dovizler,
        },
        {
          tur: 'metin',
          ad: 'tur',
          baslik: 'rapor.alan.islemTuru',
          oneriler: (s) => s.secenekler.turler,
        },
        {
          tur: 'metin',
          ad: 'sube',
          baslik: 'rapor.alan.sube',
          oneriler: (s) => s.secenekler.subeler,
        },
        { tur: 'bayrak', ad: 'devir', baslik: 'rapor.alan.devir' },
      ],
      kartlar: [
        kb.computed('rapor.alan.kasaGiris', 'para', (s) => s.toplam.kasaGiris),
        kb.computed('rapor.alan.kasaCikis', 'para', (s) => s.toplam.kasaCikis),
        kb.computed('rapor.alan.kasaBakiye', 'para', (s) => s.toplam.kasaBakiye, true),
        kb.computed('rapor.alan.bankaGiris', 'para', (s) => s.toplam.bankaGiris),
        kb.computed('rapor.alan.bankaCikis', 'para', (s) => s.toplam.bankaCikis),
        kb.computed('rapor.alan.bankaBakiye', 'para', (s) => s.toplam.bankaBakiye, true),
      ],
      bolumler: [
        section({
          kod: 'hesaplar',
          baslik: 'rapor.bolum.hesapBazli',
          satirlar: (s) => s.hesaplar,
          sutunlar: [
            hesapOzet.field('tur', 'metin', { baslik: 'rapor.alan.hesapTuru' }),
            hesapOzet.field('hesapAd', 'metin', { baslik: 'rapor.alan.hesap' }),
            hesapOzet.field('giris', 'para'),
            hesapOzet.field('cikis', 'para'),
            hesapOzet.field('bakiye', 'para'),
          ],
        }),
      ],
      satirlar: {
        siralanabilir: [],
        satirKimligi: objectKey,
        sutunlar: [
          kbSatir.field('tarih', 'tarihSaat', { sabit: true }),
          kbSatir.field('sourceType', 'metin', { baslik: 'rapor.alan.islemTuru' }),
          kbSatir.field('aciklama', 'metin'),
          kbSatir.field('cariAd', 'metin', { baslik: 'rapor.alan.cari' }),
          kbSatir.field('belgeNo', 'metin'),
          kbSatir.field('sube', 'metin'),
          kbSatir.field('kanal', 'metin', { gizli: true }),
          kbSatir.field('native', 'para', {
            baslik: 'rapor.alan.dovizTutar',
            paraBirimi: (r) => r.doviz,
          }),
          kbSatir.field('borc', 'para'),
          kbSatir.field('alacak', 'para'),
          kbSatir.field('yuruyenBakiye', 'para'),
          kbSatir.field('devirMi', 'bayrak', { gizli: true }),
        ],
      },
    }),
  ],
});

// ── Finans analiz panosu ────────────────────────────────────────────────────────────────────
const fa = cardsFor<SummaryOf<`${typeof R}/finans-analiz`>>();
const trend = columnsFor<Sema<'AylikGelirGiderNokta'>>();
const son30 = columnsFor<Sema<'AracDurumTakipRow'>>();
export const FINANCE_ANALYSIS = defineReport({
  kod: 'finans-analiz',
  baslik: 'rapor.baslik.finansAnaliz',
  aciklama: 'rapor.aciklama.finansAnaliz',
  grup: 'finans',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/finans-analiz`, {
      filtreler: [{ tur: 'donem' }],
      kartlar: [
        fa.computed('rapor.alan.faturaToplam', 'para', (s) => s.mutabakat.faturaToplam),
        fa.computed('rapor.alan.tahsilatToplam', 'para', (s) => s.mutabakat.tahsilatToplam),
        fa.computed('rapor.alan.fark', 'para', (s) => s.mutabakat.fark, true),
        fa.computed('rapor.alan.b0_30', 'para', (s) => s.yaslandirma.b0_30),
        fa.computed('rapor.alan.b31_60', 'para', (s) => s.yaslandirma.b31_60),
        fa.computed('rapor.alan.b61_90', 'para', (s) => s.yaslandirma.b61_90),
        fa.computed('rapor.alan.b90Plus', 'para', (s) => s.yaslandirma.b90Plus),
      ],
      bolumler: [
        section({
          kod: 'trend',
          baslik: 'rapor.bolum.aylikTrend',
          satirlar: (s) => s.trend,
          sutunlar: [
            trend.field('ayBas', 'tarih', { baslik: 'rapor.alan.ay' }),
            trend.field('gelir', 'para'),
            trend.field('gider', 'para'),
            trend.field('netKar', 'para'),
          ],
        }),
        section({
          kod: 'gelir',
          baslik: 'rapor.bolum.gelirKirilim',
          satirlar: (s) => s.gelirKirilim,
          sutunlar: kalemSutunlari,
        }),
        section({
          kod: 'gider',
          baslik: 'rapor.bolum.giderKirilim',
          satirlar: (s) => s.giderKirilim,
          sutunlar: kalemSutunlari,
        }),
        section({
          kod: 'son30',
          baslik: 'rapor.bolum.son30Gun',
          satirlar: (s) => s.son30Gun,
          sutunlar: [
            son30.field('gun', 'tarih'),
            son30.field('toplamArac', 'tamsayi'),
            son30.field('dolu', 'tamsayi'),
            son30.field('bakim', 'tamsayi'),
            son30.field('bos', 'tamsayi'),
            son30.field('toplamBaf', 'tamsayi'),
            // Blazor'daki gibi yüzde SPA'da: Dolu ÷ ToplamArac (uç yorumu).
            son30.computed(
              'doluluk',
              'yuzde',
              (r) =>
                Number(r.toplamArac) > 0 ? (Number(r.dolu) / Number(r.toplamArac)) * 100 : null,
              { baslik: 'rapor.alan.dolulukYuzde' },
            ),
          ],
        }),
      ],
    }),
  ],
});

// ── Virman geçmişi ──────────────────────────────────────────────────────────────────────────
const vg = columnsFor<RowOf<`${typeof R}/virman-gecmisi`>>();
const dovizToplam = columnsFor<Sema<'ReportCurrencyTotal'>>();
export const TRANSFER_HISTORY = defineReport({
  kod: 'virman-gecmisi',
  baslik: 'rapor.baslik.virmanGecmisi',
  aciklama: 'rapor.aciklama.virmanGecmisi',
  grup: 'finans',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/virman-gecmisi`, {
      filtreler: [
        { tur: 'donem' },
        { tur: 'liste', ad: 'hesapId', baslik: 'rapor.alan.hesap', kaynak: 'finansHesap' },
        { tur: 'metin', ad: 'ara', baslik: 'rapor.alan.ara' },
      ],
      bolumler: [
        section({
          kod: 'doviz',
          baslik: 'rapor.bolum.dovizToplam',
          satirlar: (s) => s,
          sutunlar: [
            dovizToplam.field('doviz', 'metin'),
            dovizToplam.field('toplam', 'para', { paraBirimi: (r) => r.doviz }),
          ],
        }),
      ],
      satirlar: {
        siralanabilir: ['tarih', 'tutar', 'tutarTl', 'doviz', 'sube'],
        satirKimligi: (r) => r.id,
        sutunlar: [
          vg.field('tarih', 'tarihSaat', { sirala: true, sabit: true }),
          vg.field('kaynakTur', 'metin'),
          vg.field('kaynakHesapAd', 'metin'),
          vg.field('hedefTur', 'metin'),
          vg.field('hedefHesapAd', 'metin'),
          vg.field('tutar', 'para', { sirala: true, paraBirimi: (r) => r.doviz }),
          vg.field('doviz', 'metin', { sirala: true }),
          vg.field('kur', 'sayi'),
          vg.field('tutarTl', 'para', { sirala: true }),
          vg.field('makbuzNo', 'metin'),
          vg.field('sube', 'metin', { sirala: true }),
          vg.field('islemYapan', 'metin'),
          vg.field('aciklama', 'metin'),
        ],
      },
    }),
  ],
});

// ── KDV listesi (oran özeti + belge bazlı geniş) ────────────────────────────────────────────
const kdv = cardsFor<SummaryOf<`${typeof R}/kdv-listesi`>>();
const kdvOran = columnsFor<Sema<'KdvListesiRowDto'>>();
const kdvg = cardsFor<SummaryOf<`${typeof R}/kdv-listesi/genis`>>();
const kdvgSatir = columnsFor<RowOf<`${typeof R}/kdv-listesi/genis`>>();
export const VAT_LIST = defineReport({
  kod: 'kdv-listesi',
  baslik: 'rapor.baslik.kdvListesi',
  aciklama: 'rapor.aciklama.kdvListesi',
  grup: 'finans',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/kdv-listesi`, {
      kod: 'oran',
      baslik: 'rapor.gorunum.oranOzeti',
      filtreler: [{ tur: 'donem' }],
      kartlar: [
        kdv.field('toplamNet', 'para'),
        kdv.field('toplamKdv', 'para'),
        kdv.field('toplamBrut', 'para'),
        kdv.field('faturaAdet', 'tamsayi'),
      ],
      bolumler: [
        section({
          kod: 'oranlar',
          baslik: 'rapor.bolum.oranlar',
          satirlar: (s) => s.satirlar,
          sutunlar: [
            kdvOran.computed('oran', 'yuzde', (r) => fractionToPercent(r.oran), {
              baslik: 'rapor.alan.kdvOrani',
            }),
            kdvOran.field('net', 'para'),
            kdvOran.field('kdv', 'para'),
            kdvOran.field('brut', 'para'),
            kdvOran.field('faturaAdet', 'tamsayi'),
          ],
        }),
      ],
    }),
    defineView(`${R}/kdv-listesi/genis`, {
      kod: 'genis',
      baslik: 'rapor.gorunum.belgeBazli',
      filtreler: [{ tur: 'donem' }, { tur: 'bayrak', ad: 'alis', baslik: 'rapor.alan.alisDahil' }],
      kartlar: [
        kdvg.field('satisNet', 'para'),
        kdvg.field('satisKdv', 'para'),
        kdvg.field('alisNet', 'para'),
        kdvg.field('alisKdv', 'para'),
        kdvg.field('netKdv', 'para', { isaretli: true }),
        kdvg.field('satisBelgeAdet', 'tamsayi'),
        kdvg.field('alisBelgeAdet', 'tamsayi'),
      ],
      uyarilar: [
        {
          metin: 'rapor.uyari.atlananDovizliAlis',
          goster: (s) => Number(s.atlananDovizliAlis) > 0,
          parametreler: (s) => ({ adet: s.atlananDovizliAlis }),
        },
      ],
      satirlar: {
        siralanabilir: ['tarih', 'no', 'tur', 'cari', 'toplamNet', 'toplamKdv'],
        satirKimligi: (r) => r.belgeId,
        sutunlar: [
          kdvgSatir.field('tarih', 'tarih', { sirala: true }),
          kdvgSatir.field('no', 'metin', { sirala: true, sabit: true }),
          kdvgSatir.field('tur', 'metin', { sirala: true }),
          kdvgSatir.field('cari', 'metin', { sirala: true }),
          kdvgSatir.field('durum', 'metin'),
          kdvgSatir.field('net20', 'para'),
          kdvgSatir.field('kdv20', 'para'),
          kdvgSatir.field('net10', 'para'),
          kdvgSatir.field('kdv10', 'para'),
          kdvgSatir.field('net1', 'para'),
          kdvgSatir.field('kdv1', 'para'),
          kdvgSatir.field('net0', 'para'),
          kdvgSatir.field('digerNet', 'para', { gizli: true }),
          kdvgSatir.field('digerKdv', 'para', { gizli: true }),
          kdvgSatir.field('toplamNet', 'para', { sirala: true }),
          kdvgSatir.field('toplamKdv', 'para', { sirala: true }),
          kdvgSatir.field('toplamBrut', 'para'),
        ],
      },
    }),
  ],
});

export const LEDGER_REPORTS: readonly ReportDefinition[] = [
  INCOME_EXPENSE,
  CASH_BANK,
  FINANCE_ANALYSIS,
  TRANSFER_HISTORY,
  VAT_LIST,
];
