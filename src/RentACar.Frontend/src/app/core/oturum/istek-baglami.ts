import { HttpContext, HttpContextToken } from '@angular/common/http';

/**
 * İstek başına davranış bayrakları (`oturumInterceptor` okur). Özellik kodu bunları
 * {@link istekBaglami} ile kurar ve `ApiIstemcisi` çağrısının `context` seçeneğine verir.
 */

/** Genel toast/bant gösterilmez; hatayı çağıran kendisi gösterir (giriş formu, arka plan yoklaması). */
export const SESSIZ = new HttpContextToken<boolean>(() => false);

/**
 * `oturum_yok` → yeniden giriş diyaloğu AÇILMAZ, hata çağırana gider. Oturum uçları (`ben`, `giris`,
 * `xsrf`, `cikis`) ve istemci hata raporu bunu taşır; yoksa diyaloğun kendi isteği diyaloğu açardı.
 */
export const YENIDEN_GIRIS_YOK = new HttpContextToken<boolean>(() => false);

/**
 * `mukerrer` (409) gelince çağrılır: sayfa kaydı YENİDEN YÜKLER. İstek yeni anahtarla tekrar
 * GÖNDERİLMEZ (idempotency envanteri, LOW-A: "farklı içerik" → kontrol et, yeniden gönderme).
 */
export const MUKERRERDE_YENILE = new HttpContextToken<(() => void) | null>(() => null);

/** İç: istek yeniden girişten sonra tekrarlandı (ikinci `oturum_yok`'ta döngü olmasın). */
export const TEKRARLANDI = new HttpContextToken<boolean>(() => false);

/** İç: XSRF belirteci yenilenip istek bir kez tekrarlandı (ikinci `xsrf_gecersiz` çağırana gider). */
export const XSRF_YENILENDI = new HttpContextToken<boolean>(() => false);

export interface IstekBaglamiSecenekleri {
  readonly sessiz?: boolean;
  readonly yenidenGirisYok?: boolean;
  readonly mukerrerdeYenile?: () => void;
}

/** Tipli `HttpContext` kurucusu: `api.post(yol, govde, { context: istekBaglami({ sessiz: true }) })`. */
export function istekBaglami(
  secenek: IstekBaglamiSecenekleri,
  taban = new HttpContext(),
): HttpContext {
  let baglam = taban;
  if (secenek.sessiz) baglam = baglam.set(SESSIZ, true);
  if (secenek.yenidenGirisYok) baglam = baglam.set(YENIDEN_GIRIS_YOK, true);
  if (secenek.mukerrerdeYenile) baglam = baglam.set(MUKERRERDE_YENILE, secenek.mukerrerdeYenile);
  return baglam;
}
