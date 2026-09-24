import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { FormHatalari } from '@shared/form/form-hatalari';

import { DocumentNotice } from './document-notice';
import type { DocumentSubmission } from './document-submission';

/**
 * Para formunun alt şeridi: genel hatalar, kalıcı not ve gönder düğmesi. Donmuş deneme varken düğme "Aynı işlemi
 * tekrar gönder" olur (form kilitli; tekrar yalnız donmuş gövdeyle) ve bilinçli "Vazgeç" (onaylı) çıkar.
 */
@Component({
  selector: 'rc-document-submit-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, DocumentNotice, FormHatalari],
  template: `
    <rc-form-hatalari [hatalar]="submission().generalErrors()" />
    <rc-document-notice [notice]="submission().notice()" />
    <div class="form__eylemler">
      <button
        type="submit"
        class="rc-dugme rc-dugme--birincil rc-dugme--kucuk"
        [disabled]="submission().sending()"
      >
        @if (submission().sending()) {
          {{ 'form.gonderiliyor' | transloco }}
        } @else if (submission().frozen()) {
          {{ 'finansBelge.tekrarGonder' | transloco }}
        } @else {
          {{ label() }}
        }
      </button>
      @if (submission().frozen() && !submission().sending()) {
        <button
          type="button"
          class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
          (click)="abandon.emit()"
        >
          {{ 'finansBelge.vazgec' | transloco }}
        </button>
      }
      <ng-content />
    </div>
  `,
  styleUrl: './finance-documents.scss',
})
export class DocumentSubmitBar {
  readonly submission = input.required<DocumentSubmission>();
  readonly label = input.required<string>();
  readonly abandon = output<void>();
}
