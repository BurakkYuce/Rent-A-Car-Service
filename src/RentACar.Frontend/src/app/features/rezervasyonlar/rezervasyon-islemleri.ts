import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { finalize, type Observable } from 'rxjs';

import { apiHatasinaCevir, type ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';

import {
  REZERVASYON_KOKU,
  type KirayaCevirYaniti,
  type RezervasyonDetayYaniti,
} from './rezervasyon-modeli';

/** Eylemin hedefi (liste satırı ya da detay): kimlik + ekranda görünen no. */
export interface RezervasyonHedefi {
  readonly id: string;
  readonly no: string;
}

/**
 * Rezervasyon durum eylemleri (onayla, iptal, kiraya çevir) — liste ve detay aynı kuralla çağırır (sayfanın
 * `providers`'ında). Uçlar anahtarsızdır; çift gönderimi durum makinesi YAPISAL reddeder (ikinci istek 400), SPA
 * ayrıca istek boyunca TÜM eylemleri kilitler (`suruyor`). Hata dalında ekran verisine dokunulmaz; iş kuralı
 * hatası (400 `dogrulama`) toast, bant/toast'ı interceptor'da olanlar (yetki, alansız `cakisma`, 5xx) orada.
 * Her sonuçta (başarı ya da hata) `sonra` çağrılır: kayıt yeniden okunur (durum başka yerde değişmiş olabilir).
 */
@Injectable()
export class RezervasyonIslemleri {
  private readonly api = inject(ApiIstemcisi);
  private readonly onay = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly router = inject(Router);
  private readonly yikim = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  /** İşlemdeki rezervasyonun kimliği (istek uçarken eylem düğmeleri pasif). */
  readonly suruyor = signal<string | null>(null);

  onayla(h: RezervasyonHedefi, sonra: () => void): void {
    this.calistir(
      h,
      this.api.post<RezervasyonDetayYaniti>(`${REZERVASYON_KOKU}/${h.id}/onayla`, null),
      () => this.toast.basari(this.t('rezervasyon.bildirim.onaylandi', { no: h.no })),
      sonra,
    );
  }

  async iptal(h: RezervasyonHedefi, sonra: () => void): Promise<void> {
    if (this.suruyor() !== null) return;
    const evet = await this.onay.sor({
      baslik: this.t('rezervasyon.onay.iptalBaslik'),
      mesaj: this.t('rezervasyon.onay.iptalMesaj', { no: h.no }),
      onayEtiketi: this.t('rezervasyon.onay.iptalOnay'),
      tehlikeli: true,
    });
    if (!evet) return;
    this.calistir(
      h,
      this.api.post<RezervasyonDetayYaniti>(`${REZERVASYON_KOKU}/${h.id}/iptal`, null),
      () => this.toast.basari(this.t('rezervasyon.bildirim.iptalEdildi', { no: h.no })),
      sonra,
    );
  }

  /**
   * Kiraya çevir: sunucu rezervasyonun fiyat taahhüdünü kiraya taşır ve kira kimliği döner → SPA kira formu
   * (`/kiralar/:id`, F4.3 rota sözleşmesi). Kira açan geri alınamaz işlem olduğu için onay sorulur; formda
   * kaydedilmemiş değişiklik varsa mesaj bunu söyler (çevirme KAYITLI hâli taşır).
   */
  async kirayaCevir(h: RezervasyonHedefi, sonra: () => void, kirli = false): Promise<void> {
    if (this.suruyor() !== null) return;
    const evet = await this.onay.sor({
      baslik: this.t('rezervasyon.onay.kirayaCevirBaslik'),
      mesaj: this.t(
        kirli ? 'rezervasyon.onay.kirayaCevirKirli' : 'rezervasyon.onay.kirayaCevirMesaj',
        {
          no: h.no,
        },
      ),
      onayEtiketi: this.t('rezervasyon.onay.kirayaCevirOnay'),
    });
    if (!evet) return;
    this.calistir(
      h,
      this.api.post<KirayaCevirYaniti>(`${REZERVASYON_KOKU}/${h.id}/kiraya-cevir`, null),
      (y) => {
        this.toast.basari(
          this.t('rezervasyon.bildirim.kirayaCevrildi', { no: h.no, sozlesme: y.sozlesmeNo }),
        );
        void this.router.navigate(['/kiralar', y.kiraId]);
      },
      sonra,
    );
  }

  private calistir<T>(
    h: RezervasyonHedefi,
    istek: Observable<T>,
    basarili: (y: T) => void,
    sonra: () => void,
  ): void {
    if (this.suruyor() !== null) return;
    this.suruyor.set(h.id);
    istek
      .pipe(
        finalize(() => this.suruyor.set(null)),
        takeUntilDestroyed(this.yikim),
      )
      .subscribe({
        next: (y) => {
          basarili(y);
          sonra();
        },
        error: (ham: unknown) => {
          this.hataGoster(apiHatasinaCevir(ham));
          sonra();
        },
      });
  }

  private hataGoster(hata: ApiHatasi): void {
    if (!genelGosterilir(hata)) this.toast.hata(hata.detay);
  }
}
