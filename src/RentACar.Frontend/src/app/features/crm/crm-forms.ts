import type { GunMetni } from '@core/form/tarih-girdisi';
import { anDegeri, gunDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { toNumber } from '@features/vehicles/vehicle-model';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

import {
  linked,
  linkedRental,
  type Assistance,
  type AssistanceRequest,
  type Complaint,
  type ComplaintRequest,
  type LegalFile,
  type LegalFileRequest,
  type Survey,
  type SurveyAnswer,
  type SurveyRequest,
} from './crm-model';

/**
 * CRM formlarının saf eşlemeleri (kayıt ↔ form ↔ gövde). Tarih = İstanbul takvim günü; dokunulmayan gün sunucunun
 * anıyla AYNEN geri gider. Para invariant metin. PUT tam değiştirme: `surum` kaydın sürümü (yeni kayıtta yok).
 */

const text = (v: string | null | undefined) => metinDegeri(v ?? null);

// ------------------------------------------------------------------ anket

export interface SurveyFormValue {
  readonly cari: SecimSecenegi | null;
  readonly kira: SecimSecenegi | null;
  readonly anketTuru: string | null;
  readonly durum: string | null;
  readonly cikisOfisi: string | null;
  readonly tarih: GunMetni | null;
  readonly puan: number | null;
  readonly kaynak: string | null;
  readonly yorum: string | null;
}

export interface AnswerRow {
  readonly soru: string | null;
  readonly cevap: string | null;
  readonly aciklama: string | null;
}

export function emptySurvey(): SurveyFormValue {
  return {
    cari: null,
    kira: null,
    anketTuru: null,
    durum: 'Yapildi',
    cikisOfisi: null,
    tarih: null,
    puan: 0,
    kaynak: null,
    yorum: null,
  };
}

export function surveyToForm(s: Survey): SurveyFormValue {
  return {
    cari: linked(s.cariId, s.musteriAd),
    kira: linkedRental(s.rentalId, s.sozlesmeNo),
    anketTuru: s.anketTuru,
    durum: s.durum,
    cikisOfisi: s.cikisOfisi,
    tarih: gunDegeri(s.tarih),
    puan: toNumber(s.puan),
    kaynak: s.kaynak,
    yorum: s.yorum,
  };
}

/**
 * Soru satırları (Blazor: varsayılan soru sayısı kadar satır; kayıtlı cevap SORU NO ile eşleşir, sorusu kayıttaki
 * METİN — snapshot). Kayıtta varsayılandan fazla soru varsa satır sayısı ona uzar.
 */
export function answerRows(
  defaults: readonly string[],
  answers: readonly SurveyAnswer[],
): AnswerRow[] {
  const max = answers.reduce((m, a) => Math.max(m, toNumber(a.soruNo) ?? 0), defaults.length);
  const rows: AnswerRow[] = [];
  for (let no = 1; no <= max; no++) {
    const a = answers.find((x) => toNumber(x.soruNo) === no);
    rows.push({
      soru: a?.soru ?? defaults[no - 1] ?? null,
      cevap: a?.cevap ?? null,
      aciklama: a?.aciklama ?? null,
    });
  }
  return rows;
}

export function surveyRequest(
  v: SurveyFormValue,
  rows: readonly AnswerRow[],
  base: { readonly tarih: string; readonly surum?: string | null } | null,
): SurveyRequest {
  return {
    cariId: v.cari?.id ?? null,
    rentalId: v.kira?.id ?? null,
    puan: v.puan ?? 0,
    yorum: text(v.yorum),
    tarih: anDegeri(v.tarih, base?.tarih),
    kaynak: text(v.kaynak),
    anketTuru: v.anketTuru ?? null,
    durum: v.durum ?? 'Yapildi',
    cikisOfisi: text(v.cikisOfisi),
    // Sorusu boş satır kaydedilmez (Blazor); soru no satır sırasıdır.
    cevaplar: rows
      .map((r, i) => ({
        soruNo: i + 1,
        soru: text(r.soru),
        cevap: text(r.cevap),
        aciklama: text(r.aciklama),
      }))
      .filter((a) => a.soru !== null),
    surum: base?.surum ?? null,
  };
}

// ------------------------------------------------------------------ şikayet

export interface ComplaintFormValue {
  readonly cari: SecimSecenegi | null;
  readonly kira: SecimSecenegi | null;
  readonly sikayetYeri: string | null;
  readonly sikayetKanali: string | null;
  readonly cikisOfisi: string | null;
  readonly teslimAlan: SecimSecenegi | null;
  readonly teslimEden: SecimSecenegi | null;
  readonly puan: number | null;
  readonly tarih: GunMetni | null;
  readonly konu: string | null;
  readonly detay: string | null;
  readonly durum: string | null;
  readonly cozum: string | null;
}

export function emptyComplaint(): ComplaintFormValue {
  return {
    cari: null,
    kira: null,
    sikayetYeri: null,
    sikayetKanali: null,
    cikisOfisi: null,
    teslimAlan: null,
    teslimEden: null,
    puan: null,
    tarih: null,
    konu: null,
    detay: null,
    durum: 'Acik',
    cozum: null,
  };
}

export function complaintToForm(c: Complaint): ComplaintFormValue {
  return {
    cari: linked(c.cariId, c.musteriAd),
    kira: linkedRental(c.rentalId, c.sozlesmeNo, c.plaka),
    sikayetYeri: c.sikayetYeri,
    sikayetKanali: c.sikayetKanali,
    cikisOfisi: c.cikisOfisi,
    teslimAlan: linked(c.teslimAlanPersonelId, c.teslimAlanAd),
    teslimEden: linked(c.teslimEdenPersonelId, c.teslimEdenAd),
    puan: toNumber(c.puan),
    tarih: gunDegeri(c.tarih),
    konu: c.konu,
    detay: c.detay,
    durum: c.durum,
    cozum: c.cozum,
  };
}

export function complaintRequest(
  v: ComplaintFormValue,
  base: { readonly tarih: string; readonly surum?: string | null } | null,
): ComplaintRequest {
  return {
    cariId: v.cari?.id ?? null,
    konu: text(v.konu),
    detay: text(v.detay),
    durum: v.durum ?? 'Acik',
    tarih: anDegeri(v.tarih, base?.tarih),
    cozum: text(v.cozum),
    rentalId: v.kira?.id ?? null,
    teslimAlanPersonelId: v.teslimAlan?.id ?? null,
    teslimEdenPersonelId: v.teslimEden?.id ?? null,
    puan: v.puan,
    sikayetKanali: text(v.sikayetKanali),
    sikayetYeri: v.sikayetYeri ?? null,
    cikisOfisi: text(v.cikisOfisi),
    surum: base?.surum ?? null,
  };
}

// ------------------------------------------------------------------ assistans

export interface AssistanceFormValue {
  readonly kira: SecimSecenegi | null;
  readonly plaka: string | null;
  readonly adSoyad: string | null;
  readonly cepTel: string | null;
  /** UTC anı (tarih-saat seçici İstanbul saatiyle gösterir). */
  readonly zaman: string | null;
  readonly sebep: string | null;
  readonly yedekLastikMi: boolean;
  readonly aracHareketMi: boolean;
  readonly kapandi: boolean;
  readonly mesaj: string | null;
  readonly cozum: string | null;
  /** #295 L1: KVKK ile gizlenen ad/telefonu bilinçli temizle → `""` (boş = `null` = dokunma). */
  readonly clearName: boolean;
  readonly clearPhone: boolean;
}

export function emptyAssistance(): AssistanceFormValue {
  return {
    kira: null,
    plaka: null,
    adSoyad: null,
    cepTel: null,
    zaman: null,
    sebep: null,
    yedekLastikMi: false,
    aracHareketMi: false,
    kapandi: false,
    mesaj: null,
    cozum: null,
    clearName: false,
    clearPhone: false,
  };
}

export function assistanceToForm(a: Assistance): AssistanceFormValue {
  return {
    kira: linkedRental(a.rentalId, a.sozlesmeNo),
    plaka: a.plaka,
    adSoyad: a.adSoyad,
    cepTel: a.cepTel,
    zaman: a.zaman,
    sebep: a.sebep,
    yedekLastikMi: a.yedekLastikMi,
    aracHareketMi: a.aracHareketMi,
    kapandi: a.kapandi,
    mesaj: a.mesaj,
    cozum: a.cozum,
    clearName: false,
    clearPhone: false,
  };
}

/** Kayıttaki ad/telefon KVKK ile gizli olabilir mi (bağlı kira var ve yanıt değeri boş). */
export function assistanceHiddenContact(a: Assistance | null): {
  readonly name: boolean;
  readonly phone: boolean;
} {
  const linked = a !== null && a.rentalId !== null;
  return { name: linked && a.adSoyad === null, phone: linked && a.cepTel === null };
}

/**
 * Ad/telefon gövdesi — sunucu sözleşmesi (#295b): `""` = temizle; `null` = dokunma (aynı kirada saklı değer korunur;
 * oluşturmada ya da kira değişince sözleşmenin GÖRÜNÜR müşterisinden doldurulur). Kayıtta GÖRÜNÜR bir değeri kullanıcı
 * boşalttıysa bu bilinçli temizlemedir → `""`; gizli (null gösterilen) alan boşsa `null` gider, "Temizle" kutusu `""`.
 */
export function contactValue(
  typed: string | null,
  clear: boolean,
  stored: string | null | undefined,
): string | null {
  if (clear) return '';
  const v = text(typed);
  if (v !== null) return v;
  return stored !== null && stored !== undefined ? '' : null;
}

export function assistanceRequest(
  v: AssistanceFormValue,
  base: { readonly surum?: string | null; readonly row?: Assistance | null } | null,
): AssistanceRequest {
  return {
    rentalId: v.kira?.id ?? null,
    plaka: text(v.plaka),
    adSoyad: contactValue(v.adSoyad, v.clearName, base?.row?.adSoyad),
    cepTel: contactValue(v.cepTel, v.clearPhone, base?.row?.cepTel),
    zaman: v.zaman,
    mesaj: text(v.mesaj),
    sebep: text(v.sebep),
    yedekLastikMi: v.yedekLastikMi,
    aracHareketMi: v.aracHareketMi,
    kapandi: v.kapandi,
    cozum: text(v.cozum),
    surum: base?.surum ?? null,
  };
}

// ------------------------------------------------------------------ hukuk

export interface LegalFormValue {
  readonly dosyaNo: string | null;
  readonly faturaNoTemp: string | null;
  readonly tur: string | null;
  readonly cari: SecimSecenegi | null;
  readonly avukat: string | null;
  readonly avukatTel: string | null;
  readonly avukatMail: string | null;
  readonly avukat2Ad: string | null;
  readonly avukat2Tel: string | null;
  readonly avukat2Mail: string | null;
  readonly tutar: string | null;
  readonly tahsilat: string | null;
  readonly durum: string | null;
  readonly tarih: GunMetni | null;
  readonly aciklama: string | null;
  readonly aktif: boolean;
}

export function emptyLegal(): LegalFormValue {
  return {
    dosyaNo: null,
    faturaNoTemp: null,
    tur: 'Dava',
    cari: null,
    avukat: null,
    avukatTel: null,
    avukatMail: null,
    avukat2Ad: null,
    avukat2Tel: null,
    avukat2Mail: null,
    tutar: null,
    tahsilat: null,
    durum: 'Acik',
    tarih: null,
    aciklama: null,
    aktif: true,
  };
}

const money = (v: number | string | null | undefined) =>
  v === null || v === undefined || v === '' ? null : String(v);

export function legalToForm(h: LegalFile): LegalFormValue {
  return {
    dosyaNo: h.dosyaNo,
    faturaNoTemp: h.faturaNoTemp,
    tur: h.tur,
    cari: linked(h.cariId, h.musteriAd),
    avukat: h.avukat,
    avukatTel: h.avukatTel,
    avukatMail: h.avukatMail,
    avukat2Ad: h.avukat2Ad,
    avukat2Tel: h.avukat2Tel,
    avukat2Mail: h.avukat2Mail,
    tutar: money(h.tutar),
    tahsilat: money(h.tahsilat),
    durum: h.durum,
    tarih: gunDegeri(h.tarih),
    aciklama: h.aciklama,
    aktif: h.aktif,
  };
}

export function legalRequest(
  v: LegalFormValue,
  base: { readonly tarih: string; readonly surum?: string | null } | null,
): LegalFileRequest {
  return {
    dosyaNo: text(v.dosyaNo),
    cariId: v.cari?.id ?? null,
    tur: v.tur ?? 'Dava',
    avukat: text(v.avukat),
    tutar: money(v.tutar) ?? '0',
    durum: v.durum ?? 'Acik',
    tarih: anDegeri(v.tarih, base?.tarih),
    aciklama: text(v.aciklama),
    aktif: v.aktif,
    faturaNoTemp: text(v.faturaNoTemp),
    avukatTel: text(v.avukatTel),
    avukatMail: text(v.avukatMail),
    avukat2Ad: text(v.avukat2Ad),
    avukat2Tel: text(v.avukat2Tel),
    avukat2Mail: text(v.avukat2Mail),
    tahsilat: money(v.tahsilat),
    surum: base?.surum ?? null,
  };
}
