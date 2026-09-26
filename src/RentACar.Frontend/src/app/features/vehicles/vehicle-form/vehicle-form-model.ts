import { FormControl, Validators, type ValidatorFn } from '@angular/forms';

import type { DayText } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

import { momentValue, dayValue, textValue } from '@features/planlama-ortak/form-yardimcilari';

import {
  FLEET_STATUSES,
  FUELS,
  GEARS,
  VEHICLE_STATUSES,
  toNumber,
  type VehicleCard,
  type VehicleCreateRequest,
} from '../vehicle-model';

/** Seç-veya-yaz öneri kaynağı (`/araclar/secim/*` ya da `/secim/sube`). */
export type SuggestionKind = 'marka' | 'tip' | 'renk' | 'segment' | 'sahip' | 'sube';

export type FieldKind = 'text' | 'int' | 'money' | 'rate' | 'date' | 'flag' | 'select';

export interface FieldSpec {
  /** Form kontrolü = API alanı (camelCase). */
  readonly name: string;
  readonly kind: FieldKind;
  /** Kolon sınırı (sunucu `AracGirdi.Metinler` ile aynı). */
  readonly maxLength?: number;
  readonly required?: boolean;
  readonly suggest?: SuggestionKind;
  /** `money` için gösterim para birimi (varsayılan TRY). */
  readonly currency?: 'EUR';
  /** `select` seçenekleri (enum adları) ve boş seçenek (belirtilmedi) var mı. */
  readonly options?: readonly string[];
  readonly optional?: boolean;
  /** Satırın tamamını kaplar. */
  readonly wide?: boolean;
}

export type SectionId = 'genel' | 'alim' | 'detay' | 'derinlik' | 'bayraklar';

export interface SectionSpec {
  readonly id: SectionId;
  readonly fields: readonly FieldSpec[];
}

const t = (name: string, maxLength: number, extra: Partial<FieldSpec> = {}): FieldSpec => ({
  name,
  kind: 'text',
  maxLength,
  ...extra,
});
const i = (name: string, extra: Partial<FieldSpec> = {}): FieldSpec => ({
  name,
  kind: 'int',
  ...extra,
});
const m = (name: string, extra: Partial<FieldSpec> = {}): FieldSpec => ({
  name,
  kind: 'money',
  ...extra,
});
const d = (name: string): FieldSpec => ({ name, kind: 'date' });
const f = (name: string): FieldSpec => ({ name, kind: 'flag' });

/**
 * Blazor `VehicleEdit` kartının TÜM alanları (yeni araç formu da aynı kümeyi kullanır; `AracIstegi` ile alan
 * alan aynı). Grup ayrı (tanımlı gruplardan seçim, `grupBilincliBos`). Bölümler Blazor'daki katlanır
 * gruplarla aynı: kart, alım/maliyet, detay alanları, kart derinliği, operasyon bayrakları.
 */
export const SECTIONS: readonly SectionSpec[] = [
  {
    id: 'genel',
    fields: [
      t('plaka', 16, { required: true }),
      t('marka', 64, { suggest: 'marka' }),
      t('tip', 64, { suggest: 'tip' }),
      t('segment', 64, { suggest: 'segment' }),
      t('sipp', 4),
      t('renk', 32, { suggest: 'renk' }),
      i('modelYili'),
      { name: 'vites', kind: 'select', options: GEARS, optional: true },
      t('sasiNo', 32),
      t('motorNo', 32),
      t('sube', 64, { suggest: 'sube' }),
      { name: 'durum', kind: 'select', options: VEHICLE_STATUSES, required: true },
      { name: 'filoDurum', kind: 'select', options: FLEET_STATUSES, optional: true },
      i('km', { required: true }),
      { name: 'yakit', kind: 'select', options: FUELS, optional: true },
      t('aracSahibi', 128, { suggest: 'sahip' }),
      i('motorGucu'),
      i('silindirHacmi'),
      t('ruhsatNo', 32),
      d('tescilTarihi'),
      t('hgsNo', 256),
      t('ogsNo', 256),
      t('kasaTipi', 256),
      t('detayTipi', 256),
      i('kiraKmLimiti'),
      d('sonBakimTarih'),
      i('sonBakimKm'),
      t('lastikDurumu', 256),
      i('vitrinAdet'),
      t('ozelKod1', 64),
      t('ozelKod2', 64),
      t('ozelKod3', 64),
      t('ozelKod4', 64),
      t('ozelKod5', 64),
    ],
  },
  {
    id: 'alim',
    fields: [
      m('alimBedeli'),
      d('alimTarihi'),
      m('alisVergisiz'),
      m('alisOtv'),
      m('alisKdv'),
      m('aylikMaliyet'),
      m('filoYonetimMaliyeti'),
      m('ikinciElDeger'),
      d('filoGirisTarih'),
      d('filoCikisTarih'),
      t('alimFaturaNo', 256),
      t('alimYapilanFirma', 256),
    ],
  },
  {
    id: 'detay',
    fields: [
      t('belgeNo', 256),
      t('ruhsatSahibi', 128),
      t('sozNo', 64),
      t('araciAlan', 128),
      t('odemeSekli', 64),
      t('assistanFirma', 128),
      t('hgsFirma', 128),
      i('disKmLimit'),
      t('tsbKodu', 32),
      m('tsbKaskoDegeri'),
      f('alisEuro'),
      m('alisEuroFiyat', { currency: 'EUR' }),
      m('satisEuroFiyat', { currency: 'EUR' }),
      t('pasifSebep', 256),
      t('sonDurum', 256),
      i('sonTeslimKm'),
      d('sonTeslimTarihi'),
      t('kiralayan', 128),
      i('kiraGun'),
      m('kiraFiyat'),
      d('kiraBitTar'),
      d('kiraBekTar'),
    ],
  },
  {
    id: 'derinlik',
    fields: [
      t('tsrbMarkaKodu', 32),
      t('tsrbTipKodu', 32),
      t('altGrupAdi', 64),
      t('entegrasyonKodu', 64),
      t('teypKodu', 64),
      t('takipMarka', 64),
      t('takipNo', 64),
      t('sahipGrup', 64),
      t('aracSahibiNo', 64),
      t('aracSahibi2', 128),
      t('krediFirma', 128),
      d('kapatmaTarih'),
      d('cikmasiPlananTarih'),
      i('aracSatisKm'),
      t('konum', 128),
      t('aciklama', 1024, { wide: true }),
      { name: 'alimBedeliKur', kind: 'rate' },
      { name: 'arac2FiyatKur', kind: 'rate' },
      { name: 'simdiKur', kind: 'rate' },
      m('aylikMaliyetDoviz'),
    ],
  },
  {
    id: 'bayraklar',
    fields: [
      f('webRezKapat'),
      f('ofisRezKapat'),
      f('zIzni'),
      f('utts'),
      f('karLastigi'),
      f('yedekAnahtar'),
      f('temizlik'),
      f('rehin'),
    ],
  },
];

export const ALL_FIELDS: readonly FieldSpec[] = SECTIONS.flatMap((s) => s.fields);

/** Grup kontrolünün adı (tanımlı gruplardan seçim; boş = "(Grupsuz)" bilinçli seçim). */
export const GROUP_FIELD = 'grup';

export type VehicleFormControls = Record<string, FormControl<unknown>>;

/** Form değeri (kontrol adı → değer). Tarih = İstanbul takvim günü, para = invariant metin. */
export type VehicleFormValue = Readonly<Record<string, unknown>>;

function validators(spec: FieldSpec): ValidatorFn[] {
  const list: ValidatorFn[] = [];
  if (spec.required) list.push(Validators.required);
  if (spec.maxLength) list.push(Validators.maxLength(spec.maxLength));
  if (spec.kind === 'int') list.push(Validators.min(0));
  return list;
}

/** Boş (yeni araç) form kontrolleri. Varsayılan: durum Müsait, KM 0, yakıt BELİRTİLMEDİ (Blazor PR-21). */
export function buildControls(): VehicleFormControls {
  const controls: VehicleFormControls = {};
  for (const spec of ALL_FIELDS) {
    controls[spec.name] = new FormControl<unknown>(emptyValue(spec), validators(spec));
  }
  controls[GROUP_FIELD] = new FormControl<unknown>(null);
  return controls;
}

function emptyValue(spec: FieldSpec): unknown {
  if (spec.kind === 'flag') return false;
  if (spec.name === 'durum') return 'Musait';
  if (spec.name === 'km') return 0;
  return null;
}

export function newVehicleValue(defaultGroup: string | null): VehicleFormValue {
  const value: Record<string, unknown> = {};
  for (const spec of ALL_FIELDS) value[spec.name] = emptyValue(spec);
  value[GROUP_FIELD] = groupOption(defaultGroup);
  return value;
}

/** Grup adı → seçim öğesi (kimlik adla kurulur; aynı ad aynı değer sayılır). */
export function groupOption(name: string | null | undefined): SecimSecenegi | null {
  const n = name?.trim() ?? '';
  return n === '' ? null : { id: `ad:${n}`, etiket: n };
}

function read(card: VehicleCard, name: string): unknown {
  return (card as Readonly<Record<string, unknown>>)[name];
}

/** Kart → form değeri. Tarihler İstanbul günü, tutarlar invariant metin (gidiş-dönüşte kaymaz). */
export function cardToForm(card: VehicleCard): VehicleFormValue {
  const value: Record<string, unknown> = {};
  for (const spec of ALL_FIELDS) {
    const raw = read(card, spec.name);
    switch (spec.kind) {
      case 'flag':
        value[spec.name] = raw === true;
        break;
      case 'int':
        value[spec.name] = toNumber(raw as number | string | null | undefined);
        break;
      case 'money':
      case 'rate':
        value[spec.name] = raw === null || raw === undefined || raw === '' ? null : String(raw);
        break;
      case 'date':
        value[spec.name] = dayValue(raw as string | null | undefined);
        break;
      default:
        value[spec.name] = typeof raw === 'string' ? raw : null;
    }
  }
  value[GROUP_FIELD] = groupOption(card.grup);
  return value;
}

/**
 * Form → `POST /araclar` / `PUT /araclar/{id}` gövdesi (TAM değiştirme: formda olmayan tek alan `kiraMusteriId`
 * kartın değeriyle AYNEN geri gider). Dokunulmayan tarih sunucunun anıyla gider (gün yuvarlaması yok);
 * yeni gün İstanbul gece yarısı. `alisEuro` işaretsizse kartta `null` ise `null` kalır.
 */
export function formToRequest(v: VehicleFormValue, base: VehicleCard | null): VehicleCreateRequest {
  const body: Record<string, unknown> = {};
  for (const spec of ALL_FIELDS) {
    const raw = v[spec.name];
    switch (spec.kind) {
      case 'flag':
        body[spec.name] = raw === true;
        break;
      case 'int':
        body[spec.name] = typeof raw === 'number' && Number.isFinite(raw) ? raw : null;
        break;
      case 'money':
      case 'rate':
        body[spec.name] =
          raw === null || raw === undefined || (typeof raw === 'string' && raw.trim() === '')
            ? null
            : raw;
        break;
      case 'date':
        body[spec.name] = momentValue(
          (raw as DayText | null) ?? null,
          base ? (read(base, spec.name) as string | null | undefined) : null,
        );
        break;
      default:
        body[spec.name] = textValue(typeof raw === 'string' ? raw : null);
    }
  }
  if (body['alisEuro'] === false && (base?.alisEuro ?? null) === null) body['alisEuro'] = null;
  if (body['km'] === null) body['km'] = 0;
  const group = v[GROUP_FIELD] as SecimSecenegi | null | undefined;
  body['grup'] = group?.etiket ?? null;
  body['grupBilincliBos'] = !group;
  body['kiraMusteriId'] = base?.kiraMusteriId ?? null;
  return body as VehicleCreateRequest;
}
