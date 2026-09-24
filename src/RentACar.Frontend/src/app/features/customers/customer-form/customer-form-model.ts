import { FormArray, FormControl, FormGroup, Validators, type ValidatorFn } from '@angular/forms';

import type { GunMetni } from '@core/form/tarih-girdisi';
import { anDegeri, gunDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import { toNumber } from '@features/vehicles/vehicle-model';

import type {
  CustomerCard,
  CustomerContact,
  CustomerRequest,
  CustomerType,
  CustomerUpdateRequest,
} from '../customer-model';
import { ALL_FIELDS, MAX_CONTACTS, PRIVACY_FLAGS, type FieldSpec } from './customer-form-fields';

/**
 * Gizli (yalnız yazılır) alanların kontrolleri. Kart bunları DÖNMEZ (TC yalnız `tcKimlikVar`, ehliyet/pasaport ve
 * bireysel vergi no yalnız maskeli) → form boş açılır. PUT sözleşmesi: `null` = DEĞİŞTİRME, `""` = temizle, dolu =
 * yeni değer. "Temizle" kutusu işaretlenmeden boş bırakılan alan kayıtlı değeri KORUR.
 */
export const SECRET_FIELDS = ['tcKimlik', 'ehliyetNo', 'pasaportNo'] as const;
export type SecretField = (typeof SECRET_FIELDS)[number];

export const TYPE_FIELD = 'tip';
export const TAX_NUMBER_FIELD = 'vergiNo';
export const PASSWORD_FIELD = 'sifre';
export const CONTACTS_FIELD = 'kisiler';

export const clearFlag = (name: SecretField | 'vergiNo') => `${name}Temizle`;

export type ContactGroup = FormGroup<{
  adSoyad: FormControl<string | null>;
  telefon: FormControl<string | null>;
  mail: FormControl<string | null>;
  gorev: FormControl<string | null>;
}>;

export type CustomerFormGroup = FormGroup<
  Record<string, FormControl<unknown> | FormArray<ContactGroup>>
>;

/** Form değeri (kontrol adı → değer). Tarih = İstanbul takvim günü, para/oran = invariant metin. */
export type CustomerFormValue = Readonly<Record<string, unknown>>;

function validators(spec: FieldSpec): ValidatorFn[] {
  const list: ValidatorFn[] = [];
  if (spec.required) list.push(Validators.required);
  if (spec.maxLength) list.push(Validators.maxLength(spec.maxLength));
  if (spec.kind === 'int') list.push(Validators.min(0), Validators.max(3650));
  if (spec.inputType === 'email') list.push(Validators.email);
  return list;
}

export function contactGroup(c?: Partial<CustomerContact>): ContactGroup {
  return new FormGroup({
    adSoyad: new FormControl<string | null>(c?.adSoyad ?? null, Validators.maxLength(128)),
    telefon: new FormControl<string | null>(c?.telefon ?? null, Validators.maxLength(32)),
    mail: new FormControl<string | null>(c?.mail ?? null, [
      Validators.maxLength(256),
      Validators.email,
    ]),
    gorev: new FormControl<string | null>(c?.gorev ?? null, Validators.maxLength(64)),
  });
}

export function buildForm(): CustomerFormGroup {
  const controls: Record<string, FormControl<unknown> | FormArray<ContactGroup>> = {};
  for (const spec of ALL_FIELDS) {
    controls[spec.name] = new FormControl<unknown>(emptyValue(spec), validators(spec));
  }
  controls[TYPE_FIELD] = new FormControl<unknown>('Bireysel', Validators.required);
  controls[TAX_NUMBER_FIELD] = new FormControl<unknown>(null, Validators.maxLength(16));
  controls[clearFlag('vergiNo')] = new FormControl<unknown>(false);
  controls['tcKimlik'] = new FormControl<unknown>(null, [
    Validators.maxLength(11),
    Validators.pattern(/^\d*$/),
  ]);
  controls['ehliyetNo'] = new FormControl<unknown>(null, Validators.maxLength(32));
  controls['pasaportNo'] = new FormControl<unknown>(null, Validators.maxLength(32));
  for (const name of SECRET_FIELDS) controls[clearFlag(name)] = new FormControl<unknown>(false);
  controls[PASSWORD_FIELD] = new FormControl<unknown>(null, Validators.maxLength(128));
  for (const flag of PRIVACY_FLAGS) controls[flag] = new FormControl<unknown>(false);
  controls[CONTACTS_FIELD] = new FormArray<ContactGroup>([]);
  return new FormGroup(controls);
}

function emptyValue(spec: FieldSpec): unknown {
  if (spec.kind === 'flag') return false;
  if (spec.kind === 'int') return 0;
  if (spec.kind === 'money') return '0';
  return null;
}

/** Yeni cari: Blazor varsayılanları (Bireysel, vade 0, risk 0, bayraklar kapalı). */
export function newCustomerValue(): CustomerFormValue {
  const value: Record<string, unknown> = {};
  for (const spec of ALL_FIELDS) value[spec.name] = emptyValue(spec);
  return { ...value, ...emptySecrets(), [TYPE_FIELD]: 'Bireysel', [TAX_NUMBER_FIELD]: null };
}

function emptySecrets(): Record<string, unknown> {
  const value: Record<string, unknown> = { [PASSWORD_FIELD]: null, [clearFlag('vergiNo')]: false };
  for (const name of SECRET_FIELDS) {
    value[name] = null;
    value[clearFlag(name)] = false;
  }
  for (const flag of PRIVACY_FLAGS) value[flag] = false;
  return value;
}

function read(card: CustomerCard, name: string): unknown {
  return (card as Readonly<Record<string, unknown>>)[name];
}

/**
 * Kart → form değeri. Gizli alanlar BOŞ (kart onları taşımaz; KVKK). Tarihler İstanbul günü, tutarlar invariant metin
 * (gidiş-dönüşte kaymaz). Yetkili kişiler ayrı (`contactsOf`).
 */
export function cardToForm(card: CustomerCard): CustomerFormValue {
  const value: Record<string, unknown> = emptySecrets();
  for (const spec of ALL_FIELDS) {
    const raw = read(card, spec.name);
    switch (spec.kind) {
      case 'flag':
        value[spec.name] = raw === true;
        break;
      case 'int':
        value[spec.name] = toNumber(raw as number | string | null | undefined) ?? 0;
        break;
      case 'money':
      case 'rate':
        value[spec.name] = raw === null || raw === undefined || raw === '' ? null : String(raw);
        break;
      case 'date':
        value[spec.name] = gunDegeri(raw as string | null | undefined);
        break;
      case 'tri':
        value[spec.name] = raw === true ? 'true' : raw === false ? 'false' : null;
        break;
      default:
        value[spec.name] = typeof raw === 'string' ? raw : null;
    }
  }
  for (const flag of PRIVACY_FLAGS) value[flag] = card[flag] === true;
  value[TYPE_FIELD] = card.tip ?? 'Bireysel';
  value[TAX_NUMBER_FIELD] = card.vergiNo ?? null;
  if (value['riskLimiti'] === null) value['riskLimiti'] = '0';
  return value;
}

export function contactsOf(card: CustomerCard | null): readonly CustomerContact[] {
  return (card?.kisiler ?? []).slice(0, MAX_CONTACTS);
}

/** Kayıtlı bireysel caride vergi no yalnız maskeli döner → gizli alan kuralı (boş = koru, temizle = ""). */
export function taxNumberIsSecret(base: CustomerCard | null): boolean {
  return (base?.tip ?? null) === ('Bireysel' satisfies CustomerType);
}

/** Gizli alan gövdesi: yazılan değer > temizle (`""`) > değiştirme (`null`). */
export function secretValue(typed: unknown, clear: unknown): string | null {
  const v = typeof typed === 'string' ? typed.trim() : '';
  if (v !== '') return v;
  return clear === true ? '' : null;
}

function numberOrNull(raw: unknown): number | null {
  return typeof raw === 'number' && Number.isFinite(raw) ? raw : null;
}

function decimalOrNull(raw: unknown): string | number | null {
  if (typeof raw === 'number') return Number.isFinite(raw) ? raw : null;
  if (typeof raw === 'string' && raw.trim() !== '') return raw.trim();
  return null;
}

/**
 * Form → `POST /cariler` / `PUT /cariler/{id}` gövdesi (TAM değiştirme).
 * - Gizli numaralar `secretValue` kuralıyla; yeni caride boş = `null`.
 * - KVKK ile gizlenen grubun alanı (kart `null` döndü, kontrol kilitli) `null` gider → sunucu KORUR.
 * - Dokunulmayan tarih sunucunun anıyla gider (gün yuvarlaması yok); yeni gün İstanbul gece yarısı.
 * - Formda olmayan `islemSubeId`/`firmaId` kartın değeriyle AYNEN geri gider; PUT'ta `surum` kartınki.
 * - Yetkili kişilerde ad-soyadı boş satır atlanır (sunucu da atlar; boş satır göndermeyiz).
 */
export function formToRequest(
  v: CustomerFormValue,
  contacts: readonly Partial<CustomerContact>[],
  base: CustomerCard | null,
): CustomerRequest | CustomerUpdateRequest {
  const body: Record<string, unknown> = {};
  for (const spec of ALL_FIELDS) {
    const raw = v[spec.name];
    switch (spec.kind) {
      case 'flag':
        body[spec.name] = raw === true;
        break;
      case 'int':
        body[spec.name] = numberOrNull(raw) ?? 0;
        break;
      case 'money':
        body[spec.name] = decimalOrNull(raw) ?? '0';
        break;
      case 'rate':
        body[spec.name] = decimalOrNull(raw);
        break;
      case 'date':
        body[spec.name] = anDegeri(
          (raw as GunMetni | null) ?? null,
          base ? (read(base, spec.name) as string | null | undefined) : null,
        );
        break;
      case 'tri':
        body[spec.name] = raw === 'true' ? true : raw === 'false' ? false : null;
        break;
      default:
        body[spec.name] = metinDegeri(typeof raw === 'string' ? raw : null);
    }
  }
  for (const flag of PRIVACY_FLAGS) body[flag] = v[flag] === true;
  body[TYPE_FIELD] = typeof v[TYPE_FIELD] === 'string' ? v[TYPE_FIELD] : 'Bireysel';
  for (const name of SECRET_FIELDS) {
    body[name] = base
      ? secretValue(v[name], v[clearFlag(name)])
      : metinDegeri(typeof v[name] === 'string' ? v[name] : null);
  }
  body[TAX_NUMBER_FIELD] = taxNumberIsSecret(base)
    ? secretValue(v[TAX_NUMBER_FIELD], v[clearFlag('vergiNo')])
    : metinDegeri(typeof v[TAX_NUMBER_FIELD] === 'string' ? v[TAX_NUMBER_FIELD] : null);
  // Portal şifresi: boş = değiştirme (tek yönlü özet; asla geri okunmaz).
  body[PASSWORD_FIELD] =
    typeof v[PASSWORD_FIELD] === 'string' && v[PASSWORD_FIELD] !== '' ? v[PASSWORD_FIELD] : null;
  body[CONTACTS_FIELD] = contacts
    .map((c) => ({
      adSoyad: metinDegeri(c.adSoyad),
      telefon: metinDegeri(c.telefon),
      mail: metinDegeri(c.mail),
      gorev: metinDegeri(c.gorev),
    }))
    .filter((c) => c.adSoyad !== null);
  body['islemSubeId'] = base?.islemSubeId ?? null;
  body['firmaId'] = base?.firmaId ?? null;
  if (base) body['surum'] = base.surum ?? null;
  return body as CustomerRequest;
}

/** Kayıtlı bayrağı kaldırılan KVKK anonimleştirmeleri (kaldırma yalnız ManageUsers; sunucu 403 döner). */
export function liftedPrivacyFlags(v: CustomerFormValue, base: CustomerCard | null): string[] {
  if (!base) return [];
  return PRIVACY_FLAGS.filter((flag) => base[flag] === true && v[flag] !== true);
}
