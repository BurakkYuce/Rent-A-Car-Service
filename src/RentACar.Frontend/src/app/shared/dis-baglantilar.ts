/**
 * HARİCİ PAYLAŞIM BAĞLANTILARI — `src/` içinde mutlak `https:` metnine izin verilen TEK dosya (lint istisnası
 * `eslint.config.mjs`'te bu dosyaya dar: yalnız `https://wa.me/` ve Gmail taslak kökü; başka mutlak adres yine
 * hata). Neden güvenli: bunlar API çağrısı DEĞİL, kullanıcının yeni sekmede açtığı gezinme bağlantılarıdır —
 * `HttpClient`/`ApiIstemcisi` ile kullanılmaz (dosya ikisini de içe aktaramaz), XSRF başlığı taşımaz, CSP
 * `connect-src`'yi ilgilendirmez. Blazor `rc-kira-tabs.js` `bindPaylas` davranışının birebir taşıması.
 */

/** WhatsApp "click to chat" kökü (numara + `?text=`). */
export const WHATSAPP_ROOT = 'https://wa.me/';

/** Gmail taslak (compose) kökü — Blazor `mailto` DEĞİL Gmail compose açıyordu (aynı davranış). */
export const GMAIL_DRAFT_ROOT = 'https://mail.google.com/mail/?view=cm&fs=1';

/**
 * GSM normalizasyonu (Blazor `normalizeTel`, sunucudaki eski WaLink kuralları): rakam dışı atılır;
 * `00` + ülke → önek atılır; `0…` → `9` eklenir (`05xx` → `905xx`); `5…` → `90` eklenir. Sonuç 10–15 hane
 * değilse `null` (geçersiz).
 */
export function gsmNormalize(tel: string | null | undefined): string | null {
  let d = (tel ?? '').replace(/\D/g, '');
  if (d.startsWith('00')) d = d.slice(2);
  else if (d.startsWith('0')) d = '9' + d;
  else if (d.startsWith('5')) d = '90' + d;
  return d.length >= 10 && d.length <= 15 ? d : null;
}

/** WhatsApp bağlantısı; numara geçersizse `null` (çağıran hata gösterir, bağlantı açılmaz). */
export function whatsappLink(tel: string | null | undefined, message: string): string | null {
  const d = gsmNormalize(tel);
  return d === null ? null : `${WHATSAPP_ROOT}${d}?text=${encodeURIComponent(message)}`;
}

/** Blazor e-posta denetimi: boş değil ve `@` ilk karakterden sonra. */
export function isEmailValid(email: string | null | undefined): boolean {
  const m = (email ?? '').trim();
  return m !== '' && m.indexOf('@') >= 1;
}

/** Gmail taslak bağlantısı (alıcı + konu + gövde); adres geçersizse `null`. */
export function gmailDraftLink(
  email: string | null | undefined,
  subject: string,
  body: string,
): string | null {
  if (!isEmailValid(email)) return null;
  const m = (email ?? '').trim();
  return (
    `${GMAIL_DRAFT_ROOT}&to=${encodeURIComponent(m)}` +
    `&su=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`
  );
}
