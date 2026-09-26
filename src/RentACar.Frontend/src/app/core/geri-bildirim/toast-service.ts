import { DestroyRef, inject, Injectable, signal } from '@angular/core';

/**
 * Altı durum (Revlo `ToastStatus` karşılığı): `bekleme` süren bir işlemi gösterir ve
 * {@link ToastService.finish} ile sonuç durumuna döner.
 */
export type ToastState = 'basari' | 'bilgi' | 'uyari' | 'hata' | 'notr' | 'bekleme';

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
  readonly durum: ToastState;
  readonly mesaj: string;
  readonly baslik?: string;
  readonly eylem?: ToastEylemi;
}

/** Ekranda aynı anda en çok bu kadar toast; taşan en eski düşer. */
export const MAX_TOASTS = 4;

/** Varsayılan süreler: hata okunmadan kaybolmasın diye daha uzun; `bekleme` sonuca kadar kalır. */
export const DEFAULT_DURATION: Readonly<Record<ToastState, number>> = {
  basari: 4000,
  bilgi: 5000,
  notr: 4000,
  uyari: 7000,
  hata: 9000,
  bekleme: 0,
};

/** Ekran okuyucuya hemen okunacaklar (`role="alert"`); diğerleri kibar bölge (`role="status"`). */
export function isUrgentState(status: ToastState): boolean {
  return status === 'hata' || status === 'uyari';
}

/**
 * Toast bildirimleri (Revlo `ToastService`'ten, sade: signal listesi + zamanlayıcı). Yığın en fazla
 * {@link MAX_TOASTS}; aynı durum + mesaj ikinci kez gelirse yeni satır açılmaz, süresi tazelenir
 * (ör. aynı anda düşen üç 5xx tek toast). Üzerine gelince/odaklanınca süre durur (`duraklat`/`devamEt`).
 * Görünüm: `<rc-toast-alani>` (`@shared/toast/toast-alani`).
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly liste = signal<readonly Toast[]>([]);
  readonly toasts = this.liste.asReadonly();

  private lastId = 0;
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();
  private readonly durations = new Map<number, number>();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clear());
  }

  show(status: ToastState, message: string, option: ToastSecenekleri = {}): number {
    const duration = option.sure ?? DEFAULT_DURATION[status];
    const same = this.liste().find(
      (t) => t.durum === status && t.mesaj === message && t.baslik === option.baslik && !t.eylem,
    );
    if (same && !option.eylem) {
      this.schedule(same.id, duration);
      return same.id;
    }

    const toast: Toast = {
      id: ++this.lastId,
      durum: status,
      mesaj: message,
      ...(option.baslik === undefined ? {} : { baslik: option.baslik }),
      ...(option.eylem === undefined ? {} : { eylem: option.eylem }),
    };
    const newItem = [...this.liste(), toast];
    for (const dropped of newItem.slice(0, Math.max(0, newItem.length - MAX_TOASTS))) {
      this.clearTimer(dropped.id);
    }
    this.liste.set(newItem.slice(-MAX_TOASTS));
    this.schedule(toast.id, duration);
    return toast.id;
  }

  basari(message: string, option?: ToastSecenekleri): number {
    return this.show('basari', message, option);
  }

  bilgi(message: string, option?: ToastSecenekleri): number {
    return this.show('bilgi', message, option);
  }

  uyari(message: string, option?: ToastSecenekleri): number {
    return this.show('uyari', message, option);
  }

  hata(message: string, option?: ToastSecenekleri): number {
    return this.show('hata', message, option);
  }

  /** Süren işlem: kalıcı `bekleme` toast'u açar; {@link finish} ile sonuç durumuna çevrilir. */
  wait(message: string, option?: Omit<ToastSecenekleri, 'sure'>): number {
    return this.show('bekleme', message, { ...option, sure: 0 });
  }

  /** `bekleme` toast'unu sonuca çevirir (aynı satır; ekran okuyucu yeni durumu duyar). */
  finish(id: number, status: Exclude<ToastState, 'bekleme'>, message: string): void {
    if (!this.liste().some((t) => t.id === id)) {
      this.show(status, message);
      return;
    }
    this.liste.update((l) =>
      l.map((t) => (t.id === id ? { ...t, durum: status, mesaj: message } : t)),
    );
    this.schedule(id, DEFAULT_DURATION[status]);
  }

  kapat(id: number): void {
    this.clearTimer(id);
    this.liste.update((l) => l.filter((t) => t.id !== id));
  }

  /** Eylem düğmesi: eylemi çalıştırır ve toast'u kapatır. */
  runAction(id: number): void {
    const toast = this.liste().find((t) => t.id === id);
    this.kapat(id);
    toast?.eylem?.calistir();
  }

  pause(id: number): void {
    const timer = this.timers.get(id);
    if (timer !== undefined) clearTimeout(timer);
    this.timers.delete(id);
  }

  resume(id: number): void {
    const duration = this.durations.get(id);
    if (duration && !this.timers.has(id) && this.liste().some((t) => t.id === id)) {
      this.schedule(id, duration);
    }
  }

  /** Hepsini kapatır (çıkışta). */
  clear(): void {
    for (const timer of this.timers.values()) clearTimeout(timer);
    this.timers.clear();
    this.durations.clear();
    this.liste.set([]);
  }

  private schedule(id: number, duration: number): void {
    this.pause(id);
    this.durations.set(id, duration);
    if (duration > 0)
      this.timers.set(
        id,
        setTimeout(() => this.kapat(id), duration),
      );
  }

  private clearTimer(id: number): void {
    this.pause(id);
    this.durations.delete(id);
  }
}
