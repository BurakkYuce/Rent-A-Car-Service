import { signal } from '@angular/core';
import { EMPTY, Observable, defer, finalize, tap } from 'rxjs';
import { toApiError } from '../api/api-hatasi';

/**
 * Gönderim kilidi + `Idempotency-Key` kuralı (docs/api/idempotency-envanteri.md, roadmap README
 * "İmzalar"):
 *
 * 1. **Sunucunun deterministik anahtarı** (ör. panel DTO'sundaki `TahsilatAnahtar`) varsa o kullanılır
 *    ve DOKUNULMADAN geçer; istemci anahtarı ne üretilir ne tüketilir. Sunucuda da başlıktan önceliklidir.
 * 2. Yoksa **mantıksal gönderim başına bir istemci anahtarı** (`ApiIstemcisi` `islemAnahtari` seçeneğiyle
 *    `Idempotency-Key` başlığına yazar) üretilir ve AYNI gönderimin yeniden
 *    denemelerinde (ağ hatası, 400 sonrası düzeltip tekrar, zaman aşımı) korunur — ilk istek sunucuya
 *    ulaşıp yazıldıysa ikincisi mükerrer sayılır, çift kayıt olmaz.
 * 3. Anahtar **her 2xx'ten sonra yenilenir**: sabit paneldeki ikinci meşru tahsilat "mükerrer" olmaz.
 *    409 `mukerrer` da işlemi sonuçlandırır (kayıt zaten var, sayfa yeniden yükler) → anahtar yenilenir;
 *    ama OTOMATİK yeniden gönderim yapılmaz (envanter LOW-A).
 * 4. İstek uçarken ikinci gönderim yok sayılır (çift tık tek istek).
 */
export class SubmitLock {
  private readonly _sending = signal(false);
  /** Düğmeyi pasifleştirmek için: `[disabled]="kilit.gonderiliyor()"`. */
  readonly gonderiliyor = this._sending.asReadonly();
  private anahtar: string | null = null;

  constructor(private readonly generateKey: () => string = newOperationKey) {}

  /** Bir sonraki (ya da yeniden denenen) gönderimin kullanacağı istemci anahtarı; henüz yoksa `null`. */
  get pendingKey(): string | null {
    return this.anahtar;
  }

  /**
   * `istek` yalnız kilit boştaysa çağrılır; çağrılmadıysa akış boş tamamlanır. Abonelik başına bir
   * gönderim — tekrar abone olmak yeni bir gönderim denemesidir (aynı anahtarla).
   */
  gonder<T>(
    request: (key: string) => Observable<T>,
    option?: { readonly deterministikAnahtar?: string | null },
  ): Observable<T> {
    return defer(() => {
      if (this._sending()) return EMPTY;
      const deterministic = option?.deterministikAnahtar?.trim() || null;
      const key = deterministic ?? (this.anahtar ??= this.generateKey());
      this._sending.set(true);
      return request(key).pipe(
        // `ApiIstemcisi` her hatayı `ApiHatasi`'na çevirir; çevrilmemiş hata da burada çevrilip okunur.
        tap({
          next: () => {
            if (!deterministic) this.anahtar = null;
          },
          error: (error: unknown) => {
            if (!deterministic && toApiError(error).kod === 'mukerrer') this.anahtar = null;
          },
        }),
        finalize(() => this._sending.set(false)),
      );
    });
  }

  /** Form yeni bir kayda sıfırlandığında: bir sonraki gönderim yeni anahtarla başlar. */
  yenile(): void {
    this.anahtar = null;
  }
}

/**
 * Rastgele UUIDv4 (36 görünür ASCII karakter; sunucu 16–128 ister). `randomUUID` yalnız güvenli
 * bağlamda (https/localhost) var; yerel ağdan http ile açılan geliştirme sunucusunda da çalışsın diye
 * `getRandomValues` yedeği.
 */
export function newOperationKey(): string {
  const c = globalThis.crypto;
  if (typeof c.randomUUID === 'function') return c.randomUUID();
  const b = c.getRandomValues(new Uint8Array(16));
  b[6] = ((b[6] ?? 0) & 0x0f) | 0x40;
  b[8] = ((b[8] ?? 0) & 0x3f) | 0x80;
  const h = Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}
