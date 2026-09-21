import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/** Alana bağlanmayan hatalar (form üstünde ya da yan panelde). Boşsa hiçbir şey çizmez. */
@Component({
  selector: 'rc-form-hatalari',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `
    @if (hatalar().length > 0) {
      <div class="rc-form-hatalari" role="alert">
        <strong>{{ 'form.genelHata' | transloco }}</strong>
        <ul>
          @for (hata of hatalar(); track $index) {
            <li>{{ hata }}</li>
          }
        </ul>
      </div>
    }
  `,
})
export class FormHatalari {
  readonly hatalar = input.required<readonly string[]>();
}
