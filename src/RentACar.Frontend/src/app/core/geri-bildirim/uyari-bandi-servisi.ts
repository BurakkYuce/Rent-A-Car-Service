import { Injectable, signal } from '@angular/core';

export type BantTuru = 'uyari' | 'hata' | 'bilgi';

export interface Bant {
  readonly tur: BantTuru;
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
export class UyariBandiServisi {
  private readonly deger = signal<Bant | null>(null);
  readonly bant = this.deger.asReadonly();

  private sayac = 0;
  private bantSirasi = 0;
  private zincirBaslangici: number | null = null;

  goster(bant: Bant): void {
    this.bantSirasi = ++this.sayac;
    this.deger.set(bant);
  }

  kapat(): void {
    this.deger.set(null);
  }

  /** Router `NavigationStart`: zincirin ilk adımında o ana kadarki bant sırası not edilir. */
  gezinmeBasladi(): void {
    this.zincirBaslangici ??= this.sayac;
  }

  /**
   * Router gezinmesi sonuçlandı. `yonlendirme` = guard yönlendirmesiyle iptal (zincir sürüyor).
   * `tamamlandi` = `NavigationEnd`: zincirden önceki kalıcı olmayan bant kapanır.
   */
  gezinmeBitti(sonuc: 'tamamlandi' | 'yonlendirme' | 'iptal'): void {
    if (sonuc === 'yonlendirme') return;
    const baslangic = this.zincirBaslangici;
    this.zincirBaslangici = null;
    if (sonuc !== 'tamamlandi' || baslangic === null) return;
    const bant = this.deger();
    if (bant && bant.kalici !== true && this.bantSirasi <= baslangic) this.deger.set(null);
  }
}
