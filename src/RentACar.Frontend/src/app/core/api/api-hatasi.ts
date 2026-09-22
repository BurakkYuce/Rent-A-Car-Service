import { HttpErrorResponse } from '@angular/common/http';
import type { Sema } from './ui-tipleri';

/**
 * Sunucunun `/api/ui/v1` ProblemDetails'inde döndüğü `kod` değerleri (backend `UiHata.cs` kod tablosu).
 * Davranış HTTP durumuna değil `kod`'a bağlıdır: iki ayrı 403 (`yetki_yok` / `pilot_degil`) ve iki
 * ayrı 409 (`cakisma` → form korunur / `mukerrer` → kayıt yeniden yüklenir) var.
 */
export const SUNUCU_HATA_KODLARI = [
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

export type SunucuHataKodu = (typeof SUNUCU_HATA_KODLARI)[number];

/**
 * Sunucunun `kod` vermediği durumlar için istemci kodları:
 * - `ag`: yanıt yok (status 0 — bağlantı koptu, CORS/CSP reddi, istek iptal edilmedi ama düştü);
 * - `sunucu`: `kod`'suz 5xx (sunucu mesajı bilinçli olarak sızdırmaz);
 * - `bilinmeyen`: `kod`'suz 4xx (404/405/415…), tanınmayan `kod`, HTTP dışı istisna.
 */
export type IstemciHataKodu = 'ag' | 'sunucu' | 'bilinmeyen';

export type ApiHataKodu = SunucuHataKodu | IstemciHataKodu;

/**
 * 409 `mukerrer`'de aynı işlem anahtarıyla ZATEN yazılmış kayıt (F4.4 adversarial HIGH-1; OpenAPI `MevcutIslem`).
 * Doluysa istemci "zaten kaydedildi" der, formu temizler — yeniden gönderime YÖNLENDİRMEZ.
 */
export type MevcutIslem = Sema<'MevcutIslem'>;

/** Alan adı → o alanın hata mesajları (ProblemDetails `errors`). */
export type AlanHatalari = Readonly<Record<string, readonly string[]>>;

export interface ApiHatasiBilgisi {
  /** HTTP durumu; yanıt yoksa ya da hata HTTP dışıysa 0. */
  readonly status: number;
  readonly kod: ApiHataKodu;
  /** Kullanıcıya gösterilebilir Türkçe açıklama (ProblemDetails `detail`, yoksa `title`, yoksa varsayılan). */
  readonly detay: string;
  /** Yalnız sunucu alan bazlı hata verdiyse (ör. `dogrulama`, `Idempotency-Key`). */
  readonly alanlar?: AlanHatalari;
  /** Yalnız 409 `mukerrer`'de, işlem zaten yazılmışsa (bkz. {@link MevcutIslem}). */
  readonly mevcut?: MevcutIslem;
}

const VARSAYILAN_DETAY: Readonly<Record<IstemciHataKodu, string>> = {
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
  readonly kod: ApiHataKodu;
  readonly detay: string;
  readonly alanlar?: AlanHatalari;
  readonly mevcut?: MevcutIslem;

  constructor(bilgi: ApiHatasiBilgisi, neden?: unknown) {
    super(bilgi.detay, neden === undefined ? undefined : { cause: neden });
    this.status = bilgi.status;
    this.kod = bilgi.kod;
    this.detay = bilgi.detay;
    if (bilgi.alanlar !== undefined) this.alanlar = bilgi.alanlar;
    if (bilgi.mevcut !== undefined) this.mevcut = bilgi.mevcut;
  }
}

/**
 * Herhangi bir hatayı `ApiHatasi`'na çevirir (saf; F3.3 interceptor'ı da `HttpErrorResponse` için
 * bunu kullanır). Zaten `ApiHatasi` ise aynen döner.
 */
export function apiHatasinaCevir(hata: unknown): ApiHatasi {
  if (hata instanceof ApiHatasi) return hata;
  if (!(hata instanceof HttpErrorResponse)) {
    return new ApiHatasi(
      { status: 0, kod: 'bilinmeyen', detay: VARSAYILAN_DETAY.bilinmeyen },
      hata,
    );
  }

  const status = hata.status;
  if (status === 0) {
    return new ApiHatasi({ status: 0, kod: 'ag', detay: VARSAYILAN_DETAY.ag }, hata);
  }

  const govde = problemGovdesi(hata.error);
  const sunucuKodu = sunucuHataKodu(govde?.['kod']);
  const kod: ApiHataKodu = sunucuKodu ?? (status >= 500 ? 'sunucu' : 'bilinmeyen');
  const detay =
    doluMetin(govde?.['detail']) ??
    doluMetin(govde?.['title']) ??
    VARSAYILAN_DETAY[kod === 'sunucu' ? 'sunucu' : 'bilinmeyen'];
  const alanlar = alanHatalari(govde?.['errors']);
  const mevcut = kod === 'mukerrer' ? mevcutIslem(govde?.['mevcut']) : undefined;

  return new ApiHatasi(
    {
      status,
      kod,
      detay,
      ...(alanlar === undefined ? {} : { alanlar }),
      ...(mevcut === undefined ? {} : { mevcut }),
    },
    hata,
  );
}

/** `mevcut` uzantısı → tipli kayıt; biçimsizse `undefined` (uydurma "zaten kaydedildi" yok). */
function mevcutIslem(deger: unknown): MevcutIslem | undefined {
  if (!nesneMi(deger)) return undefined;
  const { id, belgeNo, tutar, doviz } = deger;
  if (typeof id !== 'string' || typeof belgeNo !== 'string' || typeof doviz !== 'string')
    return undefined;
  if (typeof tutar !== 'number' && typeof tutar !== 'string') return undefined;
  return { id, belgeNo, tutar, doviz };
}

/** Tip korumalı: değer bilinen bir sunucu kodu mu? */
export function sunucuHataKoduMu(deger: unknown): deger is SunucuHataKodu {
  return typeof deger === 'string' && (SUNUCU_HATA_KODLARI as readonly string[]).includes(deger);
}

function sunucuHataKodu(deger: unknown): SunucuHataKodu | null {
  return sunucuHataKoduMu(deger) ? deger : null;
}

/** ProblemDetails gövdesi: ayrıştırılmış nesne ya da (JSON ayrıştırılamadıysa) metin gelebilir. */
function problemGovdesi(govde: unknown): Readonly<Record<string, unknown>> | null {
  if (typeof govde === 'string') {
    try {
      const ayrik: unknown = JSON.parse(govde);
      return nesneMi(ayrik) ? ayrik : null;
    } catch {
      return null;
    }
  }
  return nesneMi(govde) ? govde : null;
}

function nesneMi(deger: unknown): deger is Record<string, unknown> {
  return typeof deger === 'object' && deger !== null && !Array.isArray(deger);
}

function doluMetin(deger: unknown): string | null {
  return typeof deger === 'string' && deger.trim() !== '' ? deger : null;
}

/**
 * `errors` → `AlanHatalari`. Biçimsiz girdiler atılır (dizi olmayan değer tek mesaja çevrilir, metin
 * olmayan öğeler düşer, boş kalan alan hiç yazılmaz); geçerli alan kalmazsa `undefined`.
 */
function alanHatalari(deger: unknown): AlanHatalari | undefined {
  if (!nesneMi(deger)) return undefined;
  const sonuc: Record<string, readonly string[]> = {};
  for (const [alan, mesajlar] of Object.entries(deger)) {
    const liste = (Array.isArray(mesajlar) ? mesajlar : [mesajlar]).filter(
      (m): m is string => typeof m === 'string' && m.trim() !== '',
    );
    if (liste.length > 0) sonuc[alan] = liste;
  }
  return Object.keys(sonuc).length > 0 ? sonuc : undefined;
}
