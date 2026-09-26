import { AbstractControl, FormArray, FormGroup } from '@angular/forms';
import { type FieldErrors, toApiError } from '../api/api-hatasi';
import { trLowerCase } from '../metin/tr-normalize';

export type { FieldErrors as AlanHatalari } from '../api/api-hatasi';

/** Kontrol hatası anahtarı: değeri sunucunun mesaj listesi. Kullanıcı alanı değiştirince kalkar. */
export const SERVER_ERROR = 'sunucu';

/** Herhangi bir hatadan alan hataları (`ApiHatasi.alanlar`; ham `HttpErrorResponse` da çevrilir). */
export function getFieldErrors(error: unknown): FieldErrors | undefined {
  return toApiError(error).alanlar;
}

/**
 * Sunucu alan hatalarını form kontrollerine yazar; DEĞERLERE DOKUNMAZ (form korunur). Alan adı
 * önce `esleme`den, sonra yol olarak (`Adres.Il`, `Kalemler[0].Tutar`), büyük/küçük harf duyarsız
 * çözülür (sunucu `Plaka`, form `plaka`). Karşılığı olmayan mesajlar (ör. `Idempotency-Key`) döner —
 * form düzeyinde gösterilir, yutulmaz.
 */
export function applyServerErrors(
  root: AbstractControl,
  fields: FieldErrors | undefined,
  mapping?: Readonly<Record<string, string>>,
): string[] {
  const unmatched: string[] = [];
  if (!fields) return unmatched;
  for (const [alan, messages] of Object.entries(fields)) {
    const check = findControl(root, mapping?.[alan] ?? alan);
    if (!check || check === root) {
      unmatched.push(...messages);
      continue;
    }
    check.setErrors({ ...(check.errors ?? {}), [SERVER_ERROR]: messages });
    check.markAsTouched();
  }
  return unmatched;
}

/** Yeniden göndermeden önce: sunucu hatalarını kaldırır (istemci doğrulayıcıları yeniden koşar). */
export function clearServerErrors(root: AbstractControl): void {
  gez(root, (k) => {
    if (k.errors?.[SERVER_ERROR] !== undefined) k.updateValueAndValidity({ onlySelf: false });
  });
}

function gez(check: AbstractControl, visit: (k: AbstractControl) => void): void {
  if (check instanceof FormGroup || check instanceof FormArray) {
    for (const child of Object.values(check.controls as Record<string, AbstractControl>)) {
      gez(child, visit);
    }
  }
  visit(check);
}

function findControl(root: AbstractControl, path: string): AbstractControl | null {
  const parts = path
    .replace(/\[(\d+)\]/g, '.$1')
    .split('.')
    .filter((p) => p !== '');
  let valid: AbstractControl | null = root;
  for (const part of parts) {
    if (valid instanceof FormArray) {
      valid = /^\d+$/.test(part) ? (valid.at(Number(part)) ?? null) : null;
    } else if (valid instanceof FormGroup) {
      const group: FormGroup = valid;
      const searched = foldName(part);
      const name = Object.keys(group.controls).find((k) => foldName(k) === searched);
      valid = name === undefined ? null : (group.controls[name] ?? null);
    } else {
      return null;
    }
    if (!valid) return null;
  }
  return valid;
}

/**
 * Alan ADI (ASCII tanımlayıcı) karşılaştırması: Türkçe küçük harf "I"yı "ı" yapar, form adı "i" ile
 * yazılır (`IslemAnahtari` ↔ `islemAnahtari`); ikisi de "i"ye katlanır.
 */
function foldName(name: string): string {
  return trLowerCase(name).replace(/ı/g, 'i');
}
