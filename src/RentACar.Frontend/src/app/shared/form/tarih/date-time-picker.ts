import { ChangeDetectionStrategy, Component, ElementRef, signal, viewChild } from '@angular/core';
import { CdkConnectedOverlay, CdkOverlayOrigin } from '@angular/cdk/overlay';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  type DayText,
  mergeMoment,
  parseMoment,
  formatDay,
  parseDay,
  parseHour,
} from '@core/form/tarih-girdisi';
import { Icon } from '../../ikon/icon';
import { ParsingControl, controlProviders } from '../kontroller/base-control';
import { Calendar } from './calendar';

/**
 * Tarih-saat seçici. Değer UTC ANI (`"2026-09-22T11:30:00.000Z"`); kullanıcı İstanbul saatiyle görür
 * ve yazar. Ön doldurma sunucunun anından (ofsetli ISO) yapılır, YEREL saate çevrilip geri UTC
 * sayılmaz — Blazor'daki "hayalet +1 gün" hatasının kaynağı buydu. Tarih ve saat ayrı kutularda;
 * ikisi de doluysa değer bildirilir, biri eksik/bozuksa `tarihGecersiz`/`saatGecersiz`.
 */
@Component({
  selector: 'rc-tarih-saat-secici',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, TranslocoPipe, Icon, Calendar],
  providers: controlProviders(() => DateTimePicker, { dogrulayici: true }),
  styles: `
    :host {
      display: flex;
      gap: var(--rc-bosluk-2);
    }
    .gun {
      flex: 1 1 8rem;
    }
    .saat {
      flex: 0 0 5rem;
      text-align: center;
      font-variant-numeric: tabular-nums;
    }
    .panel {
      margin-block: var(--rc-bosluk-1);
    }
  `,
  template: `
    <div class="rc-girdi-kutusu gun" cdkOverlayOrigin #koken="cdkOverlayOrigin">
      <input
        class="rc-girdi"
        type="text"
        inputmode="numeric"
        autocomplete="off"
        [id]="itemId()"
        [value]="dayText()"
        [disabled]="pasif()"
        [attr.placeholder]="'form.tarih.bicim' | transloco"
        [attr.aria-label]="ariaEtiketi() ?? null"
        [attr.aria-invalid]="ariaInvalid()"
        [attr.aria-describedby]="ariaDescribedBy()"
        [attr.aria-required]="ariaRequired()"
        (input)="dayTyped($event)"
        (blur)="released()"
      />
      <button
        #dugme
        type="button"
        class="rc-girdi-eki"
        aria-haspopup="dialog"
        [attr.aria-expanded]="acik()"
        [attr.aria-label]="'form.tarih.takvimiAc' | transloco"
        [disabled]="pasif()"
        (click)="acik.set(!acik())"
      >
        <rc-ikon ad="calendar" [boyut]="14" />
      </button>
    </div>
    <input
      class="rc-girdi saat"
      type="text"
      inputmode="numeric"
      autocomplete="off"
      placeholder="ss:dd"
      [id]="itemId() + '-saat'"
      [value]="hourText()"
      [disabled]="pasif()"
      [attr.aria-label]="'form.tarih.saat' | transloco"
      [attr.aria-invalid]="ariaInvalid()"
      [attr.aria-describedby]="ariaDescribedBy()"
      [attr.aria-required]="ariaRequired()"
      (input)="hourTyped($event)"
      (blur)="released()"
    />
    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="koken"
      [cdkConnectedOverlayOpen]="acik()"
      (overlayOutsideClick)="outsideClick($event)"
      (overlayKeydown)="panelKeydown($event)"
      (attach)="takvim()?.focusOn()"
      (detach)="acik.set(false)"
    >
      <div
        class="rc-acilir-panel panel"
        role="dialog"
        [attr.aria-label]="'form.tarih.takvim' | transloco"
      >
        <rc-takvim [secili]="selectedDay()" (gunSecildi)="selectedFromCalendar($event)" />
      </div>
    </ng-template>
  `,
})
export class DateTimePicker extends ParsingControl<string> {
  private readonly dugme = viewChild.required<ElementRef<HTMLButtonElement>>('dugme');
  protected readonly takvim = viewChild(Calendar);

  protected readonly dayText = signal('');
  protected readonly hourText = signal('');
  protected readonly selectedDay = signal<DayText | null>(null);
  protected readonly acik = signal(false);

  protected override writtenExternally(value: string | null): void {
    const part = parseMoment(value);
    this.dayText.set(part ? formatDay(part.gun) : '');
    this.hourText.set(part?.saat ?? '');
    this.selectedDay.set(part?.gun ?? null);
    this.setError(null);
  }

  protected dayTyped(evt: Event): void {
    this.dayText.set((evt.target as HTMLInputElement).value);
    this.hesapla();
  }

  protected hourTyped(evt: Event): void {
    this.hourText.set((evt.target as HTMLInputElement).value);
    this.hesapla();
  }

  protected released(): void {
    if (this.parseError() === null) {
      const part = parseMoment(this.deger());
      if (part) {
        this.dayText.set(formatDay(part.gun));
        this.hourText.set(part.saat);
      }
    }
    this.touch();
  }

  protected selectedFromCalendar(day: DayText): void {
    this.dayText.set(formatDay(day));
    this.acik.set(false);
    this.dugme().nativeElement.focus();
    this.hesapla();
  }

  protected outsideClick(evt: MouseEvent): void {
    if (!this.dugme().nativeElement.contains(evt.target as Node)) this.acik.set(false);
  }

  protected panelKeydown(evt: KeyboardEvent): void {
    if (evt.key === 'Escape') {
      evt.preventDefault();
      this.acik.set(false);
      this.dugme().nativeElement.focus();
    }
  }

  private hesapla(): void {
    const day = parseDay(this.dayText());
    const hour = parseHour(this.hourText());
    this.selectedDay.set(day === 'gecersiz' ? null : day);
    if (day === null && hour === null) {
      this.setError(null);
      this.notify(null);
    } else if (day === null || day === 'gecersiz') {
      this.setError({ tarihGecersiz: true });
      this.notify(null);
    } else if (hour === null || hour === 'gecersiz') {
      this.setError({ saatGecersiz: true });
      this.notify(null);
    } else {
      this.setError(null);
      this.notify(mergeMoment(day, hour));
    }
  }
}
