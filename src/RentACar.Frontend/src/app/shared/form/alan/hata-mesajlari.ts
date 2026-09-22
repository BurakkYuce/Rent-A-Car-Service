import type { ValidationErrors } from '@angular/forms';
import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';

type Cevir = (anahtar: CeviriAnahtari, parametreler?: Record<string, unknown>) => string;

/**
 * Doğrulayıcı hatası → Türkçe mesaj. Sıra önemli: sunucu mesajı en üstte (sunucu son sözü söyler),
 * sonra ayrıştırma hataları (yazılan metin anlaşılmadıysa "zorunlu" demek yanıltır), sonra
 * yerleşik doğrulayıcılar. Özel doğrulayıcı `{ anahtar: { mesaj: '…' } }` ile kendi metnini verir.
 */
export function hataMesajlari(hatalar: ValidationErrors | null, cevir: Cevir): string[] {
  if (!hatalar) return [];
  const sunucu: unknown = hatalar[SUNUCU_HATASI];
  if (Array.isArray(sunucu)) {
    const mesajlar = sunucu.filter((m): m is string => typeof m === 'string');
    if (mesajlar.length > 0) return mesajlar;
  }

  const sirali: [string, (deger: unknown) => string][] = [
    ['paraGecersiz', () => cevir('form.hata.para')],
    ['paraFazlaHane', (d) => cevir('form.hata.paraFazlaHane', { sayi: sayiAl(d, 'hane') })],
    ['sayiGecersiz', () => cevir('form.hata.sayi')],
    ['tarihGecersiz', () => cevir('form.hata.tarih')],
    ['saatGecersiz', () => cevir('form.hata.saat')],
    ['tarihSirasi', () => cevir('form.hata.tarihSirasi')],
    ['tarihAralikDisi', () => cevir('form.hata.tarihAralikDisi')],
    ['required', () => cevir('form.hata.zorunlu')],
    ['minlength', (d) => cevir('form.hata.enAzUzunluk', { sayi: sayiAl(d, 'requiredLength') })],
    ['maxlength', (d) => cevir('form.hata.enCokUzunluk', { sayi: sayiAl(d, 'requiredLength') })],
    ['min', (d) => cevir('form.hata.enAz', { sayi: sayiAl(d, 'min') })],
    ['max', (d) => cevir('form.hata.enCok', { sayi: sayiAl(d, 'max') })],
    ['email', () => cevir('form.hata.eposta')],
    ['pattern', () => cevir('form.hata.desen')],
  ];
  for (const [anahtar, mesaj] of sirali) {
    if (anahtar in hatalar) {
      // `requiredTrue` da `required` anahtarıyla gelir; onay kutusunda değer `false`'tur.
      return [mesaj(hatalar[anahtar])];
    }
  }
  for (const deger of Object.values(hatalar)) {
    if (typeof deger === 'object' && deger !== null && 'mesaj' in deger) {
      const mesaj = (deger as { mesaj: unknown }).mesaj;
      if (typeof mesaj === 'string') return [mesaj];
    }
  }
  return [cevir('form.hata.gecersiz')];
}

function sayiAl(deger: unknown, alan: string): unknown {
  return typeof deger === 'object' && deger !== null
    ? (deger as Record<string, unknown>)[alan]
    : undefined;
}
