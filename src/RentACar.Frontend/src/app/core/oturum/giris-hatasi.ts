import { apiHatasinaCevir } from '@core/api/api-hatasi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';

/**
 * Giriş hatası → kullanıcı mesajı. `dogrulama` HER ZAMAN genel metin: hangi alanın yanlış olduğu
 * söylenmez (firma/kullanıcı keşfi olmasın), sunucu ayrıntısı gösterilmez.
 */
export function girisHataMesaji(hata: unknown): CeviriAnahtari {
  switch (apiHatasinaCevir(hata).kod) {
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
export function guvenliDonusAdresi(deger: unknown): string {
  if (typeof deger !== 'string') return '/';
  let adres = deger.trim();
  if (adres === '/app' || adres.startsWith('/app/') || adres.startsWith('/app?')) {
    adres = adres.slice('/app'.length) || '/';
    if (!adres.startsWith('/')) adres = `/${adres}`;
  }
  if (!adres.startsWith('/') || adres.startsWith('//') || adres.includes('\\')) return '/';
  for (let i = 0; i < adres.length; i++) {
    const kod = adres.charCodeAt(i);
    if (kod < 0x20 || kod === 0x7f) return '/';
  }
  // Giriş sayfasına dönüş döngü olur.
  if (adres === '/giris' || adres.startsWith('/giris?') || adres.startsWith('/giris/')) return '/';
  return adres;
}

/** Girişten sonraki hedef: SPA rotası (router, `/app` önekisiz) ya da sunucu adresi (tam sayfa geçiş). */
export type GirisHedefi =
  | { readonly tur: 'spa'; readonly yol: string }
  | { readonly tur: 'sunucu'; readonly adres: string };

const SPA_ONEKI = '/app';
/** Pilot kiracının varsayılan inişi (SPA Panel). */
export const PILOT_INIS = '/panel';
/** Blazor giriş kapısı: GİRİŞLİ kullanıcıyı `ReturnUrl`'e (sunucunun açık yönlendirme çitinden geçirerek) gönderir. */
const SUNUCU_GIRIS = '/login';

function spaAdresiMi(adres: string): boolean {
  return (
    adres === SPA_ONEKI ||
    adres.startsWith(`${SPA_ONEKI}/`) ||
    adres.startsWith(`${SPA_ONEKI}?`) ||
    adres.startsWith(`${SPA_ONEKI}#`)
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
export function girisSonrasiHedef(pilot: boolean, donus: unknown): GirisHedefi {
  const ham = typeof donus === 'string' ? donus.trim() : '';
  if (ham && ham !== '/' && !spaAdresiMi(ham)) {
    if (ham.startsWith('/') && !ham.startsWith('//') && !ham.includes('\\')) {
      return { tur: 'sunucu', adres: `${SUNUCU_GIRIS}?ReturnUrl=${encodeURIComponent(ham)}` };
    }
    // Kök-göreli olmayan (dış adres, şema) dönüş hiç taşınmaz.
    return pilot ? { tur: 'spa', yol: PILOT_INIS } : { tur: 'sunucu', adres: '/' };
  }
  if (!pilot) return { tur: 'sunucu', adres: '/' };
  const yol = spaAdresiMi(ham) ? guvenliDonusAdresi(ham) : '/';
  return { tur: 'spa', yol: yol === '/' ? PILOT_INIS : yol };
}
