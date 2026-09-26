import { Injectable, Injector, Signal, effect, inject, signal, untracked } from '@angular/core';

import { SESSION_CONTEXT } from '@core/oturum/oturum-baglami';
import { tabContext } from '@core/sekme/tab-state';

/**
 * Neden yüklendi: ilk açılış, sorgu (URL/parametre) değişti, oturum bağlamı değişti, elle yenileme,
 * arka plandaki sekmeye dönüldü (`sekmeyeDonunce: 'yenile'`).
 */
export type FetchReason = 'ilk' | 'sorgu' | 'baglam' | 'elle' | 'sekme';

export interface FetchPolicyAyari<P> {
  /**
   * Yüklemeyi belirleyen parametre (ör. `listeSorgusuUrlSenkronu(...).sorgu`). Değişince `sorgu`
   * nedeniyle yüklenir. Sinyal yalnız ANLAMCA değişince bildirmeli (URL senkronu öyle yapar);
   * ek güvence olarak son yüklenen parametreyle `esit` ile karşılaştırılır.
   */
  readonly parametre: Signal<P>;
  /** Asıl yükleme — genellikle `store.yukle(p)`. İptal/yarış store'da (`switchMap`). */
  readonly yukle: (parameter: P, reason: FetchReason) => void;
  /** Oturum bağlamı düşünce (`null`) çağrılır — genellikle `store.sifirla()`. */
  readonly sifirla?: () => void;
  /**
   * Sayfa şu an görünür mü. Verilmezse sayfanın SEKMESİ görünür mü (F3.2 `sekmeBaglami().aktif`;
   * kabuk dışında ve testte her zaman görünür). `false` iken değişiklikler biriktirilir; sayfa görünür
   * olunca TEK yükleme yapılır — değişip eski değerine dönen parametre yükleme üretmez.
   */
  readonly aktif?: Signal<boolean>;
  /**
   * Arka plandaki sekmeye dönülünce: `degisirse` (varsayılan) yalnız bu arada parametre/bağlam
   * değiştiyse ya da `yenile` istendiyse yükler; `yenile` her dönüşte yeniden yükler (canlı pano,
   * rozetli liste gibi başka sekmede değişebilen veri). Form sayfası `degisirse` kalmalı.
   */
  readonly sekmeyeDonunce?: 'degisirse' | 'yenile';
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
  private readonly context = inject(SESSION_CONTEXT);
  private readonly injector = inject(Injector);
  private readonly sekme = tabContext();
  private readonly manualCounter = signal(0);
  private readonly _lastReason = signal<FetchReason | null>(null);

  /** Son yüklemenin nedeni (tanı/test için). */
  readonly lastReason = this._lastReason.asReadonly();

  /** Bir parametre–yükleme çiftini politikaya bağlar. Aynı sayfada birden çok kez çağrılabilir. */
  connect<P>(setting: FetchPolicyAyari<P>): void {
    const equal = setting.esit ?? Object.is;
    const activeSignal = setting.aktif ?? this.sekme.aktif;
    // Arka plandaki sekmenin effect'i çalışmaz (görünüm ayrık); dönüş, sayfanın sekmesinin kaç kez
    // öne geldiğini sayan sinyalle anlaşılır — takılınca effect bu değişiklikle koşar.
    const returnCounter = setting.sekmeyeDonunce === 'yenile' ? this.sekme.onaGelme : null;
    let last: {
      readonly parametre: P;
      readonly baglam: string;
      readonly elle: number;
      readonly donus: number;
    } | null = null;

    effect(
      () => {
        const context = this.context();
        const active = activeSignal();
        const parameter = setting.parametre();
        const manual = this.manualCounter();
        const returnInfo = returnCounter?.() ?? 0;

        if (context === null) {
          if (last !== null) {
            last = null;
            untracked(() => setting.sifirla?.());
          }
          return;
        }
        if (!active) return;

        const reason: FetchReason | null =
          last === null
            ? 'ilk'
            : last.baglam !== context.anahtar
              ? 'baglam'
              : !equal(last.parametre, parameter)
                ? 'sorgu'
                : last.elle !== manual
                  ? 'elle'
                  : last.donus !== returnInfo
                    ? 'sekme'
                    : null;
        if (reason === null) return;

        last = { parametre: parameter, baglam: context.anahtar, elle: manual, donus: returnInfo };
        untracked(() => {
          this._lastReason.set(reason);
          setting.yukle(parameter, reason);
        });
      },
      { injector: this.injector },
    );
  }

  /** Aynı parametreyle yeniden yükler (sayfa görünür değilse görünür olunca). */
  yenile(): void {
    this.manualCounter.update((n) => n + 1);
  }
}
