import { AbstractControl, FormArray, FormGroup } from '@angular/forms';
import { type AlanHatalari, apiHatasinaCevir } from '../api/api-hatasi';
import { trKucukHarf } from '../metin/tr-normalize';

export type { AlanHatalari } from '../api/api-hatasi';

/** Kontrol hatası anahtarı: değeri sunucunun mesaj listesi. Kullanıcı alanı değiştirince kalkar. */
export const SUNUCU_HATASI = 'sunucu';

/** Herhangi bir hatadan alan hataları (`ApiHatasi.alanlar`; ham `HttpErrorResponse` da çevrilir). */
export function alanHatalariniAl(hata: unknown): AlanHatalari | undefined {
  return apiHatasinaCevir(hata).alanlar;
}

/**
 * Sunucu alan hatalarını form kontrollerine yazar; DEĞERLERE DOKUNMAZ (form korunur). Alan adı
 * önce `esleme`den, sonra yol olarak (`Adres.Il`, `Kalemler[0].Tutar`), büyük/küçük harf duyarsız
 * çözülür (sunucu `Plaka`, form `plaka`). Karşılığı olmayan mesajlar (ör. `Idempotency-Key`) döner —
 * form düzeyinde gösterilir, yutulmaz.
 */
export function sunucuHatalariniUygula(
  kok: AbstractControl,
  alanlar: AlanHatalari | undefined,
  esleme?: Readonly<Record<string, string>>,
): string[] {
  const eslesmeyen: string[] = [];
  if (!alanlar) return eslesmeyen;
  for (const [alan, mesajlar] of Object.entries(alanlar)) {
    const kontrol = kontrolBul(kok, esleme?.[alan] ?? alan);
    if (!kontrol || kontrol === kok) {
      eslesmeyen.push(...mesajlar);
      continue;
    }
    kontrol.setErrors({ ...(kontrol.errors ?? {}), [SUNUCU_HATASI]: mesajlar });
    kontrol.markAsTouched();
  }
  return eslesmeyen;
}

/** Yeniden göndermeden önce: sunucu hatalarını kaldırır (istemci doğrulayıcıları yeniden koşar). */
export function sunucuHatalariniTemizle(kok: AbstractControl): void {
  gez(kok, (k) => {
    if (k.errors?.[SUNUCU_HATASI] !== undefined) k.updateValueAndValidity({ onlySelf: false });
  });
}

function gez(kontrol: AbstractControl, ziyaret: (k: AbstractControl) => void): void {
  if (kontrol instanceof FormGroup || kontrol instanceof FormArray) {
    for (const cocuk of Object.values(kontrol.controls as Record<string, AbstractControl>)) {
      gez(cocuk, ziyaret);
    }
  }
  ziyaret(kontrol);
}

function kontrolBul(kok: AbstractControl, yol: string): AbstractControl | null {
  const parcalar = yol
    .replace(/\[(\d+)\]/g, '.$1')
    .split('.')
    .filter((p) => p !== '');
  let gecerli: AbstractControl | null = kok;
  for (const parca of parcalar) {
    if (gecerli instanceof FormArray) {
      gecerli = /^\d+$/.test(parca) ? (gecerli.at(Number(parca)) ?? null) : null;
    } else if (gecerli instanceof FormGroup) {
      const grup: FormGroup = gecerli;
      const aranan = adKatla(parca);
      const ad = Object.keys(grup.controls).find((k) => adKatla(k) === aranan);
      gecerli = ad === undefined ? null : (grup.controls[ad] ?? null);
    } else {
      return null;
    }
    if (!gecerli) return null;
  }
  return gecerli;
}

/**
 * Alan ADI (ASCII tanımlayıcı) karşılaştırması: Türkçe küçük harf "I"yı "ı" yapar, form adı "i" ile
 * yazılır (`IslemAnahtari` ↔ `islemAnahtari`); ikisi de "i"ye katlanır.
 */
function adKatla(ad: string): string {
  return trKucukHarf(ad).replace(/ı/g, 'i');
}
