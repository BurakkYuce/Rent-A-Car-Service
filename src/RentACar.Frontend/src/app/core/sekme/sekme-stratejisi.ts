import { Injectable, type ComponentRef } from '@angular/core';
import type {
  ActivatedRouteSnapshot,
  DetachedRouteHandle,
  RouteReuseStrategy,
} from '@angular/router';

import { sekmeAnahtari } from './sekme-anahtari';

/** Angular'ın `DetachedRouteHandle`'ı opak; içinde bileşen referansı taşır. */
interface AyrikTutucu {
  readonly componentRef?: ComponentRef<unknown>;
}

function bilesenRef(tutucu: DetachedRouteHandle | undefined): ComponentRef<unknown> | undefined {
  return (tutucu as AyrikTutucu | undefined)?.componentRef;
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
export class SekmeRotaStratejisi implements RouteReuseStrategy {
  private acik = new Set<string>();
  private readonly tutucular = new Map<string, DetachedRouteHandle>();

  /** Açık sekme anahtarları; artık açık olmayan ayrık bileşenler yok edilir. */
  acikAnahtarlariAyarla(anahtarlar: Iterable<string>): void {
    this.acik = new Set(anahtarlar);
    for (const [anahtar, tutucu] of [...this.tutucular]) {
      if (!this.acik.has(anahtar)) {
        this.tutucular.delete(anahtar);
        bilesenRef(tutucu)?.destroy();
      }
    }
  }

  /** Bu rotadan çıkılınca sayfası arka planda tutulacak mı (açık sekmesi var mı)? */
  saklanacakMi(rota: ActivatedRouteSnapshot): boolean {
    const anahtar = sekmeAnahtari(rota);
    return anahtar !== null && this.acik.has(anahtar);
  }

  /** Arka plandaki (ayrık) sekmenin bileşeni; yoksa `null`. */
  ayrikBilesen(anahtar: string): unknown {
    return bilesenRef(this.tutucular.get(anahtar))?.instance ?? null;
  }

  /** Çıkış: tüm ayrık bileşenler yok edilir, açık küme boşalır. */
  temizle(): void {
    this.acikAnahtarlariAyarla([]);
  }

  shouldDetach(rota: ActivatedRouteSnapshot): boolean {
    return this.saklanacakMi(rota);
  }

  store(rota: ActivatedRouteSnapshot, tutucu: DetachedRouteHandle | null): void {
    const anahtar = sekmeAnahtari(rota);
    if (anahtar === null) return;
    if (tutucu === null) {
      this.tutucular.delete(anahtar); // yeniden takıldı: artık ayrık değil
      return;
    }
    const eski = this.tutucular.get(anahtar);
    if (eski && eski !== tutucu) bilesenRef(eski)?.destroy();
    if (this.acik.has(anahtar)) this.tutucular.set(anahtar, tutucu);
    else bilesenRef(tutucu)?.destroy();
  }

  shouldAttach(rota: ActivatedRouteSnapshot): boolean {
    const anahtar = sekmeAnahtari(rota);
    return anahtar !== null && this.acik.has(anahtar) && this.tutucular.has(anahtar);
  }

  retrieve(rota: ActivatedRouteSnapshot): DetachedRouteHandle | null {
    const anahtar = sekmeAnahtari(rota);
    return anahtar === null ? null : (this.tutucular.get(anahtar) ?? null);
  }

  shouldReuseRoute(gelecek: ActivatedRouteSnapshot, simdiki: ActivatedRouteSnapshot): boolean {
    if (gelecek.routeConfig !== simdiki.routeConfig) return false;
    const anahtar = sekmeAnahtari(gelecek);
    return anahtar === null || anahtar === sekmeAnahtari(simdiki);
  }
}
