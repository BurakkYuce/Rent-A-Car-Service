import { Injectable, signal } from '@angular/core';

/**
 * Menü (ve rozet sayaçları: okunmamış bildirim, yeni talep) tazeleme isteği. Kabuk menüyü 5 dk'da bir okur; rozeti
 * değiştiren bir işlemden sonra (bildirim okundu, talep işlendi) sayfa `request()` der, kabuk menüyü HEMEN yeniden
 * okur (kabul testi: rozet işlemden sonra güncellenmiyordu). Kabuk ek sayaç ucu çağırmaz; kaynak yine `GET /menu`.
 */
@Injectable({ providedIn: 'root' })
export class MenuRefresh {
  private readonly counter = signal(0);
  /** Her istekte artar (0 = henüz istek yok); kabuk bu sinyali izler. */
  readonly requests = this.counter.asReadonly();

  request(): void {
    this.counter.update((n) => n + 1);
  }
}
