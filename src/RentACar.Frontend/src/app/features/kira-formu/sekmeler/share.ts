import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@jsverse/transloco';
import type { Observable } from 'rxjs';
import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { SubmitLock } from '@core/form/submit-lock';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { FORMAT_PIPES } from '@shared/bicim/bicim-pipe';
import { RentalFormState } from '../rental-form-state';

/**
 * Müşteriye verilecek sözleşme LİNKİ (Blazor "Sözleşme Linki" kartı; F4.1 paylaşım uçları). Link
 * tahmin edilemez ve iptal edilebilir; "yeni sürüm" ESKİ adresi öldürür (onay ister). Yalnız
 * OperationsWrite (sunucu `paylasim` barını yalnız o izinle doldurur).
 */
@Component({
  selector: 'rc-kf-paylasim',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, ...FORMAT_PIPES],
  template: `
    @if (d.visibleDetail()?.paylasim; as bar) {
      <section class="rc-bolum kf-kart" [attr.aria-label]="'kiraFormu.paylasim.baslik' | transloco">
        <h2 class="kf-kart__baslik">{{ 'kiraFormu.paylasim.baslik' | transloco }}</h2>
        @if (bar.link; as link) {
          <input
            class="rc-girdi"
            readonly
            [value]="adres()"
            [attr.aria-label]="'kiraFormu.paylasim.adres' | transloco"
            data-testid="paylasim-adresi"
          />
          <p class="kf-not">
            {{
              link.erisimSayisi === 0
                ? ('kiraFormu.paylasim.acilmadi' | transloco)
                : ('kiraFormu.paylasim.acildi' | transloco: { sayi: link.erisimSayisi })
            }}
            @if (link.sonErisimUtc) {
              · {{ link.sonErisimUtc | tarihSaat }}
            }
            @if (link.bayat) {
              <span class="rc-rozet rc-rozet--uyari">{{
                'kiraFormu.paylasim.bayat' | transloco
              }}</span>
            }
          </p>
          <div class="kf-eylemler">
            <button type="button" class="rc-dugme rc-dugme--kucuk" (click)="copy()">
              {{
                (copied() ? 'kiraFormu.paylasim.kopyalandi' : 'kiraFormu.paylasim.kopyala')
                  | transloco
              }}
            </button>
            <a class="rc-dugme rc-dugme--kucuk" [href]="link.yol" target="_blank" rel="noopener">{{
              'kiraFormu.paylasim.ac' | transloco
            }}</a>
            <button
              type="button"
              class="rc-dugme rc-dugme--kucuk"
              [disabled]="lockEntry.gonderiliyor()"
              (click)="newVersion()"
            >
              {{ 'kiraFormu.paylasim.yeniSurum' | transloco }}
            </button>
            <button
              type="button"
              class="rc-dugme rc-dugme--kucuk rc-dugme--tehlike"
              [disabled]="lockEntry.gonderiliyor()"
              (click)="iptal()"
            >
              {{ 'kiraFormu.paylasim.iptal' | transloco }}
            </button>
          </div>
        } @else {
          <p class="kf-not">{{ 'kiraFormu.paylasim.aciklama' | transloco }}</p>
          <div class="kf-eylemler">
            <button
              type="button"
              class="rc-dugme rc-dugme--kucuk"
              [disabled]="lockEntry.gonderiliyor()"
              (click)="olustur()"
            >
              {{ 'kiraFormu.paylasim.olustur' | transloco }}
            </button>
          </div>
        }
      </section>
    }
  `,
})
export class Share {
  protected readonly d = inject(RentalFormState);
  private readonly api = inject(ApiIstemcisi);
  private readonly approval = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly belge = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  protected readonly lockEntry = new SubmitLock();
  protected readonly copied = signal(false);
  /** Mutlak adres sayfanın kendi kökünden kurulur (sunucu yalnız göreli yol verir). */
  protected readonly adres = computed(() => {
    const path = this.d.visibleDetail()?.paylasim?.link?.yol ?? '';
    return `${this.belge.location?.origin ?? ''}${path}`;
  });

  private get root(): `/api/ui/v1/${string}` {
    return `/api/ui/v1/kiralar/${this.d.id ?? ''}/paylasim`;
  }

  protected olustur(): void {
    this.calistir(this.api.post(this.root, {}));
  }

  protected async newVersion(): Promise<void> {
    const ok = await this.approval.ask({
      baslik: this.t('kiraFormu.paylasim.yeniSurumBaslik'),
      mesaj: this.t('kiraFormu.paylasim.yeniSurumMesaj'),
      tehlikeli: true,
    });
    if (ok) this.calistir(this.api.post(`${this.root}/yeni-surum`, {}));
  }

  protected async iptal(): Promise<void> {
    const ok = await this.approval.ask({
      baslik: this.t('kiraFormu.paylasim.iptalBaslik'),
      mesaj: this.t('kiraFormu.paylasim.iptalMesaj'),
      tehlikeli: true,
    });
    if (ok) this.calistir(this.api.delete(this.root));
  }

  protected copy(): void {
    void this.belge.defaultView?.navigator.clipboard?.writeText(this.adres()).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1500);
    });
  }

  private calistir(request: Observable<unknown>): void {
    this.lockEntry
      .gonder(() => request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.d.yenile(),
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }
}
