import type { AbstractControl, FormGroup } from '@angular/forms';

import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';

type Values = Readonly<Record<string, unknown>>;

/** Değer eşitliği (JSON; `null`/`undefined` aynı) — `rc-tanim-crud` birleştirmesiyle aynı kural. */
export function sameValue(a: unknown, b: unknown): boolean {
  return JSON.stringify(a ?? null) === JSON.stringify(b ?? null);
}

/**
 * 409 `cakisma` sonrası sunucunun güncel kaydını KİRLİ forma birleştirir (DEVIR §5 "kalıcı sekme"):
 * - kullanıcının dokunmadığı alan sunucu değerine çekilir;
 * - dokunulan alan korunur;
 * - hem kullanıcı hem başka oturum değiştirdiyse (sunucu değeri tabandan da formdan da farklı) alan işaretlenir.
 *
 * Yalnız `fresh`te bulunan anahtarlar birleşir: yazılabilir-sır alanları (ör. `smtpSifre`) yanıtta hiç olmadığı
 * için dokunulmaz. Form değerleri başka hiçbir dalda değişmez; yeniden gönderim yapılmaz. Dönüş: işaretlenen alanlar.
 */
export function mergeServerValues(
  form: FormGroup,
  base: Values | null,
  fresh: Values,
  message: string,
): readonly string[] {
  const flagged: string[] = [];
  const reference = base ?? fresh;
  for (const [name, control] of Object.entries(form.controls) as [string, AbstractControl][]) {
    if (!(name in fresh)) continue;
    const value = fresh[name] ?? null;
    if (control.pristine) {
      control.setValue(value, { emitEvent: false });
    } else if (!sameValue(value, reference[name]) && !sameValue(value, control.value)) {
      control.setErrors({ ...(control.errors ?? {}), [SUNUCU_HATASI]: [message] });
      control.markAsTouched();
      flagged.push(name);
    }
  }
  return flagged;
}

/**
 * Formun kontrol adları için sunucu kaydından başlangıç değeri: kayıtta olmayan ad `null`. Sayfalar formu bununla
 * doldurur (`form.reset(pickValues(form, dto))`) — `reset` formu `pristine` yapar.
 */
export function pickValues(form: FormGroup, source: Values): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const name of Object.keys(form.controls)) out[name] = source[name] ?? null;
  return out;
}
