import { DestroyRef, inject, Injectable, signal } from '@angular/core';

/**
 * Altı durum (Revlo `ToastStatus` karşılığı): `bekleme` süren bir işlemi gösterir ve
 * {@link ToastServisi.bitir} ile sonuç durumuna döner.
 */
export type ToastDurumu = 'basari' | 'bilgi' | 'uyari' | 'hata' | 'notr' | 'bekleme';

export interface ToastEylemi {
  readonly etiket: string;
  readonly calistir: () => void;
}

export interface ToastSecenekleri {
  readonly baslik?: string;
  /** Otomatik kapanma (ms); `0` = kalıcı. Verilmezse duruma göre varsayılan. */
  readonly sure?: number;
  readonly eylem?: ToastEylemi;
}

export interface Toast {
  readonly id: number;
  readonly durum: ToastDurumu;
  readonly mesaj: string;
  readonly baslik?: string;
  readonly eylem?: ToastEylemi;
}

/** Ekranda aynı anda en çok bu kadar toast; taşan en eski düşer. */
export const EN_FAZLA_TOAST = 4;

/** Varsayılan süreler: hata okunmadan kaybolmasın diye daha uzun; `bekleme` sonuca kadar kalır. */
export const VARSAYILAN_SURE: Readonly<Record<ToastDurumu, number>> = {
  basari: 4000,
  bilgi: 5000,
  notr: 4000,
  uyari: 7000,
  hata: 9000,
  bekleme: 0,
};

/** Ekran okuyucuya hemen okunacaklar (`role="alert"`); diğerleri kibar bölge (`role="status"`). */
export function aciliDurumMu(durum: ToastDurumu): boolean {
  return durum === 'hata' || durum === 'uyari';
}

/**
 * Toast bildirimleri (Revlo `ToastService`'ten, sade: signal listesi + zamanlayıcı). Yığın en fazla
 * {@link EN_FAZLA_TOAST}; aynı durum + mesaj ikinci kez gelirse yeni satır açılmaz, süresi tazelenir
 * (ör. aynı anda düşen üç 5xx tek toast). Üzerine gelince/odaklanınca süre durur (`duraklat`/`devamEt`).
 * Görünüm: `<rc-toast-alani>` (`@shared/toast/toast-alani`).
 */
@Injectable({ providedIn: 'root' })
export class ToastServisi {
  private readonly liste = signal<readonly Toast[]>([]);
  readonly toastlar = this.liste.asReadonly();

  private sonId = 0;
  private readonly zamanlayicilar = new Map<number, ReturnType<typeof setTimeout>>();
  private readonly sureler = new Map<number, number>();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.temizle());
  }

  goster(durum: ToastDurumu, mesaj: string, secenek: ToastSecenekleri = {}): number {
    const sure = secenek.sure ?? VARSAYILAN_SURE[durum];
    const ayni = this.liste().find(
      (t) => t.durum === durum && t.mesaj === mesaj && t.baslik === secenek.baslik && !t.eylem,
    );
    if (ayni && !secenek.eylem) {
      this.zamanla(ayni.id, sure);
      return ayni.id;
    }

    const toast: Toast = {
      id: ++this.sonId,
      durum,
      mesaj,
      ...(secenek.baslik === undefined ? {} : { baslik: secenek.baslik }),
      ...(secenek.eylem === undefined ? {} : { eylem: secenek.eylem }),
    };
    const yeni = [...this.liste(), toast];
    for (const dusen of yeni.slice(0, Math.max(0, yeni.length - EN_FAZLA_TOAST))) {
      this.zamanlayiciSil(dusen.id);
    }
    this.liste.set(yeni.slice(-EN_FAZLA_TOAST));
    this.zamanla(toast.id, sure);
    return toast.id;
  }

  basari(mesaj: string, secenek?: ToastSecenekleri): number {
    return this.goster('basari', mesaj, secenek);
  }

  bilgi(mesaj: string, secenek?: ToastSecenekleri): number {
    return this.goster('bilgi', mesaj, secenek);
  }

  uyari(mesaj: string, secenek?: ToastSecenekleri): number {
    return this.goster('uyari', mesaj, secenek);
  }

  hata(mesaj: string, secenek?: ToastSecenekleri): number {
    return this.goster('hata', mesaj, secenek);
  }

  /** Süren işlem: kalıcı `bekleme` toast'u açar; {@link bitir} ile sonuç durumuna çevrilir. */
  bekleme(mesaj: string, secenek?: Omit<ToastSecenekleri, 'sure'>): number {
    return this.goster('bekleme', mesaj, { ...secenek, sure: 0 });
  }

  /** `bekleme` toast'unu sonuca çevirir (aynı satır; ekran okuyucu yeni durumu duyar). */
  bitir(id: number, durum: Exclude<ToastDurumu, 'bekleme'>, mesaj: string): void {
    if (!this.liste().some((t) => t.id === id)) {
      this.goster(durum, mesaj);
      return;
    }
    this.liste.update((l) => l.map((t) => (t.id === id ? { ...t, durum, mesaj } : t)));
    this.zamanla(id, VARSAYILAN_SURE[durum]);
  }

  kapat(id: number): void {
    this.zamanlayiciSil(id);
    this.liste.update((l) => l.filter((t) => t.id !== id));
  }

  /** Eylem düğmesi: eylemi çalıştırır ve toast'u kapatır. */
  eylemCalistir(id: number): void {
    const toast = this.liste().find((t) => t.id === id);
    this.kapat(id);
    toast?.eylem?.calistir();
  }

  duraklat(id: number): void {
    const zamanlayici = this.zamanlayicilar.get(id);
    if (zamanlayici !== undefined) clearTimeout(zamanlayici);
    this.zamanlayicilar.delete(id);
  }

  devamEt(id: number): void {
    const sure = this.sureler.get(id);
    if (sure && !this.zamanlayicilar.has(id) && this.liste().some((t) => t.id === id)) {
      this.zamanla(id, sure);
    }
  }

  /** Hepsini kapatır (çıkışta). */
  temizle(): void {
    for (const zamanlayici of this.zamanlayicilar.values()) clearTimeout(zamanlayici);
    this.zamanlayicilar.clear();
    this.sureler.clear();
    this.liste.set([]);
  }

  private zamanla(id: number, sure: number): void {
    this.duraklat(id);
    this.sureler.set(id, sure);
    if (sure > 0)
      this.zamanlayicilar.set(
        id,
        setTimeout(() => this.kapat(id), sure),
      );
  }

  private zamanlayiciSil(id: number): void {
    this.duraklat(id);
    this.sureler.delete(id);
  }
}
