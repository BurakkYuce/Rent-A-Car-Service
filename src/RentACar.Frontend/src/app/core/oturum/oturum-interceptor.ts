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

import { type ApiHatasiBilgisi, apiHatasinaCevir } from '@core/api/api-hatasi';
import { API_KOKU, XSRF_BASLIGI } from '@core/api/api-istemcisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';

import {
  istekBaglami,
  MUKERRERDE_YENILE,
  MUKERRER_BASLIGI,
  SESSIZ,
  TEKRARLANDI,
  XSRF_YENILENDI,
  YENIDEN_GIRIS_YOK,
} from './istek-baglami';
import { OturumServisi } from './oturum-servisi';
import { YenidenGirisServisi } from './yeniden-giris-servisi';

const GUVENLI_YONTEMLER = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);

/**
 * `HttpContext.set` nesneyi DEĞİŞTİRİR; çağıranın bağlamı (ör. birden çok istekte paylaşılan sabit)
 * kirlenmesin diye tekrar bayrakları kopyaya yazılır.
 */
function kopyala(baglam: HttpContext): HttpContext {
  const kopya = new HttpContext();
  for (const anahtar of baglam.keys()) kopya.set(anahtar, baglam.get(anahtar));
  return kopya;
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
 * | `mukerrer` | `MUKERRERDE_YENILE` çağrılır + bilgi toast'u (`MUKERRER_BASLIGI` verilirse o başlıkla uyarı); YENİ ANAHTARLA TEKRAR GÖNDERİLMEZ |
 * | `xsrf_gecersiz` | `GET oturum/xsrf` ile belirteç yenilenir, istek BİR kez tekrarlanır |
 * | `cok_istek` | uyarı toast'u |
 * | 5xx / ağ | hata toast'u |
 *
 * Hata her durumda (başarılı tekrar hariç) çağırana aynen iletilir; `ApiIstemcisi` onu `ApiHatasi`'na çevirir.
 *
 * XSRF notu: Angular'ın XSRF interceptor'ı bu zincirin DIŞINDA (önce) çalışır; tekrar edilen istek eski
 * başlığı taşır. Bu yüzden tekrarlardan önce başlık çerezdeki TAZE belirteçle yeniden yazılır.
 */
export const oturumInterceptor: HttpInterceptorFn = (istek, sonraki) => {
  if (!istek.url.startsWith(`${API_KOKU}/`)) return sonraki(istek);

  const oturum = inject(OturumServisi);
  const yenidenGiris = inject(YenidenGirisServisi);
  const toast = inject(ToastServisi);
  const bant = inject(UyariBandiServisi);
  const xsrfOkuyucu = inject(HttpXsrfTokenExtractor);
  const http = inject(HttpClient);
  const t = ceviriFonksiyonu();

  const tazeBelirtecle = (gonderilen: HttpRequest<unknown>): HttpRequest<unknown> => {
    if (GUVENLI_YONTEMLER.has(gonderilen.method)) return gonderilen;
    const belirtec = xsrfOkuyucu.getToken();
    return belirtec === null
      ? gonderilen.clone({ headers: gonderilen.headers.delete(XSRF_BASLIGI) })
      : gonderilen.clone({ setHeaders: { [XSRF_BASLIGI]: belirtec } });
  };

  const gonder = (gonderilen: HttpRequest<unknown>): Observable<HttpEvent<unknown>> =>
    sonraki(gonderilen).pipe(catchError((hata: unknown) => isle(hata, gonderilen)));

  const isle = (
    hata: unknown,
    gonderilen: HttpRequest<unknown>,
  ): Observable<HttpEvent<unknown>> => {
    if (!(hata instanceof HttpErrorResponse)) return throwError(() => hata);
    const baglam = gonderilen.context;
    const sessiz = baglam.get(SESSIZ);
    const { kod, detay, alanlar } = apiHatasinaCevir(hata);

    switch (kod) {
      case 'xsrf_gecersiz':
        if (baglam.get(XSRF_YENILENDI)) break;
        return http
          .get(`${API_KOKU}/oturum/xsrf`, {
            context: istekBaglami({ sessiz: true, yenidenGirisYok: true }),
            withCredentials: true,
          })
          .pipe(
            catchError(() => throwError(() => hata)),
            switchMap(() =>
              gonder(
                tazeBelirtecle(
                  gonderilen.clone({ context: kopyala(baglam).set(XSRF_YENILENDI, true) }),
                ),
              ),
            ),
          );

      case 'oturum_yok':
        // Oturum hiç yoksa (giriş sayfası, açılış) diyalog değil guard/giriş sayfası karar verir.
        if (baglam.get(YENIDEN_GIRIS_YOK) || baglam.get(TEKRARLANDI) || !oturum.girisYapildi()) {
          break;
        }
        return from(yenidenGiris.iste()).pipe(
          switchMap((girildi) =>
            girildi
              ? gonder(
                  tazeBelirtecle(
                    gonderilen.clone({
                      context: kopyala(baglam).set(TEKRARLANDI, true).set(XSRF_YENILENDI, false),
                    }),
                  ),
                )
              : throwError(() => hata),
          ),
        );

      case 'kiraci_kapali':
        if (!baglam.get(YENIDEN_GIRIS_YOK)) void oturum.kiraciKapandi();
        break;

      case 'yetki_yok':
      case 'pilot_degil':
        if (!sessiz) bant.goster({ tur: 'uyari', mesaj: detay, kod });
        break;

      case 'cakisma':
        // Alan hatası varsa form gösterir (form korunur); yoksa sayfa bandı.
        if (!sessiz && alanlar === undefined) bant.goster({ tur: 'uyari', mesaj: detay, kod });
        break;

      case 'mukerrer': {
        const yenile = baglam.get(MUKERRERDE_YENILE);
        yenile?.();
        if (!sessiz) {
          const mesaj = yenile ? `${detay} ${t('geriBildirim.mukerrerYenilendi')}` : detay;
          const ozelBaslik = baglam.get(MUKERRER_BASLIGI);
          if (ozelBaslik) toast.uyari(mesaj, { baslik: ozelBaslik });
          else toast.bilgi(mesaj, { baslik: t('geriBildirim.mukerrerBaslik') });
        }
        break;
      }

      case 'cok_istek':
        if (!sessiz) toast.uyari(detay);
        break;

      case 'sunucu':
        if (!sessiz) toast.hata(t('geriBildirim.sunucuHatasi'));
        break;

      case 'ag':
        if (!sessiz) toast.hata(t('geriBildirim.agHatasi'));
        break;

      default:
        break;
    }
    return throwError(() => hata);
  };

  return gonder(istek);
};

/**
 * Interceptor bu hatayı sayfa düzeyinde (bant, toast, giriş sayfası) zaten gösterdi mi? Form bunları
 * ikinci kez form üstü hataya yazmaz (`formGonderimi`). `dogrulama`, `cakisma` (alansızsa bant + form üstü:
 * formun gönder düğmesinin yanında da görünsün), vazgeçilen `oturum_yok` ve `kod`'suz 4xx forma aittir.
 */
export function genelGosterilir(hata: Pick<ApiHatasiBilgisi, 'kod'>): boolean {
  switch (hata.kod) {
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
