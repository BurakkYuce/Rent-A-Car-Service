import { Injectable, signal } from '@angular/core';

export type BannerType = 'uyari' | 'hata' | 'bilgi';

export interface Bant {
  readonly tur: BannerType;
  readonly mesaj: string;
  /** Kaynağı olan sunucu `kod`'u (ör. `yetki_yok`, `pilot_degil`, `cakisma`) — test ve görünüm için. */
  readonly kod?: string;
  /** Kalıcı bant gezinmede kapanmaz (ör. pilot olmayan firma). */
  readonly kalici?: boolean;
}

/**
 * Sayfa üstü uyarı bandı: form hatası OLMAYAN durumlar (`yetki_yok`, `pilot_degil`, alansız `cakisma`,
 * `?hata=` mesajı). Tek bant; yenisi eskisinin yerini alır. Görünüm: `<rc-uyari-bandi>`.
 *
 * Gezinme kuralı: bir gezinme zinciri (guard yönlendirmeleri dahil) BAŞLAMADAN önce gösterilmiş,
 * kalıcı olmayan bant zincir tamamlanınca kapanır. Zincir sırasında gösterilen bant (ör. `izinGuard`
 * "yetkiniz yok" deyip ana sayfaya yönlendirdi) yeni sayfada kalır.
 */
@Injectable({ providedIn: 'root' })
export class WarningBannerService {
  private readonly deger = signal<Bant | null>(null);
  readonly bant = this.deger.asReadonly();

  private sayac = 0;
  private bannerQueue = 0;
  private chainStart: number | null = null;

  show(banner: Bant): void {
    this.bannerQueue = ++this.sayac;
    this.deger.set(banner);
  }

  kapat(): void {
    this.deger.set(null);
  }

  /** Router `NavigationStart`: zincirin ilk adımında o ana kadarki bant sırası not edilir. */
  navigationStarted(): void {
    this.chainStart ??= this.sayac;
  }

  /**
   * Router gezinmesi sonuçlandı. `yonlendirme` = guard yönlendirmesiyle iptal (zincir sürüyor).
   * `tamamlandi` = `NavigationEnd`: zincirden önceki kalıcı olmayan bant kapanır.
   */
  navigationEnded(result: 'tamamlandi' | 'yonlendirme' | 'iptal'): void {
    if (result === 'yonlendirme') return;
    const start = this.chainStart;
    this.chainStart = null;
    if (result !== 'tamamlandi' || start === null) return;
    const banner = this.deger();
    if (banner && banner.kalici !== true && this.bannerQueue <= start) this.deger.set(null);
  }
}
