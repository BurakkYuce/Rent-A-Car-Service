import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import type { FormNotice } from './document-requests';

/**
 * Para formunun altındaki kalıcı not (toast kaybolur, bu kalır): 409 `mukerrer`'de YAZILMIŞ kayıt (no + tutar; aynı
 * içerik mi değil mi), ağ/5xx'te "sonuç bilinmiyor — aynı işlem aynı anahtarla gider". Kullanıcıyı ikinci işleme
 * yönlendiren metin YOK.
 */
@Component({
  selector: 'rc-document-notice',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `
    @if (notice(); as n) {
      <p
        class="rc-form-mesaji"
        [class.rc-form-mesaji--uyari]="n.tone === 'uyari'"
        [attr.role]="n.tone === 'uyari' ? 'alert' : 'status'"
      >
        @if (n.key === 'mevcut') {
          {{
            (n.params['durum'] === 'ayni' ? 'finansBelge.mevcutAyni' : 'finansBelge.mevcutFarkli')
              | transloco: n.params
          }}
        } @else {
          {{ 'finansBelge.sonucBilinmiyor' | transloco }}
        }
      </p>
    }
  `,
})
export class DocumentNotice {
  readonly notice = input<FormNotice | null>(null);
}
