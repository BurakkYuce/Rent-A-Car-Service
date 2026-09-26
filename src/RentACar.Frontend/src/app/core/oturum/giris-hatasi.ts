import { toApiError } from '@core/api/api-hatasi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';

/**
 * Giriş hatası → kullanıcı mesajı. `dogrulama` HER ZAMAN genel metin: hangi alanın yanlış olduğu
 * söylenmez (firma/kullanıcı keşfi olmasın), sunucu ayrıntısı gösterilmez.
 */
export function loginErrorMessage(error: unknown): CeviriAnahtari {
  switch (toApiError(error).kod) {
    case 'dogrulama':
      return 'oturum.giris.hata.hatali';
    case 'cok_istek':
      return 'oturum.giris.hata.cokIstek';
    case 'kiraci_kapali':
      return 'oturum.giris.hata.kiraciKapali';
    case 'ag':
      return 'oturum.giris.hata.ag';
    default:
      return 'oturum.giris.hata.genel';
  }
}

/**
 * Girişten sonraki dönüş adresi (`returnUrl`). YALNIZ uygulama içi yol kabul edilir: `/` ile başlar,
 * `//` ya da `/\` ile başlamaz (protokol-göreli dış adres), ters eğik çizgi ve kontrol karakteri yok.
 * `/app/...` biçimi (Blazor'dan gelen tam yol) router yoluna çevrilir. Aksi halde `/`.
 * Gezinme `router.navigateByUrl` ile yapılır — dış adrese zaten gidemez; bu ikinci savunma.
 */
export function safeReturnUrl(value: unknown): string {
  if (typeof value !== 'string') return '/';
  let address = value.trim();
  if (address === '/app' || address.startsWith('/app/') || address.startsWith('/app?')) {
    address = address.slice('/app'.length) || '/';
    if (!address.startsWith('/')) address = `/${address}`;
  }
  if (!address.startsWith('/') || address.startsWith('//') || address.includes('\\')) return '/';
  for (let i = 0; i < address.length; i++) {
    const code = address.charCodeAt(i);
    if (code < 0x20 || code === 0x7f) return '/';
  }
  // Giriş sayfasına dönüş döngü olur.
  if (address === '/giris' || address.startsWith('/giris?') || address.startsWith('/giris/'))
    return '/';
  return address;
}

/** Girişten sonraki hedef: SPA rotası (router, `/app` önekisiz) ya da sunucu adresi (tam sayfa geçiş). */
export type LoginTarget =
  | { readonly tur: 'spa'; readonly yol: string }
  | { readonly tur: 'sunucu'; readonly adres: string };

const SPA_PREFIX = '/app';
/** Pilot kiracının varsayılan inişi (SPA Panel). */
export const PILOT_LANDING = '/panel';
/** Blazor giriş kapısı: GİRİŞLİ kullanıcıyı `ReturnUrl`'e (sunucunun açık yönlendirme çitinden geçirerek) gönderir. */
const SERVER_LOGIN = '/login';

function isSpaUrl(address: string): boolean {
  return (
    address === SPA_PREFIX ||
    address.startsWith(`${SPA_PREFIX}/`) ||
    address.startsWith(`${SPA_PREFIX}?`) ||
    address.startsWith(`${SPA_PREFIX}#`)
  );
}

/**
 * F4.6 tek giriş — girişten (ya da girişliyken giriş sayfası açılınca) nereye gidilir. `returnUrl` bir SİTE
 * yoludur: `/app/…` yeni arayüz, diğerleri Blazor ekranı (sunucunun `/login` → `/app/giris` yönlendirmesi ve
 * `oturumGuard` böyle yazar).
 * - Pilot + (dönüş yok | `/app` dönüşü) → SPA rotası; varsayılan Panel, `/app/giris` (döngü) → Panel.
 * - Pilot DEĞİL + (dönüş yok | `/app` dönüşü) → Blazor Panel (`/`): yeni arayüz bu firmada kapalı.
 * - Blazor dönüşü (pilot olsun olmasın) → `/login?ReturnUrl=…`. Sunucu girişli kullanıcıyı
 *   `YetkiYonlendirme.GuvenliDonus` çitinden geçirir, pilotsa haritadaki SPA karşılığına çevirir. Açık
 *   yönlendirme kararı TEK yerde (sunucuda) kalır; istemci Blazor adresini kendisi açmaz.
 */
export function postLoginTarget(pilot: boolean, returnInfo: unknown): LoginTarget {
  const raw = typeof returnInfo === 'string' ? returnInfo.trim() : '';
  if (raw && raw !== '/' && !isSpaUrl(raw)) {
    if (raw.startsWith('/') && !raw.startsWith('//') && !raw.includes('\\')) {
      return { tur: 'sunucu', adres: `${SERVER_LOGIN}?ReturnUrl=${encodeURIComponent(raw)}` };
    }
    // Kök-göreli olmayan (dış adres, şema) dönüş hiç taşınmaz.
    return pilot ? { tur: 'spa', yol: PILOT_LANDING } : { tur: 'sunucu', adres: '/' };
  }
  if (!pilot) return { tur: 'sunucu', adres: '/' };
  const path = isSpaUrl(raw) ? safeReturnUrl(raw) : '/';
  return { tur: 'spa', yol: path === '/' ? PILOT_LANDING : path };
}
