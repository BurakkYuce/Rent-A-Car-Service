import { HttpContext, HttpContextToken } from '@angular/common/http';

/**
 * İstek başına davranış bayrakları (`oturumInterceptor` okur). Özellik kodu bunları
 * {@link requestContext} ile kurar ve `ApiIstemcisi` çağrısının `context` seçeneğine verir.
 */

/** Genel toast/bant gösterilmez; hatayı çağıran kendisi gösterir (giriş formu, arka plan yoklaması). */
export const SILENT = new HttpContextToken<boolean>(() => false);

/**
 * `oturum_yok` → yeniden giriş diyaloğu AÇILMAZ, hata çağırana gider. Oturum uçları (`ben`, `giris`,
 * `xsrf`, `cikis`) ve istemci hata raporu bunu taşır; yoksa diyaloğun kendi isteği diyaloğu açardı.
 */
export const NO_RELOGIN = new HttpContextToken<boolean>(() => false);

/**
 * `mukerrer` (409) gelince çağrılır: sayfa kaydı YENİDEN YÜKLER. İstek yeni anahtarla tekrar
 * GÖNDERİLMEZ (idempotency envanteri, LOW-A: "farklı içerik" → kontrol et, yeniden gönderme).
 */
export const REFRESH_ON_DUPLICATE = new HttpContextToken<(() => void) | null>(() => null);

/**
 * `mukerrer` toast'unun başlığı (varsayılan "Mükerrer işlem", bilgi). Verilirse toast UYARI olur ve bu başlığı
 * taşır; gövde yine sunucunun `detail`'ıdır. Deterministik anahtarı SUNUCUDA yeniden hesaplanan işlemler için
 * (F4.4a `tahsilatAnahtar`): 409 orada çoğunlukla "kayıt bu ekran açıldıktan sonra değişti" demektir ve
 * "mükerrer işlem kaydedildi" izlenimi yanıltır.
 */
export const DUPLICATE_HEADER = new HttpContextToken<string | null>(() => null);

/**
 * `mukerrer` toast'unu interceptor GÖSTERMEZ, çağıran gösterir (`MUKERRERDE_YENILE` yine çağrılır; diğer kodlar
 * genel katmanda kalır). Deterministik anahtarlı tahsilat (F4.4 M-C): mesaj isteğin bir TEKRAR olup olmadığına göre
 * değişir ve bunu yalnız çağıran bilir (`TahsilatDenemesi`, `tahsilatMukerrerBildir`).
 */
export const DUPLICATE_CALLER_SHOWS = new HttpContextToken<boolean>(() => false);

/** İç: istek yeniden girişten sonra tekrarlandı (ikinci `oturum_yok`'ta döngü olmasın). */
export const REPEATED = new HttpContextToken<boolean>(() => false);

/** İç: XSRF belirteci yenilenip istek bir kez tekrarlandı (ikinci `xsrf_gecersiz` çağırana gider). */
export const XSRF_REFRESHED = new HttpContextToken<boolean>(() => false);

export interface IstekBaglamiSecenekleri {
  readonly sessiz?: boolean;
  readonly yenidenGirisYok?: boolean;
  readonly mukerrerdeYenile?: () => void;
  /** Bkz. {@link DUPLICATE_HEADER}. */
  readonly mukerrerBasligi?: string;
  /** Bkz. {@link DUPLICATE_CALLER_SHOWS}. */
  readonly mukerrerCagiranGosterir?: boolean;
}

/** Tipli `HttpContext` kurucusu: `api.post(yol, govde, { context: istekBaglami({ sessiz: true }) })`. */
export function requestContext(
  option: IstekBaglamiSecenekleri,
  floor = new HttpContext(),
): HttpContext {
  let context = floor;
  if (option.sessiz) context = context.set(SILENT, true);
  if (option.yenidenGirisYok) context = context.set(NO_RELOGIN, true);
  if (option.mukerrerdeYenile) context = context.set(REFRESH_ON_DUPLICATE, option.mukerrerdeYenile);
  if (option.mukerrerBasligi) context = context.set(DUPLICATE_HEADER, option.mukerrerBasligi);
  if (option.mukerrerCagiranGosterir) context = context.set(DUPLICATE_CALLER_SHOWS, true);
  return context;
}
