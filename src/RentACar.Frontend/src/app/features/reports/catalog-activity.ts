import type { Sema } from '@core/api/ui-tipleri';

import {
  OPS_OR_VIEW,
  VIEW_REPORTS,
  type RowOf,
  type SummaryOf,
  cardsFor,
  columnsFor,
  defineReport,
  defineView,
  section,
} from './report-model';

/**
 * F10.2 — rezervasyon kaynağı, otomatik servisler (ViewReports, firma geneli), araç durum takip ve km detay
 * (OperationsWrite ∨ ViewReports; şube kapsamlıda sunucu kendi şubesine zorlar / satırları süzer).
 */
const R = '/api/ui/v1/raporlar';

// ── Rezervasyon kaynağı ─────────────────────────────────────────────────────────────────────
const rk = columnsFor<Sema<'RezervasyonKaynakRow'>>();
export const RESERVATION_SOURCE = defineReport({
  kod: 'rezervasyon-kaynak',
  baslik: 'rapor.baslik.rezervasyonKaynak',
  aciklama: 'rapor.aciklama.rezervasyonKaynak',
  grup: 'operasyon',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/rezervasyon-kaynak`, {
      filtreler: [
        { tur: 'donem' },
        {
          tur: 'secim',
          ad: 'tarihTipi',
          baslik: 'rapor.alan.tarihTipi',
          bosEtiket: 'rapor.secenek.cikisVarsayilan',
          secenekler: [
            { deger: 'Donus', etiket: 'rapor.secenek.donus' },
            { deger: 'Kayit', etiket: 'rapor.secenek.kayit' },
          ],
        },
        { tur: 'metin', ad: 'ofis', baslik: 'rapor.alan.ofis' },
        { tur: 'metin', ad: 'grup', baslik: 'rapor.alan.grup' },
        { tur: 'bayrak', ad: 'iptal', baslik: 'rapor.alan.iptalDahil' },
      ],
      bolumler: [
        section({
          kod: 'kaynaklar',
          baslik: 'rapor.bolum.kaynaklar',
          satirlar: (s) => s,
          sutunlar: [
            rk.field('kaynak', 'metin'),
            rk.field('adet', 'tamsayi'),
            rk.field('toplamGun', 'tamsayi'),
            rk.field('toplamCiro', 'para'),
            rk.field('iptalAdet', 'tamsayi'),
          ],
        }),
      ],
    }),
  ],
});

// ── Otomatik servisler (iş günlüğü) ─────────────────────────────────────────────────────────
type JobRun = Sema<'JobRunRow'>;
const job = columnsFor<JobRun>();
const jobColumns = (sort: boolean) => [
  job.computed('baslangic', 'tarihSaat', (r) => r.baslangicUtc, {
    baslik: 'rapor.alan.baslangic',
    sirala: sort,
    sabit: true,
  }),
  job.field('jobAdi', 'metin', { sirala: sort }),
  job.field('sureMs', 'tamsayi', { sirala: sort }),
  job.field('basarili', 'bayrak'),
  job.field('sonucSayisi', 'tamsayi'),
  job.field('detay', 'metin'),
];
export const JOB_RUNS = defineReport({
  kod: 'otomatik-servisler',
  baslik: 'rapor.baslik.otomatikServisler',
  aciklama: 'rapor.aciklama.otomatikServisler',
  grup: 'operasyon',
  izinler: VIEW_REPORTS,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/otomatik-servisler`, {
      filtreler: [
        { tur: 'donem' },
        { tur: 'metin', ad: 'job', baslik: 'rapor.alan.jobAdi' },
        { tur: 'bayrak', ad: 'hatali', baslik: 'rapor.alan.yalnizHatali' },
      ],
      bolumler: [
        section({
          kod: 'son',
          baslik: 'rapor.bolum.sonKosular',
          satirlar: (s) => s,
          sutunlar: jobColumns(false),
        }),
      ],
      satirlar: {
        siralanabilir: ['baslangic', 'jobAdi', 'sureMs'],
        satirKimligi: (r) => r.id,
        sutunlar: jobColumns(true),
      },
    }),
  ],
});

// ── Araç durum takip (gün / araç) ───────────────────────────────────────────────────────────
const takipGun = columnsFor<Sema<'AracDurumTakipRow'>>();
const takipArac = columnsFor<RowOf<`${typeof R}/arac-durum-takip`>>();
const trackingFilters = [
  { tur: 'donem', ipucu: 'rapor.ipucu.son30' },
  { tur: 'sube', ad: 'sube', baslik: 'rapor.alan.sube' },
  { tur: 'metin', ad: 'aracSahibi', baslik: 'rapor.alan.aracSahibi' },
  { tur: 'metin', ad: 'grup', baslik: 'rapor.alan.grup' },
  { tur: 'metin', ad: 'sipp', baslik: 'rapor.alan.sipp', enFazla: 10 },
  { tur: 'metin', ad: 'plaka', baslik: 'rapor.alan.plaka', enFazla: 20 },
] as const;
export const VEHICLE_TRACKING = defineReport({
  kod: 'arac-durum-takip',
  baslik: 'rapor.baslik.aracDurumTakip',
  aciklama: 'rapor.aciklama.aracDurumTakip',
  grup: 'operasyon',
  izinler: OPS_OR_VIEW,
  firmaGeneli: false,
  gorunumler: [
    defineView(`${R}/arac-durum-takip`, {
      kod: 'gun',
      baslik: 'rapor.gorunum.gunBazli',
      sabit: { gorunum: 'gun' },
      filtreler: trackingFilters,
      bolumler: [
        section({
          kod: 'gunler',
          baslik: 'rapor.bolum.gunluk',
          satirlar: (s) => s.gunler,
          sutunlar: [
            takipGun.field('gun', 'tarih'),
            takipGun.field('toplamArac', 'tamsayi'),
            takipGun.field('dolu', 'tamsayi'),
            takipGun.field('bakim', 'tamsayi'),
            takipGun.field('bos', 'tamsayi'),
            takipGun.field('toplamBaf', 'tamsayi'),
          ],
        }),
      ],
    }),
    defineView(`${R}/arac-durum-takip`, {
      kod: 'arac',
      baslik: 'rapor.gorunum.aracBazli',
      sabit: { gorunum: 'arac' },
      filtreler: trackingFilters,
      satirlar: {
        siralanabilir: ['plaka', 'doluGun', 'bosGun', 'bakimGun', 'sube'],
        satirKimligi: (r) => r.vehicleId,
        sutunlar: [
          takipArac.field('plaka', 'metin', { sirala: true, sabit: true }),
          takipArac.field('sube', 'metin', { sirala: true }),
          takipArac.field('grup', 'metin'),
          takipArac.field('sipp', 'metin', { gizli: true }),
          takipArac.field('aracSahibi', 'metin', { gizli: true }),
          takipArac.field('toplamGun', 'tamsayi'),
          takipArac.field('doluGun', 'tamsayi', { sirala: true }),
          takipArac.field('bakimGun', 'tamsayi', { sirala: true }),
          takipArac.field('bafGun', 'tamsayi'),
          takipArac.field('bosGun', 'tamsayi', { sirala: true }),
        ],
      },
    }),
  ],
});

// ── Km detay ────────────────────────────────────────────────────────────────────────────────
const km = cardsFor<SummaryOf<`${typeof R}/km-detay`>>();
const kmSatir = columnsFor<RowOf<`${typeof R}/km-detay`>>();
export const MILEAGE = defineReport({
  kod: 'km-detay',
  baslik: 'rapor.baslik.kmDetay',
  aciklama: 'rapor.aciklama.kmDetay',
  grup: 'operasyon',
  izinler: OPS_OR_VIEW,
  firmaGeneli: false,
  gorunumler: [
    defineView(`${R}/km-detay`, {
      filtreler: [{ tur: 'donem' }],
      kartlar: [
        km.field('katedilenKm', 'tamsayi'),
        km.field('fazlaKm', 'tamsayi'),
        km.field('fazlaKmBedeli', 'para'),
      ],
      satirlar: {
        siralanabilir: ['sozlesmeNo', 'plaka', 'katedilenKm', 'fazlaKm', 'basTar'],
        satirKimligi: (r) => r.rentalId,
        sutunlar: [
          kmSatir.field('sozlesmeNo', 'metin', {
            sirala: true,
            sabit: true,
            bag: (r) => ['/kiralar', r.rentalId],
          }),
          kmSatir.field('plaka', 'metin', { sirala: true }),
          kmSatir.field('basTar', 'tarih', { sirala: true }),
          kmSatir.field('bitTar', 'tarih'),
          kmSatir.field('cikisKm', 'tamsayi'),
          kmSatir.field('donusKm', 'tamsayi'),
          kmSatir.field('katedilenKm', 'tamsayi', { sirala: true }),
          kmSatir.field('kmLimit', 'tamsayi'),
          kmSatir.field('fazlaKm', 'tamsayi', { sirala: true }),
          kmSatir.field('fazlaKmBedeli', 'para'),
          kmSatir.field('marka', 'metin', { gizli: true }),
          kmSatir.field('tip', 'metin', { gizli: true }),
        ],
      },
    }),
  ],
});
