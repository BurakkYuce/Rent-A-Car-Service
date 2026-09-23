import type { AbstractControl, FormGroup } from '@angular/forms';

import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import { type GunMetni, anBirlestir, anParcala } from '@core/form/tarih-girdisi';

/**
 * F5.2b ekranlarının (rez şartları, filo kiralama) ortak saf form yardımcıları. Çekirdeğe konmadı:
 * davranış kira formunun (`kira-formu-durumu`) birleştirme kuralının küçük bir kopyasıdır ve yalnız bu
 * iki ekranda kullanılır; çekirdek dosyaları paralel PR'larda değişmesin diye buradadır.
 */

/** Sunucu anı → İstanbul takvim günü (gün seçicinin değeri). */
export function gunDegeri(an: string | null | undefined): GunMetni | null {
  return an ? (anParcala(an)?.gun ?? null) : null;
}

/**
 * Gün seçicinin değeri → API anı. Gün sunucudaki anın İstanbul günüyle AYNIYSA sunucunun anı AYNEN geri
 * gider (dokunulmayan tarih yuvarlanıp kaymaz — kira formu provizyon tarihi dersi; ayrıca belge tarihi
 * sınırı yalnız DEĞİŞEN tarihe uygulanır). Yeni gün İstanbul gece yarısıdır (UTC anı).
 */
export function anDegeri(
  gun: GunMetni | null,
  orijinalAn: string | null | undefined,
): string | null {
  if (gun === null) return null;
  if (orijinalAn && gunDegeri(orijinalAn) === gun) return orijinalAn;
  return anBirlestir(gun, '00:00');
}

/** Boş/boşluk → `null`, aksi kırpılmış metin. */
export function metinDegeri(s: string | null | undefined): string | null {
  const k = s?.trim() ?? '';
  return k === '' ? null : k;
}

/** Seçim öğesi (`{ id, etiket, … }`) kimliğiyle, diğerleri değerle karşılaştırılır. */
function anahtar(d: unknown): string {
  if (typeof d === 'object' && d !== null && 'id' in d) return `#${String(d.id)}`;
  return JSON.stringify(d ?? null);
}

const esit = (a: unknown, b: unknown) => anahtar(a) === anahtar(b);

/**
 * Bayat sürüm (409 `cakisma`) ya da işlem sonrası güncel kayıt KİRLİ forma birleştirilir: kullanıcının
 * DOKUNMADIĞI alan sunucu değerine çekilir, dokunduğu alan korunur; ikisi de değiştiyse (sunucu değeri
 * tabandan farklı) alan işaretlenir. Form ASLA silinmez. Dönen: çakışan alan adları.
 */
export function sunucuDegerleriniBirlestir(
  form: FormGroup,
  yeni: Readonly<Record<string, unknown>>,
  taban: Readonly<Record<string, unknown>>,
  cakismaMesaji: string,
): string[] {
  const cakisan: string[] = [];
  for (const [ad, kontrol] of Object.entries(form.controls) as [string, AbstractControl][]) {
    if (!(ad in yeni)) continue;
    if (kontrol.pristine) {
      kontrol.setValue(yeni[ad], { emitEvent: false });
    } else if (!esit(yeni[ad], taban[ad]) && !esit(yeni[ad], kontrol.value)) {
      kontrol.setErrors({ ...(kontrol.errors ?? {}), [SUNUCU_HATASI]: [cakismaMesaji] });
      kontrol.markAsTouched();
      cakisan.push(ad);
    }
  }
  return cakisan;
}
