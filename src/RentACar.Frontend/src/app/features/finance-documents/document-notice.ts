import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import type { FormNotice } from './document-requests';

/**
 * Para formunun altındaki kalıcı not (interceptor bu formlarda `mukerrer` toast'u göstermez — aynı metin iki yerde
 * çıkmasın): 409 `mukerrer`'de önceki denemenin kaydı (no + tutar; değiştirilen içerik yazılmadı), `mevcut`suz 409'da
 * "kaydedilmiş olabilir", ağ/5xx'te "sonuç bilinmiyor — aynı içerik aynı anahtarla gider". Kullanıcıyı ikinci işleme
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
        @switch (n.key) {
          @case ('kaydedildi') {
            {{ 'finansBelge.oncekiKaydedildi' | transloco: n.params }}
          }
          @case ('olabilir') {
            {{ 'finansBelge.kaydedilmisOlabilir' | transloco }}
          }
          @default {
            {{ 'finansBelge.sonucBilinmiyor' | transloco }}
          }
        }
      </p>
    }
  `,
})
export class DocumentNotice {
  readonly notice = input<FormNotice | null>(null);
}
