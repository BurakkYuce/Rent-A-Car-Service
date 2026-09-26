import { DestroyRef, Signal, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  defer,
  map,
  of,
  switchMap,
  take,
  throwIfEmpty,
} from 'rxjs';

import { ApiHatasi, toApiError } from '@core/api/api-hatasi';
import { Sayfa } from '@core/api/sayfa';

/**
 * Dört açık durum. **`bos` "boş liste" DEĞİLDİR:** henüz yükleme istenmedi ya da store sıfırlandı
 * (çıkış). Sıfır kayıtlı başarılı yanıt `hazir`'dır (liste için bkz. `kayitYok`).
 */
export type StoreStateType = 'bos' | 'yukleniyor' | 'hazir' | 'hata';

export type StoreState<T> =
  | { readonly tur: 'bos' }
  /** `onceki`: `oncekiVeriyiKoru` açıksa son iyi veri (yeniden yüklerken ekranda kalsın diye). */
  | { readonly tur: 'yukleniyor'; readonly onceki: T | undefined }
  | { readonly tur: 'hazir'; readonly veri: T }
  /** Hata durumunda veri YOK — ne boş liste ne de eski veri; ekran hatayı gösterir. */
  | { readonly tur: 'hata'; readonly hata: ApiHatasi };

export interface TemelStoreSecenekleri {
  /**
   * Yeniden yüklerken (parametre/bağlam değişti, `yenile`) son iyi veri `veri()` sinyalinde kalsın mı?
   * Varsayılan `false`. Tablo titremesini önlemek için açılır; hata gelirse eski veri yine düşer.
   */
  readonly oncekiVeriyiKoru?: boolean;
}

/**
 * Özellik store'larının TEK veri yükleme yolu (lint: özellik store dosyalarında ham `subscribe`
 * yasak). Bir getirme fonksiyonunu (`parametre → Observable<T>`) dört durumlu sinyallere çevirir.
 *
 * - **İptal:** her `yukle` önceki isteği `switchMap` ile iptal eder (HTTP isteği abort edilir); yalnız
 *   EN SON istenen parametrenin sonucu duruma yazılır. Revlo'daki "istek sürerken yenisini düşür"
 *   yarışı (eski filtrenin sonucu ekranda kalır) burada yapısal olarak yok.
 * - **Hata asla boş liste değildir:** hata `hata` durumudur; `veri()` `undefined` döner.
 * - **Dayanıklılık:** getirme fonksiyonu eşzamanlı fırlatsa ya da değer üretmeden tamamlansa bile durum
 *   `hata` olur ve store sonraki `yukle` çağrılarında çalışmaya devam eder (dış akış asla ölmez).
 *
 * Injection context'inde oluşturulur (alan başlatıcısı ya da yapıcı): aboneliği `DestroyRef` ile
 * kapanır. Sayfa düzeyi store'lar sayfanın `providers`'ında verilir; ne zaman yükleneceğine
 * `FetchPolicy` karar verir, nasıl yükleneceğine store.
 *
 * ```ts
 * @Injectable()
 * export class KiraListeStore {
 *   private readonly api = inject(ApiIstemcisi);
 *   readonly liste = new TemelStore((p: SorguParametreleri) =>
 *     this.api.get<Sayfa<KiraSatiri>>('/api/ui/v1/kiralar', { parametreler: p }),
 *   );
 * }
 * ```
 */
export class TemelStore<T, P = void> {
  private readonly _state = signal<StoreState<T>>({ tur: 'bos' });
  private readonly requests = new Subject<{ readonly parametre: P } | null>();
  private readonly keepPreviousData: boolean;
  private lastRequest: { readonly parametre: P } | null = null;

  /** Tam durum (ayrımlı birleşim) — şablonda `@switch (store.durum().tur)` ile. */
  readonly durum: Signal<StoreState<T>> = this._state.asReadonly();
  readonly tur: Signal<StoreStateType> = computed(() => this._state().tur);
  readonly isLoading: Signal<boolean> = computed(() => this._state().tur === 'yukleniyor');
  /** `hazir`'da veri; `yukleniyor`'da (koru açıksa) önceki veri; `bos`/`hata`'da `undefined`. */
  readonly veri: Signal<T | undefined> = computed(() => {
    const d = this._state();
    if (d.tur === 'hazir') return d.veri;
    if (d.tur === 'yukleniyor') return d.onceki;
    return undefined;
  });
  readonly hata: Signal<ApiHatasi | undefined> = computed(() => {
    const d = this._state();
    return d.tur === 'hata' ? d.hata : undefined;
  });

  constructor(
    private readonly fetch: (parameter: P) => Observable<T>,
    options: TemelStoreSecenekleri = {},
  ) {
    this.keepPreviousData = options.oncekiVeriyiKoru ?? false;
    this.requests
      .pipe(
        switchMap((request) => (request === null ? EMPTY : this.calistir(request.parametre))),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((status) => this._state.set(status));
  }

  /** Yükler; süren istek varsa iptal edilir. */
  yukle(parameter: P): void {
    this.lastRequest = { parametre: parameter };
    this.requests.next(this.lastRequest);
  }

  /** Son parametreyle yeniden yükler (hata sonrası "Yeniden dene"). Hiç yüklenmediyse bir şey yapmaz. */
  yenile(): void {
    if (this.lastRequest !== null) this.requests.next(this.lastRequest);
  }

  /** Süren isteği iptal eder, veriyi atar, `bos`'a döner (çıkış / bağlam kaybı — KVKK). */
  reset(): void {
    this.lastRequest = null;
    this.requests.next(null);
    this._state.set({ tur: 'bos' });
  }

  private calistir(parameter: P): Observable<StoreState<T>> {
    this._state.set({
      tur: 'yukleniyor',
      onceki: this.keepPreviousData ? this.veri() : undefined,
    });
    return defer(() => this.fetch(parameter)).pipe(
      take(1),
      throwIfEmpty(() => new Error('Veri kaynağı değer üretmeden tamamlandı.')),
      map((data): StoreState<T> => ({ tur: 'hazir', veri: data })),
      catchError((error: unknown) => of<StoreState<T>>({ tur: 'hata', hata: toApiError(error) })),
    );
  }
}

/**
 * "Kayıt bulunamadı" yalnız BAŞARILI ve sıfır kayıtlı yanıtta gösterilir. `hata`, `bos` ve
 * `yukleniyor` için `false` — hata hiçbir koşulda boş liste gibi görünmez.
 */
export function noRecord<K>(status: StoreState<Sayfa<K>>): boolean {
  return status.tur === 'hazir' && status.veri.kayitlar.length === 0;
}
