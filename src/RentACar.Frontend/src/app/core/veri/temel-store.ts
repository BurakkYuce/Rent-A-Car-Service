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

import { ApiHatasi, apiHatasinaCevir } from '@core/api/api-hatasi';
import { Sayfa } from '@core/api/sayfa';

/**
 * Dört açık durum. **`bos` "boş liste" DEĞİLDİR:** henüz yükleme istenmedi ya da store sıfırlandı
 * (çıkış). Sıfır kayıtlı başarılı yanıt `hazir`'dır (liste için bkz. `kayitYok`).
 */
export type StoreDurumTuru = 'bos' | 'yukleniyor' | 'hazir' | 'hata';

export type StoreDurumu<T> =
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
  private readonly _durum = signal<StoreDurumu<T>>({ tur: 'bos' });
  private readonly istekler = new Subject<{ readonly parametre: P } | null>();
  private readonly oncekiVeriyiKoru: boolean;
  private sonIstek: { readonly parametre: P } | null = null;

  /** Tam durum (ayrımlı birleşim) — şablonda `@switch (store.durum().tur)` ile. */
  readonly durum: Signal<StoreDurumu<T>> = this._durum.asReadonly();
  readonly tur: Signal<StoreDurumTuru> = computed(() => this._durum().tur);
  readonly yukleniyor: Signal<boolean> = computed(() => this._durum().tur === 'yukleniyor');
  /** `hazir`'da veri; `yukleniyor`'da (koru açıksa) önceki veri; `bos`/`hata`'da `undefined`. */
  readonly veri: Signal<T | undefined> = computed(() => {
    const d = this._durum();
    if (d.tur === 'hazir') return d.veri;
    if (d.tur === 'yukleniyor') return d.onceki;
    return undefined;
  });
  readonly hata: Signal<ApiHatasi | undefined> = computed(() => {
    const d = this._durum();
    return d.tur === 'hata' ? d.hata : undefined;
  });

  constructor(
    private readonly getir: (parametre: P) => Observable<T>,
    secenekler: TemelStoreSecenekleri = {},
  ) {
    this.oncekiVeriyiKoru = secenekler.oncekiVeriyiKoru ?? false;
    this.istekler
      .pipe(
        switchMap((istek) => (istek === null ? EMPTY : this.calistir(istek.parametre))),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((durum) => this._durum.set(durum));
  }

  /** Yükler; süren istek varsa iptal edilir. */
  yukle(parametre: P): void {
    this.sonIstek = { parametre };
    this.istekler.next(this.sonIstek);
  }

  /** Son parametreyle yeniden yükler (hata sonrası "Yeniden dene"). Hiç yüklenmediyse bir şey yapmaz. */
  yenile(): void {
    if (this.sonIstek !== null) this.istekler.next(this.sonIstek);
  }

  /** Süren isteği iptal eder, veriyi atar, `bos`'a döner (çıkış / bağlam kaybı — KVKK). */
  sifirla(): void {
    this.sonIstek = null;
    this.istekler.next(null);
    this._durum.set({ tur: 'bos' });
  }

  private calistir(parametre: P): Observable<StoreDurumu<T>> {
    this._durum.set({
      tur: 'yukleniyor',
      onceki: this.oncekiVeriyiKoru ? this.veri() : undefined,
    });
    return defer(() => this.getir(parametre)).pipe(
      take(1),
      throwIfEmpty(() => new Error('Veri kaynağı değer üretmeden tamamlandı.')),
      map((veri): StoreDurumu<T> => ({ tur: 'hazir', veri })),
      catchError((hata: unknown) =>
        of<StoreDurumu<T>>({ tur: 'hata', hata: apiHatasinaCevir(hata) }),
      ),
    );
  }
}

/**
 * "Kayıt bulunamadı" yalnız BAŞARILI ve sıfır kayıtlı yanıtta gösterilir. `hata`, `bos` ve
 * `yukleniyor` için `false` — hata hiçbir koşulda boş liste gibi görünmez.
 */
export function kayitYok<K>(durum: StoreDurumu<Sayfa<K>>): boolean {
  return durum.tur === 'hazir' && durum.veri.kayitlar.length === 0;
}
