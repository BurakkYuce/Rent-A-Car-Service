import type { ValidationErrors } from '@angular/forms';
import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';

type Translate = (key: CeviriAnahtari, parameters?: Record<string, unknown>) => string;

/**
 * Doğrulayıcı hatası → Türkçe mesaj. Sıra önemli: sunucu mesajı en üstte (sunucu son sözü söyler),
 * sonra ayrıştırma hataları (yazılan metin anlaşılmadıysa "zorunlu" demek yanıltır), sonra
 * yerleşik doğrulayıcılar. Özel doğrulayıcı `{ anahtar: { mesaj: '…' } }` ile kendi metnini verir.
 */
export function errorMessages(errors: ValidationErrors | null, translate: Translate): string[] {
  if (!errors) return [];
  const server: unknown = errors[SERVER_ERROR];
  if (Array.isArray(server)) {
    const messages = server.filter((m): m is string => typeof m === 'string');
    if (messages.length > 0) return messages;
  }

  const sorted: [string, (value: unknown) => string][] = [
    ['paraGecersiz', () => translate('form.hata.para')],
    ['paraFazlaHane', (d) => translate('form.hata.paraFazlaHane', { sayi: getNumber(d, 'hane') })],
    ['sayiGecersiz', () => translate('form.hata.sayi')],
    ['tarihGecersiz', () => translate('form.hata.tarih')],
    ['saatGecersiz', () => translate('form.hata.saat')],
    ['tarihSirasi', () => translate('form.hata.tarihSirasi')],
    ['tarihAralikDisi', () => translate('form.hata.tarihAralikDisi')],
    ['required', () => translate('form.hata.zorunlu')],
    [
      'minlength',
      (d) => translate('form.hata.enAzUzunluk', { sayi: getNumber(d, 'requiredLength') }),
    ],
    [
      'maxlength',
      (d) => translate('form.hata.enCokUzunluk', { sayi: getNumber(d, 'requiredLength') }),
    ],
    ['min', (d) => translate('form.hata.enAz', { sayi: getNumber(d, 'min') })],
    ['max', (d) => translate('form.hata.enCok', { sayi: getNumber(d, 'max') })],
    ['email', () => translate('form.hata.eposta')],
    ['pattern', () => translate('form.hata.desen')],
  ];
  for (const [key, message] of sorted) {
    if (key in errors) {
      // `requiredTrue` da `required` anahtarıyla gelir; onay kutusunda değer `false`'tur.
      return [message(errors[key])];
    }
  }
  for (const value of Object.values(errors)) {
    if (typeof value === 'object' && value !== null && 'mesaj' in value) {
      const message = (value as { mesaj: unknown }).mesaj;
      if (typeof message === 'string') return [message];
    }
  }
  return [translate('form.hata.gecersiz')];
}

function getNumber(value: unknown, alan: string): unknown {
  return typeof value === 'object' && value !== null
    ? (value as Record<string, unknown>)[alan]
    : undefined;
}
