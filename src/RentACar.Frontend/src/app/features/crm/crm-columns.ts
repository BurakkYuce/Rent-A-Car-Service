import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { toNumber } from '@features/vehicles/vehicle-model';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import type { Assistance, Complaint, CrmSegmentRow, LegalFile, Survey } from './crm-model';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

const dash = (v: string | null | undefined) =>
  v === null || v === undefined || v.trim() === '' ? '—' : v;
const enumLabel = (t: Translate, prefix: string, v: string | null) =>
  v === null ? '—' : t(`${prefix}.${v}` as CeviriAnahtari);
const actions = <T>(t: Translate, width = 150): TabloSutunu<T> => ({
  kod: 'islemler',
  baslik: t('crm.islemler'),
  deger: () => null,
  genislik: width,
});

/** Blazor `AnketList` tablosu (cevap sayısı sütunu YOK — uç satırda taşımıyor; parite farkı). */
export function surveyColumns(t: Translate): readonly TabloSutunu<Survey>[] {
  const h = (k: string) => t(`crm.anket.sutun.${k}` as CeviriAnahtari);
  return [
    {
      kod: 'tarih',
      baslik: h('tarih'),
      deger: (r) => r.tarih,
      tur: 'tarih',
      sirala: true,
      genislik: 105,
    },
    {
      kod: 'anketTuru',
      baslik: h('tur'),
      deger: (r) => enumLabel(t, 'crm.anket.turler', r.anketTuru),
      sirala: true,
      genislik: 90,
    },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 110 },
    { kod: 'musteri', baslik: h('musteri'), deger: (r) => dash(r.musteriAd), genislik: 170 },
    {
      kod: 'sozlesmeNo',
      baslik: h('sozlesme'),
      deger: (r) => dash(r.sozlesmeNo),
      sirala: true,
      genislik: 140,
    },
    {
      kod: 'cikisOfisi',
      baslik: h('ofis'),
      deger: (r) => dash(r.cikisOfisi),
      sirala: true,
      genislik: 130,
    },
    {
      kod: 'puan',
      baslik: h('puan'),
      deger: (r) => toNumber(r.puan),
      tur: 'sayi',
      sirala: true,
      genislik: 70,
    },
    { kod: 'yorum', baslik: h('yorum'), deger: (r) => dash(r.yorum), genislik: 200 },
    { kod: 'kaynak', baslik: h('kaynak'), deger: (r) => dash(r.kaynak), genislik: 110 },
    actions<Survey>(t),
  ];
}

export function complaintColumns(t: Translate): readonly TabloSutunu<Complaint>[] {
  const h = (k: string) => t(`crm.sikayet.sutun.${k}` as CeviriAnahtari);
  return [
    {
      kod: 'tarih',
      baslik: h('tarih'),
      deger: (r) => r.tarih,
      tur: 'tarih',
      sirala: true,
      genislik: 105,
    },
    { kod: 'musteri', baslik: h('musteri'), deger: (r) => dash(r.musteriAd), genislik: 160 },
    { kod: 'tel', baslik: h('tel'), deger: (r) => dash(r.musteriTel), genislik: 120 },
    {
      kod: 'sozlesmeNo',
      baslik: h('sozlesme'),
      deger: (r) => dash(r.sozlesmeNo),
      sirala: true,
      genislik: 140,
    },
    { kod: 'plaka', baslik: h('plaka'), deger: (r) => dash(r.plaka), sirala: true, genislik: 100 },
    {
      kod: 'cikisOfisi',
      baslik: h('ofis'),
      deger: (r) => dash(r.cikisOfisi),
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'yer',
      baslik: h('yer'),
      deger: (r) => enumLabel(t, 'crm.sikayet.yerler', r.sikayetYeri),
      genislik: 100,
    },
    {
      kod: 'sikayetKanali',
      baslik: h('kanal'),
      deger: (r) => dash(r.sikayetKanali),
      sirala: true,
      genislik: 100,
    },
    {
      kod: 'puan',
      baslik: h('puan'),
      deger: (r) => toNumber(r.puan),
      tur: 'sayi',
      sirala: true,
      genislik: 70,
    },
    {
      kod: 'teslimAlan',
      baslik: h('teslimAlan'),
      deger: (r) => dash(r.teslimAlanAd),
      genislik: 130,
    },
    {
      kod: 'teslimEden',
      baslik: h('teslimEden'),
      deger: (r) => dash(r.teslimEdenAd),
      genislik: 130,
    },
    { kod: 'konu', baslik: h('konu'), deger: (r) => r.konu, sirala: true, genislik: 180 },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 100 },
    { kod: 'cozum', baslik: h('cozum'), deger: (r) => dash(r.cozum), genislik: 180 },
    actions<Complaint>(t),
  ];
}

export function assistanceColumns(t: Translate): readonly TabloSutunu<Assistance>[] {
  const h = (k: string) => t(`crm.assistans.sutun.${k}` as CeviriAnahtari);
  return [
    {
      kod: 'zaman',
      baslik: h('zaman'),
      deger: (r) => r.zaman,
      tur: 'tarihSaat',
      sirala: true,
      genislik: 140,
    },
    { kod: 'plaka', baslik: h('plaka'), deger: (r) => dash(r.plaka), sirala: true, genislik: 100 },
    { kod: 'adSoyad', baslik: h('adSoyad'), deger: (r) => dash(r.adSoyad), genislik: 150 },
    { kod: 'cepTel', baslik: h('cepTel'), deger: (r) => dash(r.cepTel), genislik: 120 },
    { kod: 'mesaj', baslik: h('mesaj'), deger: (r) => r.mesaj, genislik: 200 },
    { kod: 'sebep', baslik: h('sebep'), deger: (r) => dash(r.sebep), sirala: true, genislik: 140 },
    {
      kod: 'yedekLastik',
      baslik: h('yedekLastik'),
      deger: (r) => (r.yedekLastikMi ? t('crm.evet') : '—'),
      genislik: 100,
    },
    { kod: 'hareket', baslik: h('hareket'), deger: () => null, genislik: 110 },
    { kod: 'kapandi', baslik: h('durum'), deger: () => null, sirala: true, genislik: 90 },
    { kod: 'cozum', baslik: h('cozum'), deger: (r) => dash(r.cozum), genislik: 180 },
    actions<Assistance>(t),
  ];
}

export function legalColumns(t: Translate): readonly TabloSutunu<LegalFile>[] {
  const h = (k: string) => t(`crm.hukuk.sutun.${k}` as CeviriAnahtari);
  return [
    {
      kod: 'dosyaNo',
      baslik: h('dosyaNo'),
      deger: (r) => r.dosyaNo,
      sirala: true,
      sabit: true,
      genislik: 120,
    },
    { kod: 'musteri', baslik: h('musteri'), deger: (r) => dash(r.musteriAd), genislik: 160 },
    { kod: 'faturaNo', baslik: h('faturaNo'), deger: (r) => dash(r.faturaNoTemp), genislik: 120 },
    {
      kod: 'tur',
      baslik: h('tur'),
      deger: (r) => enumLabel(t, 'crm.hukuk.turler', r.tur),
      sirala: true,
      genislik: 80,
    },
    {
      kod: 'avukat',
      baslik: h('avukat'),
      deger: (r) => dash(r.avukat),
      sirala: true,
      genislik: 140,
    },
    { kod: 'avukatTel', baslik: h('avukatTel'), deger: (r) => dash(r.avukatTel), genislik: 120 },
    { kod: 'avukat2', baslik: h('avukat2'), deger: (r) => dash(r.avukat2Ad), genislik: 130 },
    {
      kod: 'tutar',
      baslik: h('tutar'),
      deger: (r) => toNumber(r.tutar),
      tur: 'para',
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'tahsilat',
      baslik: h('tahsilat'),
      deger: (r) => toNumber(r.tahsilat),
      tur: 'para',
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'kalan',
      baslik: h('kalan'),
      deger: (r) => toNumber(r.kalan),
      tur: 'para',
      sirala: true,
      genislik: 120,
    },
    { kod: 'durum', baslik: h('durum'), deger: (r) => r.durum, sirala: true, genislik: 100 },
    {
      kod: 'tarih',
      baslik: h('tarih'),
      deger: (r) => r.tarih,
      tur: 'tarih',
      sirala: true,
      genislik: 105,
    },
    actions<LegalFile>(t),
  ];
}

/** Blazor `CrmAnaliz` segment tablosu. Ort. KM girilmemişse "—" (0 "hiç yol yok" iddiası olurdu). */
export function segmentColumns(t: Translate): readonly TabloSutunu<CrmSegmentRow>[] {
  const h = (k: string) => t(`crm.analiz.sutun.${k}` as CeviriAnahtari);
  return [
    {
      kod: 'ad',
      baslik: h('musteri'),
      deger: (r) => r.ad,
      sirala: true,
      sabit: true,
      genislik: 180,
    },
    { kod: 'mail', baslik: h('mail'), deger: (r) => dash(r.mail), genislik: 180 },
    { kod: 'tel', baslik: h('tel'), deger: (r) => dash(r.tel), genislik: 120 },
    {
      kod: 'kiraSayisi',
      baslik: h('kiraSayisi'),
      deger: (r) => toNumber(r.kiraSayisi),
      tur: 'sayi',
      sirala: true,
      genislik: 90,
    },
    {
      kod: 'toplamCiro',
      baslik: h('ciro'),
      deger: (r) => toNumber(r.toplamCiro),
      tur: 'para',
      sirala: true,
      genislik: 130,
    },
    {
      kod: 'ortalamaKiraBedeli',
      baslik: h('ortKira'),
      deger: (r) => toNumber(r.ortalamaKiraBedeli),
      tur: 'para',
      sirala: true,
      genislik: 130,
    },
    {
      kod: 'ortalamaKm',
      baslik: h('ortKm'),
      deger: (r) => toNumber(r.ortalamaKm),
      tur: 'sayi',
      sirala: true,
      genislik: 90,
    },
    {
      kod: 'hizmetBedeli',
      baslik: h('hizmet'),
      deger: (r) => toNumber(r.hizmetBedeli),
      tur: 'para',
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'dogumTarihi',
      baslik: h('dogum'),
      deger: (r) => r.dogumTarihi,
      tur: 'tarih',
      genislik: 105,
    },
    {
      kod: 'ilkKiraZamani',
      baslik: h('ilkKira'),
      deger: (r) => r.ilkKiraZamani,
      tur: 'tarih',
      sirala: true,
      genislik: 105,
    },
    {
      kod: 'sonIslem',
      baslik: h('sonIslem'),
      deger: (r) => r.sonIslem,
      tur: 'tarih',
      sirala: true,
      genislik: 105,
    },
    { kod: 'segment', baslik: h('segment'), deger: (r) => r.segment, sirala: true, genislik: 110 },
  ];
}
