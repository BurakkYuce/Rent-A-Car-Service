import { FormControl, FormGroup, Validators, type AbstractControl } from '@angular/forms';
import type { Schema } from '@core/api/ui-tipleri';
import { mergeMoment, bugun, addDays } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

/** F5.1 rezervasyon uçlarının sözleşme tipleri (`docs/api/ui-v1.json`'dan üretilen şemaların takma adları). */
export type ReservationListRow = Schema<'RezervasyonListeSatiri'>;
export type ReservationDto = Schema<'RezervasyonDto'>;
export type ReservationDetailResponse = Schema<'RezervasyonDetayYaniti'>;
export type ReservationRequest = Schema<'RezervasyonIstegi'>;
export type UpdateReservationRequest = Schema<'RezervasyonGuncelleIstegi'>;
export type CreateReservationResponse = Schema<'RezervasyonOlusturYaniti'>;
export type ConvertToRentalResponse = Schema<'KirayaCevirYaniti'>;
export type ReservationFormOptions = Schema<'RezervasyonFormSecenekleri'>;

export const RESERVATION_ROOT = '/api/ui/v1/rezervasyonlar';

/** Sunucu enum ADLARI (`ReservationStatus`) — API tanımsız adı 400'ler. */
export const RESERVATION_STATUSES = ['Rezerv', 'Onayli', 'KirayaCevrildi', 'Iptal'] as const;
export type ReservationStatus = (typeof RESERVATION_STATUSES)[number];

export function isReservationStatus(d: string): d is ReservationStatus {
  return (RESERVATION_STATUSES as readonly string[]).includes(d);
}

const STATUS_BADGE: Readonly<Record<ReservationStatus, string>> = {
  Rezerv: 'rc-rozet--uyari',
  Onayli: 'rc-rozet--bilgi',
  KirayaCevrildi: 'rc-rozet--basari',
  Iptal: 'rc-rozet--hata',
};

export function statusBadge(status: string): string {
  return `rc-rozet ${isReservationStatus(status) ? STATUS_BADGE[status] : ''}`.trim();
}

/** Para alanı değeri: `rc-para-girdisi` invariant metin (`"1234.56"`) ya da sunucudan gelen JSON sayısı. */
type Money = string | number | null;

/** Brokerden gelen bilgi (FAZ 4.5; fiyata/deftere GİRMEZ) — Blazor `BrokerBilgisiPanel` sırası. */
export const OTA_FIELDS = [
  'otaKiraBedeli',
  'otaDropBedeli',
  'otaBebekKoltugu',
  'otaNavigasyon',
  'otaLcf',
  'otaCdw',
  'otaScdw',
  'otaEkSurucu',
] as const;

/** Ödeme/komisyon (bilgi; deftere/bakiyeye yansımaz) — Blazor oluştur formu sırası. */
export const PAYMENT_MONEY_FIELDS = [
  'provizyon',
  'depozito',
  'komisyonTutar',
  'dropUcreti',
] as const;

const money = () => new FormControl<Money>(null);
const metin = (maximum: number) =>
  new FormControl<string | null>(null, Validators.maxLength(maximum));
const secim = (required = false) =>
  new FormControl<SecimSecenegi | null>(null, required ? Validators.required : []);
const oran = () => new FormControl<number | null>(null, [Validators.min(0), Validators.max(100)]);

/**
 * Rezervasyon formu — alan kümesi `RezervasyonIstegi` ile BİREBİR (sunucu whitelist'i). Oluştur ve düzenle
 * aynı formu kullanır: PUT tam değiştirmedir, gövdede olmayan alan boş yazılır; bu yüzden düzenlemede de
 * TÜM alanlar formda ve sunucu değeriyle dolu gider (Blazor düzenle formunda olmayan ofis/ödeme alanları dahil).
 */
export function createReservationForm() {
  return new FormGroup({
    musteri: secim(true),
    arac: secim(true),
    basTar: new FormControl<string | null>(null, Validators.required),
    bitTar: new FormControl<string | null>(null, Validators.required),
    gunlukUcret: money(),
    fiyatTuru: new FormControl<string | null>(null),
    kampanyaKodu: metin(64),
    cikisOfisi: secim(),
    donusOfisi: secim(),
    kaynak: secim(),
    talepTuru: metin(64),
    geldigiBirim: metin(64),
    onayKodu: metin(64),
    projeAdi: metin(128),
    kmLimit: new FormControl<number | null>(null, [Validators.min(0), Validators.max(10_000_000)]),
    fazlaKmUcret: money(),
    yakitBirimUcret: money(),
    provizyon: money(),
    depozito: money(),
    komisyonOran: oran(),
    komisyonTutar: money(),
    dropUcreti: money(),
    sonraOdeOran: oran(),
    otaKiraBedeli: money(),
    otaDropBedeli: money(),
    otaBebekKoltugu: money(),
    otaNavigasyon: money(),
    otaLcf: money(),
    otaCdw: money(),
    otaScdw: money(),
    otaEkSurucu: money(),
    aciklama: metin(1024),
  });
}

export type ReservationForm = ReturnType<typeof createReservationForm>;
export type ReservationFormValue = ReturnType<ReservationForm['getRawValue']>;
export type ReservationField = keyof ReservationFormValue;

export const DEFAULT_HOUR = '09:00';

/**
 * Yeni rezervasyonun ön tarihleri (Blazor: bugün 09:00 → +3 gün 09:00). Bugünün 09:00'u geçtiyse yarın 09:00:
 * sunucu geçmiş başlangıcı reddeder ("Rezervasyon geçmiş tarihe…"), dolu gelen formun ilk kaydı hata vermesin.
 */
export function defaultDates(now: Date = new Date()): { basTar: string; bitTar: string } {
  let day = bugun(now);
  if (Date.parse(mergeMoment(day, DEFAULT_HOUR)) <= now.getTime()) day = addDays(day, 1);
  return {
    basTar: mergeMoment(day, DEFAULT_HOUR),
    bitTar: mergeMoment(addDays(day, 3), DEFAULT_HOUR),
  };
}

/** Sunucu sayısı (`number | string`) → form sayısı (yalnız tamsayı/oran alanları; para metin kalır). */
export function toNumber(v: number | string | null | undefined): number | null {
  if (typeof v === 'number') return Number.isFinite(v) ? v : null;
  if (typeof v === 'string' && v.trim() !== '') {
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

/** Ad → seçim değeri (ofis/kaynak sunucuda METİN tutulur; kimlik = ad). */
export function nameOption(name: string | null | undefined): SecimSecenegi | null {
  const d = name?.trim() ?? '';
  return d === '' ? null : { id: d, etiket: d };
}

/** Fiyat türü ön-seçimi (Blazor A5-B2): kayıt boş ama kampanya kodu doluysa "Otomatik" (kod yalnız Otomatik'te). */
function priceTypeValue(r: ReservationDto): string | null {
  if (r.fiyatTuru) return r.fiyatTuru;
  return r.kampanyaKodu ? 'Otomatik' : null;
}

/** Detay yanıtı → form değerleri (düzenleme; temiz forma `reset`, kirli forma `sunucuDegerleriniBirlestir`). */
export function valuesFromDetail(d: ReservationDetailResponse): ReservationFormValue {
  const r = d.rezervasyon;
  return {
    musteri: { id: r.musteriId, etiket: d.musteriAd },
    arac: { id: r.vehicleId, etiket: d.plaka },
    basTar: r.basTar,
    bitTar: r.bitTar,
    gunlukUcret: r.gunlukUcret,
    fiyatTuru: priceTypeValue(r),
    kampanyaKodu: r.kampanyaKodu,
    cikisOfisi: nameOption(r.cikisOfisi),
    donusOfisi: nameOption(r.donusOfisi),
    kaynak: nameOption(r.kaynak),
    talepTuru: r.talepTuru,
    geldigiBirim: r.geldigiBirim,
    onayKodu: r.onayKodu,
    projeAdi: r.projeAdi,
    kmLimit: toNumber(r.kmLimit),
    fazlaKmUcret: r.fazlaKmUcret,
    yakitBirimUcret: r.yakitBirimUcret,
    provizyon: r.provizyon,
    depozito: r.depozito,
    komisyonOran: toNumber(r.komisyonOran),
    komisyonTutar: r.komisyonTutar,
    dropUcreti: r.dropUcreti,
    sonraOdeOran: toNumber(r.sonraOdeOran),
    otaKiraBedeli: r.otaKiraBedeli,
    otaDropBedeli: r.otaDropBedeli,
    otaBebekKoltugu: r.otaBebekKoltugu,
    otaNavigasyon: r.otaNavigasyon,
    otaLcf: r.otaLcf,
    otaCdw: r.otaCdw,
    otaScdw: r.otaScdw,
    otaEkSurucu: r.otaEkSurucu,
    aciklama: r.aciklama,
  };
}

const isEmpty = (v: unknown) => v === null || v === undefined || v === '';
const textValue = (v: string | null): string | null => {
  const d = v?.trim() ?? '';
  return d === '' ? null : d;
};
const moneyValue = (v: Money): Money => (isEmpty(v) ? null : v);

/** Doğrulayıcının garanti ettiği zorunlu değer; yoksa programlama hatası (sessiz boş gövde YOK). */
function zorunlu<T>(v: T | null | undefined, alan: string): T {
  if (v === null || v === undefined || v === '') {
    throw new Error(`Rezervasyon formu: zorunlu alan boş gönderilemez (${alan}).`);
  }
  return v;
}

/**
 * `POST /rezervasyonlar` ve `PUT /rezervasyonlar/{id}` gövdesi — sunucu whitelist'inin TAMAMI (PUT tam
 * değiştirme). Para alanları `rc-para-girdisi`'nin invariant metni (ya da dokunulmamış sunucu sayısı) olarak
 * AYNEN gider; kayan noktaya çevrilmez. Ofis/kaynak sunucuda ad olarak tutulur (seçimin etiketi).
 */
export function reservationBody(d: ReservationFormValue): ReservationRequest {
  return {
    musteriId: zorunlu(d.musteri?.id, 'musteri'),
    vehicleId: zorunlu(d.arac?.id, 'arac'),
    basTar: zorunlu(d.basTar, 'basTar'),
    bitTar: zorunlu(d.bitTar, 'bitTar'),
    gunlukUcret: moneyValue(d.gunlukUcret),
    fiyatTuru: textValue(d.fiyatTuru),
    kampanyaKodu: textValue(d.kampanyaKodu),
    cikisOfisi: d.cikisOfisi?.etiket ?? null,
    donusOfisi: d.donusOfisi?.etiket ?? null,
    kaynak: d.kaynak?.etiket ?? null,
    aciklama: textValue(d.aciklama),
    kmLimit: d.kmLimit,
    fazlaKmUcret: moneyValue(d.fazlaKmUcret),
    yakitBirimUcret: moneyValue(d.yakitBirimUcret),
    provizyon: moneyValue(d.provizyon),
    depozito: moneyValue(d.depozito),
    komisyonOran: d.komisyonOran,
    komisyonTutar: moneyValue(d.komisyonTutar),
    dropUcreti: moneyValue(d.dropUcreti),
    sonraOdeOran: d.sonraOdeOran,
    otaKiraBedeli: moneyValue(d.otaKiraBedeli),
    otaDropBedeli: moneyValue(d.otaDropBedeli),
    otaBebekKoltugu: moneyValue(d.otaBebekKoltugu),
    otaNavigasyon: moneyValue(d.otaNavigasyon),
    otaLcf: moneyValue(d.otaLcf),
    otaCdw: moneyValue(d.otaCdw),
    otaScdw: moneyValue(d.otaScdw),
    otaEkSurucu: moneyValue(d.otaEkSurucu),
    talepTuru: textValue(d.talepTuru),
    geldigiBirim: textValue(d.geldigiBirim),
    onayKodu: textValue(d.onayKodu),
    projeAdi: textValue(d.projeAdi),
  };
}

/** Karşılaştırma anahtarı: müşteri/araç kimlikle, ofis/kaynak adla, para sayısal değerle ("1200" = 1200.00). */
function valueKey(v: unknown): string {
  if (isEmpty(v)) return 'null';
  if (typeof v === 'object' && v !== null && 'id' in v) {
    const s = v as SecimSecenegi;
    return `s:${s.id}|${s.etiket}`;
  }
  if (typeof v === 'number' || (typeof v === 'string' && /^-?\d+(\.\d+)?$/.test(v))) {
    return `n:${Number(v)}`;
  }
  return JSON.stringify(v);
}

/** Ofis ve kaynak için kimlik değil ad önemlidir (seçim ucu kimlik verir, sunucu adı saklar). */
const COMPARE_BY_NAME: ReadonlySet<ReservationField> = new Set([
  'cikisOfisi',
  'donusOfisi',
  'kaynak',
]);

function anahtar(name: ReservationField, v: unknown): string {
  if (COMPARE_BY_NAME.has(name) && typeof v === 'object' && v !== null && 'etiket' in v) {
    return `a:${(v as SecimSecenegi).etiket}`;
  }
  if ((name === 'musteri' || name === 'arac') && typeof v === 'object' && v !== null && 'id' in v) {
    return `k:${(v as SecimSecenegi).id}`;
  }
  return valueKey(v);
}

/**
 * Sunucunun güncel değerlerini KİRLİ formla birleştirir (409 `cakisma` / sekmeye dönüş — F4.3 dersi): kullanıcının
 * DOKUNMADIĞI alan sunucu değerine çekilir (başka oturumun yazdığı değer bayat tam değiştirmeyle geri alınmasın),
 * dokunduğu alan KORUNUR. Dönüş: kullanıcının dokunduğu VE sunucuda önceki okumadan beri değişmiş alanlar.
 */
export function mergeServerValues(
  form: ReservationForm,
  newItem: ReservationFormValue,
  previous: ReservationFormValue | null,
): ReservationField[] {
  const conflicting: ReservationField[] = [];
  for (const name of Object.keys(newItem) as ReservationField[]) {
    const check = form.controls[name] as AbstractControl<unknown>;
    if (check.dirty) {
      if (previous && anahtar(name, newItem[name]) !== anahtar(name, previous[name]))
        conflicting.push(name);
    } else if (anahtar(name, newItem[name]) !== anahtar(name, check.value)) {
      check.setValue(newItem[name]);
    }
  }
  return conflicting;
}

/** Sunucu seçenek listesi + kayıttaki değer (listede yoksa eklenir — eski değer kaybolmaz). */
export function optionList(
  list: readonly string[] | undefined,
  existing: string | null | undefined,
): { deger: string; etiket: string }[] {
  const values = [...(list ?? [])];
  if (existing && !values.includes(existing)) values.push(existing);
  return values.map((d) => ({ deger: d, etiket: d }));
}
