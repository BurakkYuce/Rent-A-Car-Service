import {
  HttpClient,
  HttpContext,
  HttpErrorResponse,
  HttpEvent,
  HttpInterceptorFn,
  HttpRequest,
  HttpXsrfTokenExtractor,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, Observable, switchMap, tap, throwError } from 'rxjs';

import { type ApiHatasiBilgisi, toApiError } from '@core/api/api-hatasi';
import {
  API_ROOT,
  EXPECTED_SESSION_HEADER,
  IDEMPOTENCY_HEADER,
  XSRF_HEADER,
} from '@core/api/api-istemcisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';

import {
  requestContext,
  REFRESH_ON_DUPLICATE,
  DUPLICATE_HEADER,
  DUPLICATE_CALLER_SHOWS,
  SILENT,
  REPEATED,
  XSRF_REFRESHED,
  NO_RELOGIN,
} from './request-context';
import { identityOfSession } from './oturum-baglami';
import { SessionService } from './session-service';
import { ReloginService } from './relogin-service';

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);

/**
 * `X-Beklenen-Oturum` HİÇ eklenmeyen ilk yol SEGMENTLERİ (#417 I3): oturum uçları (giriş/çıkış/`ben`/xsrf — sunucu
 * filtresi bunlara da uygulanır; başlık girişi ve yeniden girişi kırardı) ve platform konsolu (kiracı kimliği yok, kendi
 * oturumu). Önek değil segment eşleşmesi (adv-419 (5)): `platformx/...` muaf DEĞİL.
 */
const NO_EXPECTED_SESSION_SEGMENTS = new Set(['oturum', 'platform']);

function isExemptPath(url: string): boolean {
  return NO_EXPECTED_SESSION_SEGMENTS.has(url.slice(API_ROOT.length + 1).split(/[/?#]/)[0] ?? '');
}

/**
 * `X-Beklenen-Oturum`: yazma isteğinin hangi kimlik adına yazılması beklendiği. Çağıran başlığı kendisi verdiyse (para
 * çekirdeği `beklenenOturum`) dokunulmaz; GET ve oturum/platform uçlarına ASLA eklenmez.
 * - `Idempotency-Key` taşıyan yazma (#417 L1/L2): anahtarın BAĞLI olduğu kimlik (`expectedIdentityFor`) — tekrar,
 *   anahtarın ilk gönderildiği kullanıcı adına gider. Dönen `key`, sonucun bağa bildirilmesi içindir.
 * - Anahtarsız yazma (adv-419 L3: iade, ters kayıt, kiraya çevirme, teslim/dönüş, dönem kapat…): sekmenin O AN bildiği
 *   kimlik (`ben`). Başka sekmede başka kullanıcıyla girildiyse bayat sekmenin tıkı yeni kullanıcı adına yazılmaz,
 *   409 `oturum_degisti` döner. `ben` yoksa başlık yok (oturumu sunucu denetler).
 */
function withExpectedSession(
  request: HttpRequest<unknown>,
  session: SessionService,
): { request: HttpRequest<unknown>; key: string | null } {
  if (
    SAFE_METHODS.has(request.method) ||
    request.headers.has(EXPECTED_SESSION_HEADER) ||
    isExemptPath(request.url)
  )
    return { request, key: null };
  const key = request.headers.get(IDEMPOTENCY_HEADER);
  const ben = session.ben();
  const identity =
    key !== null ? session.expectedIdentityFor(key) : ben ? identityOfSession(ben) : null;
  return identity === null
    ? { request, key: null }
    : { request: request.clone({ setHeaders: { [EXPECTED_SESSION_HEADER]: identity } }), key };
}

/**
 * adv-419 L1: deneme KESİN reddedildi mi (sunucu işledi, yazmadı)? 4xx; `mukerrer` (anahtar zaten yazılmış) ve
 * `oturum_degisti` (bağ kuralının kendisi) hariç. 2xx, 5xx, ağ hatası ve iptal kesin red değildir.
 */
function isDefinitiveRejection(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status < 400 || error.status >= 500)
    return false;
  const { kod } = toApiError(error);
  return kod !== 'mukerrer' && kod !== 'oturum_degisti';
}

/**
 * `HttpContext.set` nesneyi DEĞİŞTİRİR; çağıranın bağlamı (ör. birden çok istekte paylaşılan sabit)
 * kirlenmesin diye tekrar bayrakları kopyaya yazılır.
 */
function kopyala(context: HttpContext): HttpContext {
  const copy = new HttpContext();
  for (const key of context.keys()) copy.set(key, context.get(key));
  return copy;
}

/**
 * `/api/ui` hatalarını ProblemDetails `kod`'una göre ele alır (roadmap F3.3). HTTP durumu tek başına
 * yetmez: iki ayrı 403 ve iki ayrı 409 var. Hiçbir dalda sayfadan GEZİNİLMEZ (tek istisna
 * `kiraci_kapali`) — form verisi yerinde kalır.
 *
 * | kod | davranış |
 * |---|---|
 * | `dogrulama` | yalnız çağırana (alan hataları `ApiHatasi.alanlar` → `formHatasi`) |
 * | `oturum_yok` | yerinde yeniden giriş diyaloğu → AYNI istek tekrarlanır, sonuç çağırana |
 * | `kiraci_kapali` | tam temizlik + mesajlı giriş sayfası |
 * | `yetki_yok`, `pilot_degil` | uyarı bandı (form hatası değil) |
 * | `cakisma` | alan hatası varsa çağırana, yoksa bant; form korunur |
 * | `mukerrer` | `MUKERRERDE_YENILE` çağrılır + bilgi toast'u (`mevcut` varsa "zaten kaydedildi"; yoksa `MUKERRER_BASLIGI` verilirse o başlıkla uyarı; `MUKERRER_CAGIRAN_GOSTERIR` ise toast'u çağıran gösterir); YENİ ANAHTARLA TEKRAR GÖNDERİLMEZ |
 * | `xsrf_gecersiz` | `GET oturum/xsrf` ile belirteç yenilenir, istek BİR kez tekrarlanır |
 * | `cok_istek` | uyarı toast'u |
 * | 5xx / ağ | hata toast'u |
 *
 * Hata her durumda (başarılı tekrar hariç) çağırana aynen iletilir; `ApiIstemcisi` onu `ApiHatasi`'na çevirir.
 *
 * XSRF notu: Angular'ın XSRF interceptor'ı bu zincirin DIŞINDA (önce) çalışır; tekrar edilen istek eski
 * başlığı taşır. Bu yüzden tekrarlardan önce başlık çerezdeki TAZE belirteçle yeniden yazılır.
 */
export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(`${API_ROOT}/`)) return next(request);

  const session = inject(SessionService);
  const relogin = inject(ReloginService);
  const toast = inject(ToastService);
  const banner = inject(WarningBannerService);
  const xsrfReader = inject(HttpXsrfTokenExtractor);
  const http = inject(HttpClient);
  const t = translationFunction();

  const withFreshToken = (sent: HttpRequest<unknown>): HttpRequest<unknown> => {
    if (SAFE_METHODS.has(sent.method)) return sent;
    const token = xsrfReader.getToken();
    return token === null
      ? sent.clone({ headers: sent.headers.delete(XSRF_HEADER) })
      : sent.clone({ setHeaders: { [XSRF_HEADER]: token } });
  };

  const gonder = (sent: HttpRequest<unknown>): Observable<HttpEvent<unknown>> =>
    next(sent).pipe(catchError((error: unknown) => isle(error, sent)));

  const isle = (error: unknown, sent: HttpRequest<unknown>): Observable<HttpEvent<unknown>> => {
    if (!(error instanceof HttpErrorResponse)) return throwError(() => error);
    const context = sent.context;
    const silent = context.get(SILENT);
    const { kod: code, detay: detail, alanlar: fields, mevcut: existing } = toApiError(error);

    switch (code) {
      case 'xsrf_gecersiz':
        if (context.get(XSRF_REFRESHED)) break;
        return http
          .get(`${API_ROOT}/oturum/xsrf`, {
            context: requestContext({ sessiz: true, yenidenGirisYok: true }),
            withCredentials: true,
          })
          .pipe(
            catchError(() => throwError(() => error)),
            switchMap(() =>
              gonder(
                withFreshToken(sent.clone({ context: kopyala(context).set(XSRF_REFRESHED, true) })),
              ),
            ),
          );

      case 'oturum_yok':
        // Oturum hiç yoksa (giriş sayfası, açılış) diyalog değil guard/giriş sayfası karar verir.
        if (context.get(NO_RELOGIN) || context.get(REPEATED) || !session.loggedIn()) {
          break;
        }
        return from(relogin.request()).pipe(
          switchMap((entered) =>
            entered
              ? gonder(
                  withFreshToken(
                    sent.clone({
                      context: kopyala(context).set(REPEATED, true).set(XSRF_REFRESHED, false),
                    }),
                  ),
                )
              : throwError(() => error),
          ),
        );

      case 'kiraci_kapali':
        if (!context.get(NO_RELOGIN)) void session.tenantClosed();
        break;

      case 'yetki_yok':
      case 'pilot_degil':
        if (!silent) banner.show({ tur: 'uyari', mesaj: detail, kod: code });
        break;

      case 'cakisma':
        // Alan hatası varsa form gösterir (form korunur); yoksa sayfa bandı.
        if (!silent && fields === undefined)
          banner.show({ tur: 'uyari', mesaj: detail, kod: code });
        break;

      case 'mukerrer': {
        const refresh = context.get(REFRESH_ON_DUPLICATE);
        refresh?.();
        if (!silent && !context.get(DUPLICATE_CALLER_SHOWS)) {
          const message = refresh ? `${detail} ${t('geriBildirim.mukerrerYenilendi')}` : detail;
          const customHeader = context.get(DUPLICATE_HEADER);
          // F4.4 HIGH-1: işlem ZATEN yazıldı (kaybolan yanıttan sonraki tekrar) → "zaten kaydedildi" bilgisi;
          // "kayıt değişmiş, tekrar deneyin" izlenimi ikinci tahsilata yönlendiriyordu.
          if (existing?.ayniIcerik)
            toast.bilgi(message, { baslik: t('geriBildirim.zatenKaydedildi') });
          // 3. tur M-A: BAŞKA bir işlem yazılmış; bu isteğin tutarı YAZILMADI → uyarı (bilgi tonu kaydedildi sandırır).
          else if (existing) toast.uyari(message, { baslik: t('geriBildirim.baskaIslemYazildi') });
          else if (customHeader) toast.uyari(message, { baslik: customHeader });
          else toast.bilgi(message, { baslik: t('geriBildirim.mukerrerBaslik') });
        }
        break;
      }

      case 'cok_istek':
        if (!silent) toast.uyari(detail);
        break;

      case 'sunucu':
        if (!silent) toast.hata(t('geriBildirim.sunucuHatasi'));
        break;

      case 'ag':
        if (!silent) toast.hata(t('geriBildirim.agHatasi'));
        break;

      default:
        break;
    }
    return throwError(() => error);
  };

  const prepared = withExpectedSession(request, session);
  const key = prepared.key;
  const result = gonder(prepared.request);
  return key === null
    ? result
    : result.pipe(
        tap({
          error: (error: unknown) => {
            if (isDefinitiveRejection(error)) session.keyAttemptRejected(key);
          },
        }),
      );
};

/**
 * Interceptor bu hatayı sayfa düzeyinde (bant, toast, giriş sayfası) zaten gösterdi mi? Form bunları
 * ikinci kez form üstü hataya yazmaz (`formGonderimi`). `dogrulama`, `cakisma` (alansızsa bant + form üstü:
 * formun gönder düğmesinin yanında da görünsün), vazgeçilen `oturum_yok` ve `kod`'suz 4xx forma aittir.
 */
export function genelGosterilir(error: Pick<ApiHatasiBilgisi, 'kod'>): boolean {
  switch (error.kod) {
    case 'yetki_yok':
    case 'pilot_degil':
    case 'mukerrer':
    case 'cok_istek':
    case 'sunucu':
    case 'ag':
    case 'kiraci_kapali':
      return true;
    default:
      return false;
  }
}
