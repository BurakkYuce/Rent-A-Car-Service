import { computed, inject, Injectable, signal, type Signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { tabKey } from './tab-key';

/**
 * Sekmeli çalışma alanının (F3.2) ilk pakette duran küçük durumu: hangi sekme görünür ve sayfaların
 * verdiği sekme etiketleri. Kabuk (tembel parça) yazar; sayfalar ve `FetchPolicy` okur.
 */
@Injectable({ providedIn: 'root' })
export class TabState {
  /**
   * Görünür sekmenin anahtarı. `null`: kabuk yok (giriş sayfası, birim testleri) → her sayfa görünür
   * sayılır. Kabuk içinde sekme olmayan sayfada `''` (arka plandaki sekmeler görünmez).
   */
  readonly activeKey = signal<string | null>(null);

  private readonly _onFocus = signal<ReadonlyMap<string, number>>(new Map());
  /** Her sekmenin kaç kez öne geldiği (FetchPolicy `sekmeyeDonunce: 'yenile'`). */
  readonly bringToFront = this._onFocus.asReadonly();

  private readonly _labels = signal<ReadonlyMap<string, string>>(new Map());
  /** Sayfaların verdiği etiketler. YALNIZ BELLEKTE — depoya yazılmaz (müşteri adı olabilir, KVKK). */
  readonly labels = this._labels.asReadonly();

  /** Görünür sekmeyi ayarlar; ÖNCEKİNDEN farklıysa öne gelme sayacı artar (sorgu değişimi saymaz). */
  activate(key: string | null): void {
    if (this.activeKey() === key) return;
    this.activeKey.set(key);
    if (key) {
      this._onFocus.update((previous) => new Map(previous).set(key, (previous.get(key) ?? 0) + 1));
    }
  }

  setLabel(key: string, label: string | null): void {
    this._labels.update((previous) => {
      const newItem = new Map(previous);
      if (label?.trim()) newItem.set(key, label.trim());
      else newItem.delete(key);
      return newItem;
    });
  }

  clear(): void {
    this.activeKey.set(null);
    this._onFocus.set(new Map());
    this._labels.set(new Map());
  }
}

export interface TabContext {
  /** Bu sayfanın sekme anahtarı; sekme sayfası değilse `null`. */
  readonly anahtar: string | null;
  /** Sayfa şu an görünür mü (arka plandaki sekmede `false`). `FetchPolicy` varsayılan olarak bunu kullanır. */
  readonly aktif: Signal<boolean>;
  /** Sekme kaç kez öne geldi (arka plandan dönüşte artar); sekme değilse hep 0. */
  readonly onaGelme: Signal<number>;
  /**
   * Sekme başlığı (ör. `Kira 2026260801001`). Bellekte kalır, depoya YAZILMAZ; `null` → rota başlığı.
   */
  etiketAyarla(label: string | null): void;
}

/**
 * Sayfanın sekme bağlamı — sayfa bileşeninde ya da onun `providers`'ındaki bir serviste, enjeksiyon
 * bağlamında çağrılır. Router yoksa (birim testi) sayfa her zaman görünür sayılır.
 */
export function tabContext(): TabContext {
  const status = inject(TabState);
  const route = inject(ActivatedRoute, { optional: true });
  const key = route ? tabKey(route.snapshot) : null;
  return {
    anahtar: key,
    aktif: computed(() => {
      const active = status.activeKey();
      return active === null || key === null || active === key;
    }),
    onaGelme: computed(() => (key === null ? 0 : (status.bringToFront().get(key) ?? 0))),
    etiketAyarla: (label) => {
      if (key !== null) status.setLabel(key, label);
    },
  };
}
