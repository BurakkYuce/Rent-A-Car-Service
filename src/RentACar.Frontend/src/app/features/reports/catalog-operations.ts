import type { Schema } from '@core/api/ui-tipleri';
import { tarihBicimle } from '@core/bicim/bicim';

import {
  OPS_OR_VIEW,
  type ReportColumn,
  type RowOf,
  type SummaryOf,
  cardsFor,
  columnsFor,
  defineReport,
  defineView,
  section,
} from './report-model';

/**
 * F10.2 — operasyon raporları (uç `ReportApi.Operations`/`Inventory`; OperationsWrite ∨ ViewReports): periyodik
 * servis (şube zorlanır), sigorta-muayene (satır kapsamı), karşılaştırmalı analiz (firma geneli), personel çalışma
 * (kapsam serviste). Vardiya yazma (F10.3) `duzenleyici` bölümüyle: OperationsWrite'ta ekle/düzenle/sil.
 */
const R = '/api/ui/v1/raporlar';

// ── Periyodik servis ────────────────────────────────────────────────────────────────────────
const ps = cardsFor<SummaryOf<`${typeof R}/periyodik-servis`>>();
const psRow = columnsFor<RowOf<`${typeof R}/periyodik-servis`>>();
export const PERIODIC_SERVICE = defineReport({
  kod: 'periyodik-servis',
  baslik: 'rapor.baslik.periyodikServis',
  aciklama: 'rapor.aciklama.periyodikServis',
  grup: 'operasyon',
  izinler: OPS_OR_VIEW,
  firmaGeneli: false,
  gorunumler: [
    defineView(`${R}/periyodik-servis`, {
      filtreler: [
        { tur: 'metin', ad: 'plaka', baslik: 'rapor.alan.plaka', enFazla: 20 },
        { tur: 'sube', ad: 'sube', baslik: 'rapor.alan.sube' },
        {
          tur: 'secim',
          ad: 'aktif',
          baslik: 'rapor.alan.aktiflik',
          secenekler: [
            { deger: 'true', etiket: 'rapor.secenek.aktif' },
            { deger: 'false', etiket: 'rapor.secenek.pasif' },
          ],
        },
        {
          tur: 'sayi',
          ad: 'esik',
          baslik: 'rapor.alan.esik',
          tamsayi: true,
          enAz: 0,
          enFazla: 10_000_000,
        },
      ],
      kartlar: [ps.field('adet', 'tamsayi', { baslik: 'rapor.alan.aracAdet' })],
      satirlar: {
        siralanabilir: ['plaka', 'kalanKm', 'guncelKm', 'sube'],
        satirKimligi: (r) => r.vehicleId,
        sutunlar: [
          psRow.field('plaka', 'metin', { sirala: true, sabit: true }),
          psRow.field('sube', 'metin', { sirala: true }),
          psRow.field('guncelKm', 'tamsayi', { sirala: true }),
          psRow.field('sonrakiBakimKm', 'tamsayi'),
          psRow.field('kalanKm', 'tamsayi', { sirala: true }),
          psRow.field('kaynak', 'metin', { gizli: true }),
          psRow.field('marka', 'metin'),
          psRow.field('tip', 'metin'),
          psRow.field('modelYili', 'metin', { gizli: true }),
          psRow.field('sonServisTarihi', 'tarih'),
          psRow.field('sonServisKm', 'tamsayi'),
          psRow.field('aktif', 'bayrak', { gizli: true }),
        ],
      },
    }),
  ],
});

// ── Sigorta / muayene ───────────────────────────────────────────────────────────────────────
const sm = cardsFor<SummaryOf<`${typeof R}/sigorta-muayene`>>();
const smRow = columnsFor<RowOf<`${typeof R}/sigorta-muayene`>>();
const DOC_TYPES = ['Trafik', 'Kasko', 'Muayene', 'Mtv', 'ZIzni', 'Seyrusefer'] as const;
export const INSURANCE_INSPECTION = defineReport({
  kod: 'sigorta-muayene',
  baslik: 'rapor.baslik.sigortaMuayene',
  aciklama: 'rapor.aciklama.sigortaMuayene',
  grup: 'operasyon',
  izinler: OPS_OR_VIEW,
  firmaGeneli: false,
  gorunumler: [
    defineView(`${R}/sigorta-muayene`, {
      filtreler: [
        {
          tur: 'secim',
          ad: 'tur',
          baslik: 'rapor.alan.belgeTuru',
          bosEtiket: 'rapor.secenek.hepsi',
          secenekler: DOC_TYPES.map((d) => ({
            deger: d,
            etiket: `rapor.secenek.belge${d}` as const,
          })),
        },
        { tur: 'gun', ad: 'enGec', baslik: 'rapor.alan.enGec' },
        {
          tur: 'metin',
          ad: 'sahip',
          baslik: 'rapor.alan.aracSahibi',
          oneriler: (s) => s.aracSahipleri,
        },
        { tur: 'metin', ad: 'plaka', baslik: 'rapor.alan.plaka', enFazla: 20 },
      ],
      kartlar: [sm.field('adet', 'tamsayi', { baslik: 'rapor.alan.aracAdet' })],
      satirlar: {
        siralanabilir: ['plaka', 'trafikBitis', 'kaskoBitis', 'muayeneBitis', 'mtvVade'],
        satirKimligi: (r) => r.vehicleId,
        sutunlar: [
          smRow.field('plaka', 'metin', { sirala: true, sabit: true }),
          smRow.field('marka', 'metin'),
          smRow.field('tip', 'metin', { gizli: true }),
          smRow.field('sube', 'metin'),
          smRow.field('aracSahibi', 'metin'),
          smRow.field('trafikBitis', 'tarih', { sirala: true }),
          smRow.field('kaskoBitis', 'tarih', { sirala: true }),
          smRow.field('muayeneBitis', 'tarih', { sirala: true }),
          smRow.field('mtvVade', 'tarih', { sirala: true }),
          smRow.field('mtvOdendi', 'bayrak'),
          smRow.field('zIzniBitis', 'tarih', { gizli: true }),
          smRow.field('seyrusiferBitis', 'tarih', { gizli: true }),
          smRow.field('belgeNo', 'metin', { gizli: true }),
          smRow.field('kimde', 'metin', { gizli: true }),
          smRow.field('sasiNo', 'metin', { gizli: true }),
        ],
      },
    }),
  ],
});

// ── Karşılaştırmalı analiz (ay × kırılım) ───────────────────────────────────────────────────
const ka = cardsFor<SummaryOf<`${typeof R}/karsilastirmali-analiz`>>();
type CompRow = Schema<'ComparativeRow'>;
const kaRow = columnsFor<CompRow>();
function monthColumns(keys: readonly string[]): readonly ReportColumn<CompRow>[] {
  return [
    kaRow.field('kirilim', 'metin', { sabit: true }),
    ...keys.map((k, i) =>
      kaRow.computed(`m${i}`, 'sayi', (r) => (r.aylar as Record<string, unknown>)[k], {
        baslik: 'rapor.alan.ay',
        baslikMetni: k,
      }),
    ),
    kaRow.field('toplam', 'sayi'),
  ];
}
export const COMPARATIVE = defineReport({
  kod: 'karsilastirmali-analiz',
  baslik: 'rapor.baslik.karsilastirmali',
  aciklama: 'rapor.aciklama.karsilastirmali',
  grup: 'operasyon',
  izinler: OPS_OR_VIEW,
  firmaGeneli: true,
  gorunumler: [
    defineView(`${R}/karsilastirmali-analiz`, {
      filtreler: [
        { tur: 'donem' },
        {
          tur: 'secim',
          ad: 'tablo',
          baslik: 'rapor.alan.kaynakTablo',
          bosEtiket: 'rapor.secenek.kiraVarsayilan',
          secenekler: [{ deger: 'Rezervasyon', etiket: 'rapor.secenek.rezervasyon' }],
        },
        {
          tur: 'secim',
          ad: 'veri',
          baslik: 'rapor.alan.veriTuru',
          bosEtiket: 'rapor.secenek.adetVarsayilan',
          secenekler: [{ deger: 'Gun', etiket: 'rapor.secenek.gunSayisi' }],
        },
        {
          tur: 'secim',
          ad: 'kirilim',
          baslik: 'rapor.alan.kirilim',
          bosEtiket: 'rapor.secenek.grupVarsayilan',
          secenekler: [
            { deger: 'RezKaynagi', etiket: 'rapor.alan.rezKaynagi' },
            { deger: 'CikisNoktasi', etiket: 'rapor.alan.cikisOfisi' },
          ],
        },
        { tur: 'metin', ad: 'ofis', baslik: 'rapor.alan.ofis' },
      ],
      kartlar: [ka.field('genelToplam', 'sayi')],
      bolumler: [
        section({
          kod: 'matris',
          baslik: 'rapor.bolum.aylik',
          satirlar: (s) => s.satirlar,
          sutunlar: (s) => monthColumns(s.ayAnahtarlari),
          toplam: (s) => ({
            ...Object.fromEntries(s.ayToplamlari.map((v, i) => [`m${i}`, v])),
            toplam: s.genelToplam,
          }),
        }),
      ],
    }),
  ],
});

// ── Personel çalışma (vardiya) ──────────────────────────────────────────────────────────────
const pc = cardsFor<SummaryOf<`${typeof R}/personel-calisma`>>();
type Matrix = Schema<'ShiftMatrixRow'>;
const pcMatrix = columnsFor<Matrix>();
const pcList = columnsFor<Schema<'ShiftRow'>>();
function dayColumns(days: readonly string[]): readonly ReportColumn<Matrix>[] {
  return [
    pcMatrix.field('personelAd', 'metin', { sabit: true }),
    ...days.map((d, i) =>
      pcMatrix.computed(
        `g${i}`,
        'metin',
        (r) =>
          r.gunler
            .find((x) => x.gun === d)
            ?.vardiyalar.map((v) => v.aralik)
            .join(', ') || null,
        { baslik: 'rapor.alan.gun', baslikMetni: tarihBicimle(d) },
      ),
    ),
    pcMatrix.field('toplamSaatMetni', 'metin'),
  ];
}
export const STAFF_SHIFTS = defineReport({
  kod: 'personel-calisma',
  baslik: 'rapor.baslik.personelCalisma',
  aciklama: 'rapor.aciklama.personelCalisma',
  grup: 'operasyon',
  izinler: OPS_OR_VIEW,
  firmaGeneli: false,
  gorunumler: [
    defineView(`${R}/personel-calisma`, {
      zarfsiz: true,
      // F10.3: OperationsWrite'ta vardiya listesi ekle/düzenle/sil bölümüyle çizilir (`/api/ui/v1/vardiyalar`).
      duzenleyici: { tur: 'vardiya', bolum: 'liste', izin: 'OperationsWrite' },
      filtreler: [
        { tur: 'donem', ipucu: 'rapor.ipucu.vardiyaPencere' },
        { tur: 'arama', ad: 'personelId', baslik: 'rapor.alan.personel', kaynak: 'personel' },
        { tur: 'sube', ad: 'sube', baslik: 'rapor.alan.sube' },
      ],
      kartlar: [
        pc.field('bas', 'tarih', { baslik: 'rapor.alan.basTar' }),
        pc.field('bit', 'tarih', { baslik: 'rapor.alan.bitTar' }),
        pc.field('toplamVardiya', 'tamsayi'),
        pc.field('toplamDk', 'tamsayi'),
      ],
      uyarilar: [{ metin: 'rapor.uyari.kirpildi', goster: (s) => s.kirpildi }],
      bolumler: [
        section({
          kod: 'matris',
          baslik: 'rapor.bolum.vardiyaMatrisi',
          satirlar: (s) => s.matris,
          sutunlar: (s) => dayColumns(s.gunler),
        }),
        section({
          kod: 'liste',
          baslik: 'rapor.bolum.vardiyalar',
          satirlar: (s) => s.liste,
          sutunlar: [
            pcList.field('tarih', 'tarih'),
            pcList.field('personelAd', 'metin'),
            pcList.field('personelKadroSube', 'metin'),
            pcList.field('aralik', 'metin'),
            pcList.field('sureDk', 'tamsayi'),
            pcList.field('sube', 'metin'),
            pcList.field('aciklama', 'metin'),
          ],
        }),
      ],
    }),
  ],
});
