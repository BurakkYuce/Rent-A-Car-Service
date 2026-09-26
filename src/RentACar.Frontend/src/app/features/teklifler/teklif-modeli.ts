import {
  FormControl,
  FormGroup,
  Validators,
  type AbstractControl,
  type ValidationErrors,
  type ValidatorFn,
} from '@angular/forms';
import type { Schema } from '@core/api/ui-tipleri';
import {
  mergeMoment,
  parseMoment,
  formatDay,
  addDays,
  type DayText,
} from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

/** F5.1 teklif uçlarının sözleşme tipleri. */
export type QuotationListRow = Schema<'TeklifListeSatiri'>;
export type QuotationDto = Schema<'TeklifDto'>;
export type QuotationDetailResponse = Schema<'TeklifDetayYaniti'>;
export type QuotationRequest = Schema<'TeklifIstegi'>;
export type CreateQuotationResponse = Schema<'TeklifOlusturYaniti'>;
export type QuotationAcceptResponse = Schema<'TeklifKabulYaniti'>;

export const QUOTATION_ROOT = '/api/ui/v1/teklifler';

/** Sunucu enum ADLARI (`QuotationStatus`: Taslak → Gonderildi → Kabul/Red). */
export const QUOTATION_STATUSES = ['Taslak', 'Gonderildi', 'Kabul', 'Red'] as const;
export type QuotationStatus = (typeof QUOTATION_STATUSES)[number];

export function isQuotationStatus(d: string): d is QuotationStatus {
  return (QUOTATION_STATUSES as readonly string[]).includes(d);
}

const BADGE: Readonly<Record<QuotationStatus, string>> = {
  Taslak: '',
  Gonderildi: 'rc-rozet--bilgi',
  Kabul: 'rc-rozet--basari',
  Red: 'rc-rozet--hata',
};

export function quotationBadge(status: string): string {
  return `rc-rozet ${isQuotationStatus(status) ? BADGE[status] : ''}`.trim();
}

/** Açık teklif (kabul/red edilebilir) — sunucunun `yetkiler` kuralıyla aynı; liste satırında yetki alanı yok. */
export function isQuotationOpen(status: string): boolean {
  return status === 'Taslak' || status === 'Gonderildi';
}

/**
 * Yeni teklif formu — Blazor QuotationList "+ Yeni Teklif" alanları birebir (müşteri, araç, başlangıç, bitiş, fiyat
 * türü, günlük ücret, geçerlilik, çıkış/dönüş ofisi). Blazor'da teklif düzenleme yok → sunucuda PUT yok.
 */
export function createQuotationForm() {
  return new FormGroup({
    musteri: new FormControl<SecimSecenegi | null>(null, Validators.required),
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    basTar: new FormControl<string | null>(null, Validators.required),
    bitTar: new FormControl<string | null>(null, Validators.required),
    fiyatTuru: new FormControl<string | null>(null),
    gunlukUcret: new FormControl<string | number | null>(null),
    gecerlilik: new FormControl<DayText | null>(null),
    cikisOfisi: new FormControl<SecimSecenegi | null>(null),
    donusOfisi: new FormControl<SecimSecenegi | null>(null),
  });
}

export type QuotationForm = ReturnType<typeof createQuotationForm>;
export type QuotationFormValue = ReturnType<QuotationForm['getRawValue']>;

/**
 * Geçerlilik takvim günü → an: günün İstanbul gece yarısı (Blazor `FormParse.Date(gecerlilik)` ile aynı gün
 * başı). Sunucu kuralı: geçerlilik başlangıçtan önce olamaz.
 */
export function validityMoment(day: DayText | null): string | null {
  return day ? mergeMoment(day, '00:00') : null;
}

/**
 * Sunucunun kabul edeceği en erken geçerlilik günü: geçerlilik günün İstanbul gece yarısı olarak gittiği için
 * başlangıç 00:00 değilse başlangıç günü REDDEDİLİR (gece yarısı < başlangıç) → ertesi gün. Başlangıç yoksa `null`.
 */
export function validityEarliest(startDate: string | null): DayText | null {
  const p = parseMoment(startDate);
  if (!p) return null;
  return p.saat === '00:00' ? p.gun : addDays(p.gun, 1);
}

/**
 * İstemci doğrulaması — sunucu kuralının (`GecerlilikTarihi < BasTar` → red) birebir aynısı, aynı anlarla
 * karşılaştırır; istek gitmeden alanın altında açıklayıcı mesaj verir. Kardeş `basTar` kontrolünü okur (başlangıç
 * değişince çağıran `updateValueAndValidity` yapar). Mesaj çağırandan (çeviri).
 */
export function validityValidator(message: (earliest: string) => string): ValidatorFn {
  return (k: AbstractControl): ValidationErrors | null => {
    const day = k.value as DayText | null;
    const start = k.parent?.get('basTar')?.value as string | null | undefined;
    const an = validityMoment(day);
    if (!an || !start) return null;
    if (Date.parse(an) >= Date.parse(start)) return null;
    const earliest = validityEarliest(start);
    return { gecerlilikErken: { mesaj: message(earliest ? formatDay(earliest) : '') } };
  };
}

/** Doğrulayıcının garanti ettiği zorunlu değer; yoksa programlama hatası. */
function zorunlu<T>(v: T | null | undefined, alan: string): T {
  if (v === null || v === undefined || v === '') {
    throw new Error(`Teklif formu: zorunlu alan boş gönderilemez (${alan}).`);
  }
  return v;
}

/** `POST /teklifler` gövdesi. Günlük ücret boş → tarife (fiyat motoru); para invariant metin AYNEN. */
export function quotationBody(d: QuotationFormValue): QuotationRequest {
  const fee = d.gunlukUcret;
  return {
    musteriId: zorunlu(d.musteri?.id, 'musteri'),
    vehicleId: zorunlu(d.arac?.id, 'arac'),
    basTar: zorunlu(d.basTar, 'basTar'),
    bitTar: zorunlu(d.bitTar, 'bitTar'),
    gunlukUcret: fee === '' ? null : fee,
    fiyatTuru: d.fiyatTuru?.trim() ? d.fiyatTuru.trim() : null,
    cikisOfisi: d.cikisOfisi?.etiket ?? null,
    donusOfisi: d.donusOfisi?.etiket ?? null,
    gecerlilikTarihi: validityMoment(d.gecerlilik),
  };
}
