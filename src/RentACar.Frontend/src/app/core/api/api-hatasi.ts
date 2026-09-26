import { HttpErrorResponse } from '@angular/common/http';
import type { Schema } from './ui-tipleri';

/**
 * Sunucunun `/api/ui/v1` ProblemDetails'inde döndüğü `kod` değerleri (backend `UiHata.cs` kod tablosu).
 * Davranış HTTP durumuna değil `kod`'a bağlıdır: iki ayrı 403 (`yetki_yok` / `pilot_degil`) ve iki
 * ayrı 409 (`cakisma` → form korunur / `mukerrer` → kayıt yeniden yüklenir) var.
 */
export const SERVER_ERROR_CODES = [
  'dogrulama',
  'yetki_yok',
  'pilot_degil',
  'cakisma',
  'mukerrer',
  'oturum_yok',
  'kiraci_kapali',
  'cok_istek',
  'xsrf_gecersiz',
] as const;

export type ServerErrorCode = (typeof SERVER_ERROR_CODES)[number];

/**
 * Sunucunun `kod` vermediği durumlar için istemci kodları:
 * - `ag`: yanıt yok (status 0 — bağlantı koptu, CORS/CSP reddi, istek iptal edilmedi ama düştü);
 * - `sunucu`: `kod`'suz 5xx (sunucu mesajı bilinçli olarak sızdırmaz);
 * - `bilinmeyen`: `kod`'suz 4xx (404/405/415…), tanınmayan `kod`, HTTP dışı istisna.
 */
export type ClientErrorCode = 'ag' | 'sunucu' | 'bilinmeyen';

export type ApiErrorCode = ServerErrorCode | ClientErrorCode;

/**
 * 409 `mukerrer`'de aynı işlem anahtarıyla ZATEN yazılmış kayıt (F4.4 adversarial HIGH-1; OpenAPI `MevcutIslem`).
 * `ayniIcerik` true → kendi tekrarı: "zaten kaydedildi", form temizlenir. false → BAŞKA bir işlem yazılmış, gönderilen
 * tutar YAZILMADI: uyarı, form korunur, kayıt yenilenir, kullanıcı bilinçli yeniden gönderir (3. tur M-A).
 */
export type CurrentOperation = Schema<'MevcutIslem'>;

/** Alan adı → o alanın hata mesajları (ProblemDetails `errors`). */
export type FieldErrors = Readonly<Record<string, readonly string[]>>;

export interface ApiHatasiBilgisi {
  /** HTTP durumu; yanıt yoksa ya da hata HTTP dışıysa 0. */
  readonly status: number;
  readonly kod: ApiErrorCode;
  /** Kullanıcıya gösterilebilir Türkçe açıklama (ProblemDetails `detail`, yoksa `title`, yoksa varsayılan). */
  readonly detay: string;
  /** Yalnız sunucu alan bazlı hata verdiyse (ör. `dogrulama`, `Idempotency-Key`). */
  readonly alanlar?: FieldErrors;
  /** Yalnız 409 `mukerrer`'de, işlem zaten yazılmışsa (bkz. {@link CurrentOperation}). */
  readonly mevcut?: CurrentOperation;
}

const DEFAULT_DETAIL: Readonly<Record<ClientErrorCode, string>> = {
  ag: 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.',
  sunucu: 'Beklenmeyen bir sunucu hatası oluştu.',
  bilinmeyen: 'Beklenmeyen bir hata oluştu.',
};

/**
 * `/api/ui/v1` çağrılarının TEK hata tipi. `ApiIstemcisi` her hatayı buna çevirir; `TemelStore` hata
 * durumunda bunu taşır. `kod`'a göre davranış (yeniden giriş diyaloğu, bant, toast) F3.3 interceptor'ında.
 *
 * **Otomatik yeniden gönderim yok:** `mukerrer` (özellikle "farklı içerik") alındığında yeni anahtarla
 * tekrar gönderilmez, kayıt yeniden yüklenir (idempotency envanteri, LOW-A SPA kuralı).
 */
export class ApiHatasi extends Error implements ApiHatasiBilgisi {
  override readonly name = 'ApiHatasi';
  readonly status: number;
  readonly kod: ApiErrorCode;
  readonly detay: string;
  readonly alanlar?: FieldErrors;
  readonly mevcut?: CurrentOperation;

  constructor(info: ApiHatasiBilgisi, reason?: unknown) {
    super(info.detay, reason === undefined ? undefined : { cause: reason });
    this.status = info.status;
    this.kod = info.kod;
    this.detay = info.detay;
    if (info.alanlar !== undefined) this.alanlar = info.alanlar;
    if (info.mevcut !== undefined) this.mevcut = info.mevcut;
  }
}

/**
 * Herhangi bir hatayı `ApiHatasi`'na çevirir (saf; F3.3 interceptor'ı da `HttpErrorResponse` için
 * bunu kullanır). Zaten `ApiHatasi` ise aynen döner.
 */
export function toApiError(error: unknown): ApiHatasi {
  if (error instanceof ApiHatasi) return error;
  if (!(error instanceof HttpErrorResponse)) {
    return new ApiHatasi({ status: 0, kod: 'bilinmeyen', detay: DEFAULT_DETAIL.bilinmeyen }, error);
  }

  const status = error.status;
  if (status === 0) {
    return new ApiHatasi({ status: 0, kod: 'ag', detay: DEFAULT_DETAIL.ag }, error);
  }

  const body = problemBody(error.error);
  const serverCode = serverErrorCode(body?.['kod']);
  const code: ApiErrorCode = serverCode ?? (status >= 500 ? 'sunucu' : 'bilinmeyen');
  const detail =
    filledText(body?.['detail']) ??
    filledText(body?.['title']) ??
    DEFAULT_DETAIL[code === 'sunucu' ? 'sunucu' : 'bilinmeyen'];
  const fields = fieldErrors(body?.['errors']);
  const existing = code === 'mukerrer' ? currentOperation(body?.['mevcut']) : undefined;

  return new ApiHatasi(
    {
      status,
      kod: code,
      detay: detail,
      ...(fields === undefined ? {} : { alanlar: fields }),
      ...(existing === undefined ? {} : { mevcut: existing }),
    },
    error,
  );
}

/** `mevcut` uzantısı → tipli kayıt; biçimsizse `undefined` (uydurma "zaten kaydedildi" yok). */
function currentOperation(value: unknown): CurrentOperation | undefined {
  if (!isObject(value)) return undefined;
  const {
    id,
    belgeNo: documentNo,
    tutar: amount,
    doviz: currency,
    ayniIcerik: sameContent,
  } = value;
  if (typeof id !== 'string' || typeof documentNo !== 'string' || typeof currency !== 'string')
    return undefined;
  if (typeof amount !== 'number' && typeof amount !== 'string') return undefined;
  // Güvenli taraf: bilinmiyorsa "aynı içerik DEĞİL" — form silinmez, "YAZILMADI" uyarısı (kasiyer parasını
  // kaydedildi sanmasın).
  return {
    id,
    belgeNo: documentNo,
    tutar: amount,
    doviz: currency,
    ayniIcerik: sameContent === true,
  };
}

/** Tip korumalı: değer bilinen bir sunucu kodu mu? */
export function isServerErrorCode(value: unknown): value is ServerErrorCode {
  return typeof value === 'string' && (SERVER_ERROR_CODES as readonly string[]).includes(value);
}

function serverErrorCode(value: unknown): ServerErrorCode | null {
  return isServerErrorCode(value) ? value : null;
}

/** ProblemDetails gövdesi: ayrıştırılmış nesne ya da (JSON ayrıştırılamadıysa) metin gelebilir. */
function problemBody(body: unknown): Readonly<Record<string, unknown>> | null {
  if (typeof body === 'string') {
    try {
      const detached: unknown = JSON.parse(body);
      return isObject(detached) ? detached : null;
    } catch {
      return null;
    }
  }
  return isObject(body) ? body : null;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function filledText(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

/**
 * `errors` → `AlanHatalari`. Biçimsiz girdiler atılır (dizi olmayan değer tek mesaja çevrilir, metin
 * olmayan öğeler düşer, boş kalan alan hiç yazılmaz); geçerli alan kalmazsa `undefined`.
 */
function fieldErrors(value: unknown): FieldErrors | undefined {
  if (!isObject(value)) return undefined;
  const result: Record<string, readonly string[]> = {};
  for (const [alan, messages] of Object.entries(value)) {
    const list = (Array.isArray(messages) ? messages : [messages]).filter(
      (m): m is string => typeof m === 'string' && m.trim() !== '',
    );
    if (list.length > 0) result[alan] = list;
  }
  return Object.keys(result).length > 0 ? result : undefined;
}
