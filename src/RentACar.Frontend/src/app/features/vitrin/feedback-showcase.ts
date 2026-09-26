import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { type ToastState, ToastService } from '@core/geri-bildirim/toast-service';
import { type BannerType, WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';
import { requestContext } from '@core/oturum/request-context';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';

type Kontrol = 'plaka' | 'aciklama';

/**
 * Oturum ve geri bildirim vitrini (F3.3; F3.7 vitrini genişletir). Deneme formu
 * `POST /api/ui/v1/vitrin/kayit`'a gider — bu uç SUNUCUDA YOK; e2e onu Playwright ile sahteler ve
 * kod bazlı davranışı kilitler: doğrulama/çakışma formu korur, oturum düşünce yerinde giriş + aynı
 * istek (aynı `Idempotency-Key`) tekrarlanır, mükerrerde kayıt yeniden yüklenir. Gönderim F3.6
 * `formGonderimi` ile (alan hataları kontrollere, genel hatalar `rc-form-hatalari`'na). Canlıda uç 404 verir.
 */
@Component({
  selector: 'rc-geri-bildirim-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, RouterLink, FormErrors],
  templateUrl: './feedback-showcase.html',
  styleUrl: './feedback-showcase.scss',
})
export class FeedbackShowcase {
  private readonly api = inject(ApiIstemcisi);
  private readonly approval = inject(ConfirmService);
  protected readonly toast = inject(ToastService);
  private readonly bant = inject(WarningBannerService);
  private readonly t = translationFunction();

  protected readonly form = inject(NonNullableFormBuilder).group({ plaka: [''], aciklama: [''] });
  protected readonly submission = formSubmission();
  protected readonly sonuc = signal<string | null>(null);
  protected readonly refreshCount = signal(0);
  protected readonly confirmResult = signal<boolean | null>(null);
  /** Kontrol hataları signal değil: zoneless'ta şablon yanıttan sonra bununla yeniden çizilir. */
  private readonly responseCounter = signal(0);
  protected readonly statuses: readonly ToastState[] = [
    'basari',
    'bilgi',
    'uyari',
    'hata',
    'notr',
    'bekleme',
  ];

  protected readonly bannerTypes: readonly BannerType[] = ['uyari', 'hata', 'bilgi'];

  protected gonder(): void {
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.post<{ id: string }>('/api/ui/v1/vitrin/kayit', this.form.getRawValue(), {
          islemAnahtari: key,
          context: requestContext({
            mukerrerdeYenile: () => this.refreshCount.update((n) => n + 1),
          }),
        }),
      {
        basarili: (response) => {
          this.sonuc.set(response.id);
          this.toast.basari(this.t('vitrin.kaydedildi'));
        },
        hata: () => this.responseCounter.update((n) => n + 1),
      },
    );
  }

  protected fieldErrors(name: Kontrol): readonly string[] {
    this.responseCounter();
    const errors: unknown = this.form.controls[name].errors?.[SERVER_ERROR];
    return Array.isArray(errors) ? errors.filter((h): h is string => typeof h === 'string') : [];
  }

  protected showToast(status: ToastState): void {
    const message = this.t('vitrin.ornekToast');
    if (status === 'bekleme') {
      const id = this.toast.wait(message);
      setTimeout(() => this.toast.finish(id, 'basari', this.t('vitrin.kaydedildi')), 1500);
    } else {
      this.toast.show(status, message);
    }
  }

  protected showBanner(type: BannerType): void {
    this.bant.show({ tur: type, mesaj: this.t('vitrin.bantOrnek') });
  }

  protected async requestConfirm(): Promise<void> {
    this.confirmResult.set(
      await this.approval.ask({
        baslik: this.t('vitrin.onayBaslik'),
        mesaj: this.t('vitrin.onayMesaj'),
        tehlikeli: true,
      }),
    );
  }
}
