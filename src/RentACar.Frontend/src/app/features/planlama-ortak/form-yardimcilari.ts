import type { AbstractControl, FormGroup } from '@angular/forms';

import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import { type DayText, mergeMoment, parseMoment } from '@core/form/tarih-girdisi';

/**
 * F5.2b ekranlarının (rez şartları, filo kiralama) ortak saf form yardımcıları. Çekirdeğe konmadı:
 * davranış kira formunun (`kira-formu-durumu`) birleştirme kuralının küçük bir kopyasıdır ve yalnız bu
 * iki ekranda kullanılır; çekirdek dosyaları paralel PR'larda değişmesin diye buradadır.
 */

/** Sunucu anı → İstanbul takvim günü (gün seçicinin değeri). */
export function dayValue(an: string | null | undefined): DayText | null {
  return an ? (parseMoment(an)?.gun ?? null) : null;
}

/**
 * Gün seçicinin değeri → API anı. Gün sunucudaki anın İstanbul günüyle AYNIYSA sunucunun anı AYNEN geri
 * gider (dokunulmayan tarih yuvarlanıp kaymaz — kira formu provizyon tarihi dersi; ayrıca belge tarihi
 * sınırı yalnız DEĞİŞEN tarihe uygulanır). Yeni gün İstanbul gece yarısıdır (UTC anı).
 */
export function momentValue(
  day: DayText | null,
  originalMoment: string | null | undefined,
): string | null {
  if (day === null) return null;
  if (originalMoment && dayValue(originalMoment) === day) return originalMoment;
  return mergeMoment(day, '00:00');
}

/** Boş/boşluk → `null`, aksi kırpılmış metin. */
export function textValue(s: string | null | undefined): string | null {
  const k = s?.trim() ?? '';
  return k === '' ? null : k;
}

/** Seçim öğesi (`{ id, etiket, … }`) kimliğiyle, diğerleri değerle karşılaştırılır. */
function anahtar(d: unknown): string {
  if (typeof d === 'object' && d !== null && 'id' in d) return `#${String(d.id)}`;
  return JSON.stringify(d ?? null);
}

const equal = (a: unknown, b: unknown) => anahtar(a) === anahtar(b);

/**
 * Bayat sürüm (409 `cakisma`) ya da işlem sonrası güncel kayıt KİRLİ forma birleştirilir: kullanıcının
 * DOKUNMADIĞI alan sunucu değerine çekilir, dokunduğu alan korunur; ikisi de değiştiyse (sunucu değeri
 * tabandan farklı) alan işaretlenir. Form ASLA silinmez. Dönen: çakışan alan adları.
 */
export function mergeServerValues(
  form: FormGroup,
  newItem: Readonly<Record<string, unknown>>,
  floor: Readonly<Record<string, unknown>>,
  conflictMessage: string,
): string[] {
  const conflicting: string[] = [];
  for (const [name, check] of Object.entries(form.controls) as [string, AbstractControl][]) {
    if (!(name in newItem)) continue;
    if (check.pristine) {
      check.setValue(newItem[name], { emitEvent: false });
    } else if (!equal(newItem[name], floor[name]) && !equal(newItem[name], check.value)) {
      check.setErrors({ ...(check.errors ?? {}), [SERVER_ERROR]: [conflictMessage] });
      check.markAsTouched();
      conflicting.push(name);
    }
  }
  return conflicting;
}
