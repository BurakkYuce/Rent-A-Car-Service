import { ChangeDetectionStrategy, Component, booleanAttribute, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import type { MoneySubmissionState } from '@core/form/money-submission';
import { FormHatalari } from '@shared/form/form-hatalari';
import { Ikon } from '@shared/ikon/ikon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

import { MoneyNoticeView } from './money-notice';

/**
 * Para formunun alt şeridi (tüm para ekranlarında aynı): kalıcı not, alansız hatalar, gönder düğmesi ve bilinçli
 * "Vazgeç". Donmuş deneme varken düğme "Aynı işlemi tekrar gönder" olur ve formu değil kopyayı gönderir; vazgeçiş
 * onaylıdır (`MoneySubmission.abandon`) ve `abandoned` ile bildirilir (ekran yeniden okunur).
 *
 * `type="submit"`: düğme çevreleyen `<form (ngSubmit)>`'i tetikler; `button` (varsayılan): `send` yayar.
 */
@Component({
  selector: 'rc-money-submit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, FormHatalari, Ikon, MoneyNoticeView],
  template: `
    <rc-money-notice [notice]="submission().notice()" />
    <rc-form-hatalari [hatalar]="submission().errors()" />
    <div class="form__eylemler">
      <button
        [attr.type]="type()"
        class="rc-dugme rc-dugme--kucuk"
        [class.rc-dugme--birincil]="!secondary()"
        [disabled]="submission().sending() || disabled()"
        [attr.aria-busy]="submission().sending()"
        [attr.data-testid]="testId()"
        (click)="clicked()"
      >
        @if (ikon(); as i) {
          <rc-ikon [ad]="i" [boyut]="14" />
        }
        @if (submission().sending()) {
          {{ 'form.gonderiliyor' | transloco }}
        } @else if (submission().frozen()) {
          {{ retryLabel() ?? ('paraIslemi.tekrarGonder' | transloco) }}
        } @else {
          {{ label() }}
        }
      </button>
      @if (submission().frozen() && !submission().sending()) {
        <button
          type="button"
          class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
          (click)="abandon()"
        >
          {{ 'paraIslemi.vazgec' | transloco }}
        </button>
      }
      <ng-content />
    </div>
  `,
})
export class MoneySubmitBar {
  readonly submission = input.required<MoneySubmissionState>();
  /** Düğme etiketi (çevrilmiş metin). */
  readonly label = input.required<string>();
  /** Donmuş denemenin tekrar etiketi (çevrilmiş); boşsa "Aynı işlemi tekrar gönder". */
  readonly retryLabel = input<string | null>(null);
  /** Gönder düğmesinin `data-testid`'si (e2e). */
  readonly testId = input<string | null>(null);
  readonly type = input<'button' | 'submit'>('button');
  readonly ikon = input<IkonAdi | null>(null);
  readonly secondary = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly send = output<void>();
  readonly abandoned = output<void>();

  protected clicked(): void {
    if (this.type() === 'button') this.send.emit();
  }

  protected async abandon(): Promise<void> {
    if (await this.submission().abandon()) this.abandoned.emit();
  }
}
