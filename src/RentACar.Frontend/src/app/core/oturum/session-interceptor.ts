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
import { catchError, from, Observable, switchMap, throwError } from 'rxjs';

import { type ApiHatasiBilgisi, toApiError } from '@core/api/api-hatasi';
import { API_ROOT, XSRF_HEADER } from '@core/api/api-istemcisi';
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
import { SessionService } from './session-service';
import { ReloginService } from './relogin-service';

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);

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

  return gonder(request);
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
