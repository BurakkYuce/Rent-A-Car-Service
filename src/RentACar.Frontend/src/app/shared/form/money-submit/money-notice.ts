import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import type { MoneyNotice } from '@core/form/money-notice';

/**
 * Para formunun kalıcı notu (`MoneySubmission.notice`): sonuç bilinmiyor, önceki deneme kayıtlı ya da çağıranın bağlam
 * notu. Başlık + metin + (varsa) sunucunun kendi açıklaması. Uyarı tonu `alert`, bilgi `status`.
 */
@Component({
  selector: 'rc-money-notice',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `
    @if (notice(); as n) {
      <div
        class="rc-form-mesaji"
        [class.rc-form-mesaji--uyari]="n.tone === 'uyari'"
        [attr.role]="n.tone === 'uyari' ? 'alert' : 'status'"
      >
        @if (n.title) {
          <strong class="baslik">{{ n.title | transloco }}</strong>
        }
        <span class="metin">{{ n.message | transloco: n.params }}</span>
        @if (n.detail) {
          <span class="ayrinti">{{ n.detail }}</span>
        }
      </div>
    }
  `,
  styles: `
    .baslik,
    .metin,
    .ayrinti {
      display: block;
    }
    .ayrinti {
      margin-top: 0.25rem;
      opacity: 0.85;
    }
  `,
})
export class MoneyNoticeView {
  readonly notice = input<MoneyNotice | null>(null);
}
