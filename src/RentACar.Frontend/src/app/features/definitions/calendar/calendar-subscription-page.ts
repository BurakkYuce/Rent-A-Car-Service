import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@jsverse/transloco';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { SubmitLock } from '@core/form/submit-lock';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { TemelStore } from '@core/veri/temel-store';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

type CalendarLink = Schema<'CalendarLinkDto'>;

/**
 * F11.2a takvim aboneliği (Blazor `TakvimAbonelik`, oturum): kullanıcının KENDİ iCal bağlantısı + yenile (eski
 * bağlantı iptal; onaylı). Bağlantı kişisel veri taşır — yalnız ekranda, hiçbir yere saklanmaz.
 */
@Component({
  selector: 'rc-calendar-subscription-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, PageBand],
  styleUrl: '../definitions.scss',
  template: `
    <rc-sayfa-bandi [baslik]="'tanimlar.calendar.baslik' | transloco" ikon="calendar" />
    <div class="rc-sayfa">
      @switch (link.tur()) {
        @case ('hata') {
          <div class="rc-form-hatalari" role="alert">
            {{ 'tanimlar.calendar.yuklenemedi' | transloco }} {{ link.hata()?.detay }}
            <button type="button" class="rc-dugme rc-dugme--kucuk" (click)="link.yenile()">
              {{ 'tanimlar.document.yenidenDene' | transloco }}
            </button>
          </div>
        }
        @case ('hazir') {
          <p class="not">{{ 'tanimlar.calendar.aciklama' | transloco }}</p>
          <div class="baglanti">
            <input
              class="rc-girdi"
              readonly
              [value]="link.veri()?.url ?? ''"
              [attr.aria-label]="'tanimlar.calendar.baglanti' | transloco"
              (focus)="selectAll($event)"
            />
            <button type="button" class="rc-dugme" (click)="copy()">
              {{ 'tanimlar.calendar.kopyala' | transloco }}
            </button>
          </div>
          <p class="not">{{ 'tanimlar.calendar.uyari' | transloco }}</p>
          @if (renewError()) {
            <p class="rc-form-mesaji rc-form-mesaji--hata" role="alert">{{ renewError() }}</p>
          }
          <div class="eylemler">
            <button
              type="button"
              class="rc-dugme rc-dugme--tehlike"
              [disabled]="renewing()"
              (click)="renew()"
            >
              {{ 'tanimlar.calendar.yenile' | transloco }}
            </button>
          </div>
        }
        @default {
          <p class="not" role="status">{{ 'form.tanim.yukleniyor' | transloco }}</p>
        }
      }
    </div>
  `,
})
export class CalendarSubscriptionPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();
  private readonly renewLock = new SubmitLock();

  protected readonly link = new TemelStore<CalendarLink>(() =>
    this.api.get<CalendarLink>('/api/ui/v1/takvim-abonelik'),
  );
  protected readonly renewing = this.renewLock.gonderiliyor;
  protected readonly renewError = signal<string | null>(null);

  constructor() {
    this.link.yukle();
  }

  protected selectAll(e: Event): void {
    (e.target as HTMLInputElement).select();
  }

  protected copy(): void {
    const url = this.link.veri()?.url;
    if (!url) return;
    void navigator.clipboard
      ?.writeText(url)
      .then(() => this.toast.basari(this.t('tanimlar.calendar.kopyalandi')))
      .catch(() => undefined);
  }

  protected async renew(): Promise<void> {
    const yes = await this.confirm.ask({
      baslik: this.t('tanimlar.calendar.yenileBaslik'),
      mesaj: this.t('tanimlar.calendar.yenileMesaj'),
      onayEtiketi: this.t('tanimlar.calendar.yenile'),
      tehlikeli: true,
    });
    if (!yes) return;
    this.renewError.set(null);
    this.renewLock
      .gonder((key) =>
        this.api.post<CalendarLink>('/api/ui/v1/takvim-abonelik/yenile', null, {
          islemAnahtari: key,
        }),
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.basari(this.t('tanimlar.calendar.yenilendi'));
          this.link.yenile();
        },
        error: (e: unknown) => this.renewError.set(toApiError(e).detay),
      });
  }
}
