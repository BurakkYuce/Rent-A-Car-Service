import { computed, inject, Injectable, signal, type Signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { sekmeAnahtari } from './sekme-anahtari';

/**
 * Sekmeli çalışma alanının (F3.2) ilk pakette duran küçük durumu: hangi sekme görünür ve sayfaların
 * verdiği sekme etiketleri. Kabuk (tembel parça) yazar; sayfalar ve `FetchPolicy` okur.
 */
@Injectable({ providedIn: 'root' })
export class SekmeDurumu {
  /**
   * Görünür sekmenin anahtarı. `null`: kabuk yok (giriş sayfası, birim testleri) → her sayfa görünür
   * sayılır. Kabuk içinde sekme olmayan sayfada `''` (arka plandaki sekmeler görünmez).
   */
  readonly etkinAnahtar = signal<string | null>(null);

  private readonly _onaGelme = signal<ReadonlyMap<string, number>>(new Map());
  /** Her sekmenin kaç kez öne geldiği (FetchPolicy `sekmeyeDonunce: 'yenile'`). */
  readonly onaGelme = this._onaGelme.asReadonly();

  private readonly _etiketler = signal<ReadonlyMap<string, string>>(new Map());
  /** Sayfaların verdiği etiketler. YALNIZ BELLEKTE — depoya yazılmaz (müşteri adı olabilir, KVKK). */
  readonly etiketler = this._etiketler.asReadonly();

  /** Görünür sekmeyi ayarlar; ÖNCEKİNDEN farklıysa öne gelme sayacı artar (sorgu değişimi saymaz). */
  etkinlestir(anahtar: string | null): void {
    if (this.etkinAnahtar() === anahtar) return;
    this.etkinAnahtar.set(anahtar);
    if (anahtar) {
      this._onaGelme.update((onceki) =>
        new Map(onceki).set(anahtar, (onceki.get(anahtar) ?? 0) + 1),
      );
    }
  }

  etiketAyarla(anahtar: string, etiket: string | null): void {
    this._etiketler.update((onceki) => {
      const yeni = new Map(onceki);
      if (etiket?.trim()) yeni.set(anahtar, etiket.trim());
      else yeni.delete(anahtar);
      return yeni;
    });
  }

  temizle(): void {
    this.etkinAnahtar.set(null);
    this._onaGelme.set(new Map());
    this._etiketler.set(new Map());
  }
}

export interface SekmeBaglami {
  /** Bu sayfanın sekme anahtarı; sekme sayfası değilse `null`. */
  readonly anahtar: string | null;
  /** Sayfa şu an görünür mü (arka plandaki sekmede `false`). `FetchPolicy` varsayılan olarak bunu kullanır. */
  readonly aktif: Signal<boolean>;
  /** Sekme kaç kez öne geldi (arka plandan dönüşte artar); sekme değilse hep 0. */
  readonly onaGelme: Signal<number>;
  /**
   * Sekme başlığı (ör. `Kira 2026260801001`). Bellekte kalır, depoya YAZILMAZ; `null` → rota başlığı.
   */
  etiketAyarla(etiket: string | null): void;
}

/**
 * Sayfanın sekme bağlamı — sayfa bileşeninde ya da onun `providers`'ındaki bir serviste, enjeksiyon
 * bağlamında çağrılır. Router yoksa (birim testi) sayfa her zaman görünür sayılır.
 */
export function sekmeBaglami(): SekmeBaglami {
  const durum = inject(SekmeDurumu);
  const rota = inject(ActivatedRoute, { optional: true });
  const anahtar = rota ? sekmeAnahtari(rota.snapshot) : null;
  return {
    anahtar,
    aktif: computed(() => {
      const etkin = durum.etkinAnahtar();
      return etkin === null || anahtar === null || etkin === anahtar;
    }),
    onaGelme: computed(() => (anahtar === null ? 0 : (durum.onaGelme().get(anahtar) ?? 0))),
    etiketAyarla: (etiket) => {
      if (anahtar !== null) durum.etiketAyarla(anahtar, etiket);
    },
  };
}
