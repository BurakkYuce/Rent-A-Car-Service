import { Injectable, Injector, Signal, effect, inject, signal, untracked } from '@angular/core';

import { OTURUM_BAGLAMI } from '@core/oturum/oturum-baglami';

/** Neden yüklendi: ilk açılış, sorgu (URL/parametre) değişti, oturum bağlamı değişti, elle yenileme. */
export type GetirmeNedeni = 'ilk' | 'sorgu' | 'baglam' | 'elle';

export interface FetchPolicyAyari<P> {
  /**
   * Yüklemeyi belirleyen parametre (ör. `listeSorgusuUrlSenkronu(...).sorgu`). Değişince `sorgu`
   * nedeniyle yüklenir. Sinyal yalnız ANLAMCA değişince bildirmeli (URL senkronu öyle yapar);
   * ek güvence olarak son yüklenen parametreyle `esit` ile karşılaştırılır.
   */
  readonly parametre: Signal<P>;
  /** Asıl yükleme — genellikle `store.yukle(p)`. İptal/yarış store'da (`switchMap`). */
  readonly yukle: (parametre: P, neden: GetirmeNedeni) => void;
  /** Oturum bağlamı düşünce (`null`) çağrılır — genellikle `store.sifirla()`. */
  readonly sifirla?: () => void;
  /**
   * Sayfa şu an görünür mü (F3.2 sekmeli çalışma alanı arka plandaki sekmeyi `false` yapar).
   * `false` iken değişiklikler biriktirilir; sayfa görünür olunca TEK yükleme yapılır — değişip eski
   * değerine dönen parametre yükleme üretmez. Verilmezse her zaman görünür.
   */
  readonly aktif?: Signal<boolean>;
  /** Parametre eşitliği; varsayılan `Object.is`. */
  readonly esit?: (a: P, b: P) => boolean;
}

/**
 * Sayfa düzeyi "NE ZAMAN yüklenir" kararı (Revlo `FetchPolicyService` uyarlaması). Sayfa bileşeninin
 * `providers`'ında verilir — tekil DEĞİL. NASIL yükleneceği store'da (`TemelStore`).
 *
 * Revlo'dan farklar: `HotelService` yerine `OTURUM_BAGLAMI` sinyali; dil değişimi yok (yalnız tr);
 * sekme eşleştirmesi (`tabMatchPath`) yerine `aktif` sinyali (F3.2 sağlar); bayrak yığını ve
 * abonelikler yerine tek `effect` + "son yüklenen" karşılaştırması.
 *
 * ```ts
 * @Component({ providers: [FetchPolicy, KiraListeStore], ... })
 * export class KiraListeSayfasi {
 *   private readonly store = inject(KiraListeStore);
 *   protected readonly liste = listeSorgusuUrlSenkronu(KIRA_LISTESI);
 *   constructor() {
 *     inject(FetchPolicy).baglan({
 *       parametre: this.liste.apiParametreleri,
 *       yukle: (p) => this.store.liste.yukle(p),
 *       sifirla: () => this.store.liste.sifirla(),
 *     });
 *   }
 * }
 * ```
 */
@Injectable()
export class FetchPolicy {
  private readonly baglam = inject(OTURUM_BAGLAMI);
  private readonly injector = inject(Injector);
  private readonly elleSayaci = signal(0);
  private readonly _sonNeden = signal<GetirmeNedeni | null>(null);

  /** Son yüklemenin nedeni (tanı/test için). */
  readonly sonNeden = this._sonNeden.asReadonly();

  /** Bir parametre–yükleme çiftini politikaya bağlar. Aynı sayfada birden çok kez çağrılabilir. */
  baglan<P>(ayar: FetchPolicyAyari<P>): void {
    const esit = ayar.esit ?? Object.is;
    let son: { readonly parametre: P; readonly baglam: string; readonly elle: number } | null =
      null;

    effect(
      () => {
        const baglam = this.baglam();
        const aktif = ayar.aktif?.() ?? true;
        const parametre = ayar.parametre();
        const elle = this.elleSayaci();

        if (baglam === null) {
          if (son !== null) {
            son = null;
            untracked(() => ayar.sifirla?.());
          }
          return;
        }
        if (!aktif) return;

        const neden: GetirmeNedeni | null =
          son === null
            ? 'ilk'
            : son.baglam !== baglam.anahtar
              ? 'baglam'
              : !esit(son.parametre, parametre)
                ? 'sorgu'
                : son.elle !== elle
                  ? 'elle'
                  : null;
        if (neden === null) return;

        son = { parametre, baglam: baglam.anahtar, elle };
        untracked(() => {
          this._sonNeden.set(neden);
          ayar.yukle(parametre, neden);
        });
      },
      { injector: this.injector },
    );
  }

  /** Aynı parametreyle yeniden yükler (sayfa görünür değilse görünür olunca). */
  yenile(): void {
    this.elleSayaci.update((n) => n + 1);
  }
}
