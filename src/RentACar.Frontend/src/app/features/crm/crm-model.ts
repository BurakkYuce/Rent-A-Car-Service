import type { ApiYolu } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { listeTanimi } from '@core/veri/liste-sorgusu';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

// ---- Uçlar (`/api/ui/v1/...`; CRM OperationsWrite, analiz ViewReports)
export const SURVEYS = '/api/ui/v1/anketler';
export const COMPLAINTS = '/api/ui/v1/sikayetler';
export const ASSISTANCE = '/api/ui/v1/assistans-talepleri';
export const LEGAL_FILES = '/api/ui/v1/hukuk-dosyalari';
export const CRM_ANALYSIS = '/api/ui/v1/crm/analiz';
export const RENTAL_PICK = '/api/ui/v1/crm/secim/kira';

export function recordPath(base: ApiYolu, id: string): ApiYolu {
  return `${base}/${encodeURIComponent(id)}` as ApiYolu;
}

export type Survey = Sema<'SurveyRow'>;
export type SurveyCard = Sema<'SurveyCardDto'>;
export type SurveyAnswer = Sema<'SurveyAnswerDto'>;
export type SurveyRequest = Sema<'SurveyUpdateRequest'>;
export type Complaint = Sema<'ComplaintRow'>;
export type ComplaintCard = Sema<'ComplaintCardDto'>;
export type ComplaintRequest = Sema<'ComplaintUpdateRequest'>;
export type Assistance = Sema<'AssistanceRow'>;
export type AssistanceCard = Sema<'AssistanceCardDto'>;
export type AssistanceRequest = Sema<'AssistanceUpdateRequest'>;
export type LegalFile = Sema<'LegalFileRow'>;
export type LegalFileCard = Sema<'LegalFileCardDto'>;
export type LegalFileRequest = Sema<'LegalFileUpdateRequest'>;
export type CrmAnalysis = Sema<'CrmAnalysisDto'>;
export type CrmSegmentRow = Sema<'CrmSegmentRow'>;
export type CrmFilterOptions = Sema<'CrmFilterOptions'>;
export type RentalPickItem = Sema<'RentalPickItem'>;

/** Sunucu enum ADLARI (tanımsız ad 400). */
export const SURVEY_TYPES = ['Cikis', 'Donus'] as const;
export const SURVEY_STATUSES = ['Yapildi', 'Yapilmadi'] as const;
export const COMPLAINT_STATUSES = ['Acik', 'Cozuldu', 'Kapali'] as const;
export const COMPLAINT_PLACES = ['Kira', 'Rezervasyon'] as const;
export const LEGAL_TYPES = ['Dava', 'Icra', 'Diger'] as const;
export const LEGAL_STATUSES = ['Acik', 'Beklemede', 'Kapali'] as const;
/** Blazor şikayet kanalı önerileri (serbest metin de kabul). */
export const COMPLAINT_CHANNELS = ['Telefon', 'Web', 'Yüz Yüze', 'E-posta'];

export const SURVEY_LIST = listeTanimi({
  filtreler: {
    cariId: { tur: 'kimlik' },
    anketTuru: { tur: 'secim', degerler: SURVEY_TYPES },
    durum: { tur: 'secim', degerler: SURVEY_STATUSES },
    tarihBas: { tur: 'tarih' },
    tarihBit: { tur: 'tarih' },
    cikisOfisi: { tur: 'metin', enFazla: 128 },
  },
  siralanabilir: ['tarih', 'puan', 'durum', 'anketTuru', 'cikisOfisi', 'sozlesmeNo'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

export const COMPLAINT_LIST = listeTanimi({
  filtreler: {
    cariId: { tur: 'kimlik' },
    ofis: { tur: 'metin', enFazla: 128 },
    yer: { tur: 'secim', degerler: COMPLAINT_PLACES },
    kanal: { tur: 'metin', enFazla: 64 },
    durum: { tur: 'secim', degerler: COMPLAINT_STATUSES },
    ara: { tur: 'metin', enFazla: 100 },
  },
  siralanabilir: [
    'tarih',
    'konu',
    'durum',
    'puan',
    'sikayetKanali',
    'cikisOfisi',
    'sozlesmeNo',
    'plaka',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

export const ASSISTANCE_LIST = listeTanimi({
  filtreler: {
    plaka: { tur: 'metin', enFazla: 20 },
    tarihBas: { tur: 'tarih' },
    tarihBit: { tur: 'tarih' },
    ara: { tur: 'metin', enFazla: 100 },
    kapandi: { tur: 'bayrak' },
    yedekLastik: { tur: 'bayrak' },
    hareketEdemiyor: { tur: 'bayrak' },
  },
  siralanabilir: ['zaman', 'plaka', 'kapandi', 'sozlesmeNo', 'sebep'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

export const LEGAL_LIST = listeTanimi({
  filtreler: {
    cariId: { tur: 'kimlik' },
    tarihBas: { tur: 'tarih' },
    tarihBit: { tur: 'tarih' },
    faturaNo: { tur: 'metin', enFazla: 64 },
    dosyaNo: { tur: 'metin', enFazla: 64 },
    ara: { tur: 'metin', enFazla: 100 },
    tur: { tur: 'secim', degerler: LEGAL_TYPES },
    durum: { tur: 'secim', degerler: LEGAL_STATUSES },
  },
  siralanabilir: ['dosyaNo', 'tarih', 'tur', 'durum', 'tutar', 'tahsilat', 'kalan', 'avukat'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

export const CRM_LIST = listeTanimi({
  filtreler: {
    tarihBas: { tur: 'tarih' },
    tarihBit: { tur: 'tarih' },
    minKira: { tur: 'tamsayi', enAz: 0, enFazla: 100_000 },
    kaynak: { tur: 'metin', enFazla: 64 },
    ofis: { tur: 'metin', enFazla: 128 },
  },
  siralanabilir: [
    'ad',
    'kiraSayisi',
    'toplamCiro',
    'ortalamaKiraBedeli',
    'ortalamaKm',
    'hizmetBedeli',
    'ilkKiraZamani',
    'sonIslem',
    'segment',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** Hukuk dışa aktarma: Blazor ucu kendi sorgu adlarını okur (`bas`/`bit`); ekrandaki süzgeç AYNEN taşınır. */
export function legalExportParameters(
  f: Readonly<Record<string, string | number | boolean | undefined>>,
): Record<string, string> {
  const names: Record<string, string> = {
    cariId: 'cariId',
    dosyaNo: 'dosyaNo',
    faturaNo: 'faturaNo',
    tarihBas: 'bas',
    tarihBit: 'bit',
    tur: 'tur',
    durum: 'durum',
    ara: 'ara',
  };
  const out: Record<string, string> = {};
  for (const [api, legacy] of Object.entries(names)) {
    const v = f[api];
    if (v !== undefined && v !== null && v !== '') out[legacy] = String(v);
  }
  return out;
}

/** Kira seçim öğesi → aranabilir seçim etiketi ("Sözleşme — Plaka — Müşteri"; TC/telefon YOK). */
export function rentalOption(r: RentalPickItem): SecimSecenegi {
  return {
    id: r.id,
    etiket: [r.sozlesmeNo, r.plaka, r.musteriAd].filter((x) => !!x && x !== '').join(' — '),
  };
}

/** Kayıttaki bağlı kira → seçim değeri (sözleşme no + varsa plaka). */
export function linkedRental(
  id: string | null,
  no: string | null,
  plate?: string | null,
): SecimSecenegi | null {
  if (!id) return null;
  return { id, etiket: [no ?? id, plate].filter((x) => !!x && x !== '').join(' — ') };
}

export function linked(id: string | null, label: string | null): SecimSecenegi | null {
  return id ? { id, etiket: label ?? id } : null;
}
