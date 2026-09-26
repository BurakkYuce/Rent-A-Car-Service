import { Injectable, type ComponentRef } from '@angular/core';
import type {
  ActivatedRouteSnapshot,
  DetachedRouteHandle,
  RouteReuseStrategy,
} from '@angular/router';

import { tabKey } from './tab-key';

/** Angular'ın `DetachedRouteHandle`'ı opak; içinde bileşen referansı taşır. */
interface AyrikTutucu {
  readonly componentRef?: ComponentRef<unknown>;
}

function componentRefValue(
  handle: DetachedRouteHandle | undefined,
): ComponentRef<unknown> | undefined {
  return (handle as AyrikTutucu | undefined)?.componentRef;
}

/**
 * Sekmeli çalışma alanının rota yeniden kullanım stratejisi (Revlo `TabRouteReuseStrategy` fikri,
 * yeniden yazıldı). Açık sekmesi olan sayfa başka sekmeye geçilince YOK EDİLMEZ, ayrılıp saklanır; geri
 * dönülünce aynı bileşen (form durumu, kaydırma, store) yeniden takılır.
 *
 * - Anahtar `sekmeAnahtari` (desen + yol parametreleri): aynı desenin farklı kaydı ayrı bileşendir
 *   (`shouldReuseRoute` false), sorgu değişimi aynı bileşende kalır.
 * - Yalnız AÇIK sekmelerin sayfası saklanır (`acikAnahtarlariAyarla`); kapanan sekmenin ayrık
 *   bileşeni hemen yok edilir. Angular yeniden takınca `store(rota, null)` çağırır → kayıt düşer;
 *   haritada yalnız AYRIK (görünmeyen) bileşenler durur.
 * - İlk pakettedir (router sağlayıcısı); sekme listesi, depo ve arayüz kabuğun tembel parçasında.
 */
@Injectable({ providedIn: 'root' })
export class TabRouteStrategy implements RouteReuseStrategy {
  private acik = new Set<string>();
  private readonly handles = new Map<string, DetachedRouteHandle>();

  /** Açık sekme anahtarları; artık açık olmayan ayrık bileşenler yok edilir. */
  setOpenKeys(keys: Iterable<string>): void {
    this.acik = new Set(keys);
    for (const [key, handle] of [...this.handles]) {
      if (!this.acik.has(key)) {
        this.handles.delete(key);
        componentRefValue(handle)?.destroy();
      }
    }
  }

  /** Bu rotadan çıkılınca sayfası arka planda tutulacak mı (açık sekmesi var mı)? */
  shouldStore(route: ActivatedRouteSnapshot): boolean {
    const key = tabKey(route);
    return key !== null && this.acik.has(key);
  }

  /** Arka plandaki (ayrık) sekmenin bileşeni; yoksa `null`. */
  detachedComponent(key: string): unknown {
    return componentRefValue(this.handles.get(key))?.instance ?? null;
  }

  /** Çıkış: tüm ayrık bileşenler yok edilir, açık küme boşalır. */
  clear(): void {
    this.setOpenKeys([]);
  }

  shouldDetach(route: ActivatedRouteSnapshot): boolean {
    return this.shouldStore(route);
  }

  store(route: ActivatedRouteSnapshot, handle: DetachedRouteHandle | null): void {
    const key = tabKey(route);
    if (key === null) return;
    if (handle === null) {
      this.handles.delete(key); // yeniden takıldı: artık ayrık değil
      return;
    }
    const old = this.handles.get(key);
    if (old && old !== handle) componentRefValue(old)?.destroy();
    if (this.acik.has(key)) this.handles.set(key, handle);
    else componentRefValue(handle)?.destroy();
  }

  shouldAttach(route: ActivatedRouteSnapshot): boolean {
    const key = tabKey(route);
    return key !== null && this.acik.has(key) && this.handles.has(key);
  }

  retrieve(route: ActivatedRouteSnapshot): DetachedRouteHandle | null {
    const key = tabKey(route);
    return key === null ? null : (this.handles.get(key) ?? null);
  }

  shouldReuseRoute(future: ActivatedRouteSnapshot, current: ActivatedRouteSnapshot): boolean {
    if (future.routeConfig !== current.routeConfig) return false;
    const key = tabKey(future);
    return key === null || key === tabKey(current);
  }
}
