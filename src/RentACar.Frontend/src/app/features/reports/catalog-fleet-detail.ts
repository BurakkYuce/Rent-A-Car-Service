import type { Schema } from '@core/api/ui-tipleri';

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

/** F10.2 — doluluk, araç günlük durum, servis özeti (uç `ReportApi.Fleet`/`FleetDetail`). Firma geneli. */
const R = '/api/ui/v1/raporlar';

// ── Doluluk ─────────────────────────────────────────────────────────────────────────────────
const dl = cardsFor<SummaryOf<`${typeof R}/doluluk`>>();
const dlDay = columnsFor<Schema<'DolulukGunRow'>>();
export const OCCUPANCY = defineReport({
  kod: 'doluluk',
  baslik: 'rapor.baslik.doluluk',
  aciklama: 'rapor.aciklama.doluluk',
  grup: 'filo',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/doluluk`, {
      filtreler: [
        { tur: 'donem', ipucu: 'rapor.ipucu.buAy' },
        {
          tur: 'secim',
          ad: 'kirilim',
          param: 'boyut',
          baslik: 'rapor.alan.kirilim',
          bosEtiket: 'rapor.secenek.kirilimYok',
          secenekler: [
            { deger: 'Sube', etiket: 'rapor.alan.sube' },
            { deger: 'AracGrubu', etiket: 'rapor.alan.grup' },
            { deger: 'RezervasyonKaynagi', etiket: 'rapor.alan.rezKaynagi' },
          ],
        },
      ],
      kartlar: [
        dl.computed('rapor.alan.aracAdet', 'tamsayi', (s) => s.ozet.aracSayisi),
        dl.computed('rapor.alan.donemGun', 'tamsayi', (s) => s.ozet.donemGun),
        dl.computed('rapor.alan.aracGun', 'tamsayi', (s) => s.ozet.aracGun),
        dl.computed('rapor.alan.kiraGun', 'tamsayi', (s) => s.ozet.kiraGun),
        dl.computed('rapor.alan.dolulukYuzde', 'yuzde', (s) => s.ozet.dolulukYuzde),
        dl.computed('rapor.alan.toplamRezGun', 'tamsayi', (s) => s.gunluk.toplamRezGun),
      ],
      uyarilar: [
        {
          metin: 'rapor.uyari.payda',
          goster: (s) => !!s.gunluk.paydaAciklama,
          parametreler: (s) => ({ metin: s.gunluk.paydaAciklama }),
        },
      ],
      bolumler: [
        section({
          kod: 'gunluk',
          baslik: 'rapor.bolum.gunluk',
          satirlar: (s) => s.gunluk.satirlar,
          sutunlar: [
            dlDay.field('gun', 'tarih'),
            dlDay.field('seri', 'metin'),
            dlDay.field('aracSayisi', 'tamsayi', { baslik: 'rapor.alan.aracAdet' }),
            dlDay.field('kiraGun', 'tamsayi'),
            dlDay.field('rezGun', 'tamsayi'),
            dlDay.field('kiraYuzde', 'yuzde'),
            dlDay.field('rezYuzde', 'yuzde'),
          ],
        }),
      ],
    }),
  ],
});

// ── Araç günlük durum ───────────────────────────────────────────────────────────────────────
const ag = cardsFor<SummaryOf<`${typeof R}/arac-gunluk-durum`>>();
const networkRow = columnsFor<RowOf<`${typeof R}/arac-gunluk-durum`>>();
export const VEHICLE_DAILY = defineReport({
  kod: 'arac-gunluk-durum',
  baslik: 'rapor.baslik.aracGunlukDurum',
  aciklama: 'rapor.aciklama.aracGunlukDurum',
  grup: 'filo',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/arac-gunluk-durum`, {
      filtreler: [
        { tur: 'gun', ad: 'gun', baslik: 'rapor.alan.gun', ipucu: 'rapor.ipucu.bugun' },
        { tur: 'metin', ad: 'ofis', baslik: 'rapor.alan.ofis' },
        { tur: 'metin', ad: 'grup', baslik: 'rapor.alan.grup' },
        { tur: 'metin', ad: 'sipp', baslik: 'rapor.alan.sipp', enFazla: 10 },
        { tur: 'metin', ad: 'aracSahibi', baslik: 'rapor.alan.aracSahibi' },
        { tur: 'metin', ad: 'plaka', baslik: 'rapor.alan.plaka', enFazla: 20 },
      ],
      kartlar: [
        ag.field('gun', 'tarih'),
        ag.field('aracAdet', 'tamsayi'),
        ag.field('gunlukKira', 'para'),
        ag.field('gunlukHizmet', 'para'),
        ag.field('gunlukToplam', 'para'),
      ],
      satirlar: {
        siralanabilir: ['plaka', 'sozlesmeNo', 'gunlukToplam', 'basTar'],
        satirKimligi: (r) => `${r.vehicleId}:${r.rentalId}`,
        sutunlar: [
          networkRow.field('plaka', 'metin', { sirala: true, sabit: true }),
          networkRow.field('sozlesmeNo', 'metin', {
            sirala: true,
            bag: (r) => ['/kiralar', r.rentalId],
          }),
          networkRow.field('musteri', 'metin'),
          networkRow.field('sipp', 'metin', { gizli: true }),
          networkRow.field('grup', 'metin'),
          networkRow.field('aracSahibi', 'metin', { gizli: true }),
          networkRow.field('cikisOfisi', 'metin'),
          networkRow.field('basTar', 'tarih', { sirala: true }),
          networkRow.field('bitTar', 'tarih'),
          networkRow.field('gun', 'tamsayi', { baslik: 'rapor.alan.kiraGun' }),
          networkRow.field('gunlukKira', 'para'),
          networkRow.field('gunlukHizmet', 'para'),
          networkRow.field('gunlukToplam', 'para', { sirala: true }),
        ],
      },
    }),
  ],
});

// ── Servis maliyet özeti ────────────────────────────────────────────────────────────────────
const so = cardsFor<SummaryOf<`${typeof R}/servis-ozet`>>();
const soRow = columnsFor<RowOf<`${typeof R}/servis-ozet`>>();
export const SERVICE_COST = defineReport({
  kod: 'servis-ozet',
  baslik: 'rapor.baslik.servisOzet',
  aciklama: 'rapor.aciklama.servisOzet',
  grup: 'filo',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/servis-ozet`, {
      filtreler: [{ tur: 'donem' }],
      kartlar: [so.field('toplam', 'para'), so.field('adet', 'tamsayi')],
      satirlar: {
        siralanabilir: ['plaka', 'tip', 'toplam', 'adet'],
        satirKimligi: (r) => `${r.vehicleId}:${r.tip}`,
        sutunlar: [
          soRow.field('plaka', 'metin', {
            sirala: true,
            sabit: true,
            bag: (r) => scorecardLink(r.vehicleId),
          }),
          soRow.field('tip', 'metin', { sirala: true, baslik: 'rapor.alan.servisTipi' }),
          soRow.field('toplam', 'para', { sirala: true }),
          soRow.field('adet', 'tamsayi', { sirala: true }),
        ],
      },
    }),
  ],
});
