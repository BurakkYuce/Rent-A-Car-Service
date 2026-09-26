import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { finalize, type Observable } from 'rxjs';

import { toApiError, type ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';

import {
  RESERVATION_ROOT,
  type ConvertToRentalResponse,
  type ReservationDetailResponse,
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
export class ReservationActions {
  private readonly api = inject(ApiIstemcisi);
  private readonly approval = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly teardown = inject(DestroyRef);
  private readonly t = translationFunction();

  /** İşlemdeki rezervasyonun kimliği (istek uçarken eylem düğmeleri pasif). */
  readonly inProgress = signal<string | null>(null);

  onayla(h: RezervasyonHedefi, after: () => void): void {
    this.calistir(
      h,
      this.api.post<ReservationDetailResponse>(`${RESERVATION_ROOT}/${h.id}/onayla`, null),
      () => this.toast.basari(this.t('rezervasyon.bildirim.onaylandi', { no: h.no })),
      after,
    );
  }

  async iptal(h: RezervasyonHedefi, after: () => void): Promise<void> {
    if (this.inProgress() !== null) return;
    const yes = await this.approval.ask({
      baslik: this.t('rezervasyon.onay.iptalBaslik'),
      mesaj: this.t('rezervasyon.onay.iptalMesaj', { no: h.no }),
      onayEtiketi: this.t('rezervasyon.onay.iptalOnay'),
      tehlikeli: true,
    });
    if (!yes) return;
    this.calistir(
      h,
      this.api.post<ReservationDetailResponse>(`${RESERVATION_ROOT}/${h.id}/iptal`, null),
      () => this.toast.basari(this.t('rezervasyon.bildirim.iptalEdildi', { no: h.no })),
      after,
    );
  }

  /**
   * Kiraya çevir: sunucu rezervasyonun fiyat taahhüdünü kiraya taşır ve kira kimliği döner → SPA kira formu
   * (`/kiralar/:id`, F4.3 rota sözleşmesi). Kira açan geri alınamaz işlem olduğu için onay sorulur; formda
   * kaydedilmemiş değişiklik varsa mesaj bunu söyler (çevirme KAYITLI hâli taşır).
   */
  async kirayaCevir(h: RezervasyonHedefi, after: () => void, dirty = false): Promise<void> {
    if (this.inProgress() !== null) return;
    const yes = await this.approval.ask({
      baslik: this.t('rezervasyon.onay.kirayaCevirBaslik'),
      mesaj: this.t(
        dirty ? 'rezervasyon.onay.kirayaCevirKirli' : 'rezervasyon.onay.kirayaCevirMesaj',
        {
          no: h.no,
        },
      ),
      onayEtiketi: this.t('rezervasyon.onay.kirayaCevirOnay'),
    });
    if (!yes) return;
    this.calistir(
      h,
      this.api.post<ConvertToRentalResponse>(`${RESERVATION_ROOT}/${h.id}/kiraya-cevir`, null),
      (y) => {
        this.toast.basari(
          this.t('rezervasyon.bildirim.kirayaCevrildi', { no: h.no, sozlesme: y.sozlesmeNo }),
        );
        void this.router.navigate(['/kiralar', y.kiraId]);
      },
      after,
    );
  }

  private calistir<T>(
    h: RezervasyonHedefi,
    request: Observable<T>,
    successful: (y: T) => void,
    after: () => void,
  ): void {
    if (this.inProgress() !== null) return;
    this.inProgress.set(h.id);
    request
      .pipe(
        finalize(() => this.inProgress.set(null)),
        takeUntilDestroyed(this.teardown),
      )
      .subscribe({
        next: (y) => {
          successful(y);
          after();
        },
        error: (raw: unknown) => {
          this.showError(toApiError(raw));
          after();
        },
      });
  }

  private showError(error: ApiHatasi): void {
    if (!genelGosterilir(error)) this.toast.hata(error.detay);
  }
}
